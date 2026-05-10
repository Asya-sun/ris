using Manager.Models;
using Manager.Models.Database;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Bson;
using Shared.DTO;

namespace Manager.Services;

public class MongoRepository
{
    private readonly IMongoCollection<CrackRequestEntity> _collection;
    private readonly ILogger<MongoRepository> _logger;

    public MongoRepository(IConfiguration config, ILogger<MongoRepository> logger)
    {
        _logger = logger;

        var connectionString = Environment.GetEnvironmentVariable("MONGO_CONNECTION")
            ?? "mongodb://mongodb-primary:27017,mongodb-secondary-1:27017,mongodb-secondary-2:27017/?replicaSet=rs0&readPreference=secondaryPreferred";


        var settings = MongoClientSettings.FromConnectionString(connectionString);
        settings.GuidRepresentation = GuidRepresentation.Standard;

        var client = new MongoClient(settings);
        var database = client.GetDatabase("CrackHash");
        _collection = database.GetCollection<CrackRequestEntity>("Requests");

        var statusIndex = Builders<CrackRequestEntity>.IndexKeys.Ascending(r => r.Status);
        _collection.Indexes.CreateOne(new CreateIndexModel<CrackRequestEntity>(statusIndex));

        var requestIdIndex = Builders<CrackRequestEntity>.IndexKeys.Ascending(r => r.RequestId);
        _collection.Indexes.CreateOne(new CreateIndexModel<CrackRequestEntity>(requestIdIndex));

        _logger.LogInformation("MongoRepository initialized with replica set support");
    }

    public async Task Create(CrackRequestEntity entity)
    {
        try
        {
            await _collection.WithWriteConcern(WriteConcern.WMajority)
                .InsertOneAsync(entity);

            _logger.LogInformation("Request {RequestId} saved to MongoDB with majority write concern", entity.RequestId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save request {RequestId} to MongoDB", entity.RequestId);
            throw;
        }
    }

    public async Task<CrackRequestEntity?> GetById(Guid requestId)
    {
        try
        {

            var result = await _collection
    .Find(x => x.RequestId == requestId)
    .FirstOrDefaultAsync();


            if (result == null)
            {
                _logger.LogDebug("Request {RequestId} not found in MongoDB", requestId);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get request {RequestId} from MongoDB", requestId);
            throw;
        }
    }


    public async Task SetTaskStarted(Guid requestId)
    {
        var update = Builders<CrackRequestEntity>.Update
            .Set(x => x.Status, CrackStatus.IN_PROGRESS)
            .Set(x => x.StartedAt, DateTime.UtcNow)
            .Set(x => x.LastUpdatedAt, DateTime.UtcNow)
            .Inc(x => x.Version, 1);

        await _collection
            .WithWriteConcern(WriteConcern.WMajority)
            .UpdateOneAsync(
                x => x.RequestId == requestId,
                update);
    }

    public async Task AddSubTasks(
        Guid requestId,
        List<SubTaskEntity> subTasks)
    {
        if (!subTasks.Any())
            return;

        var update = Builders<CrackRequestEntity>.Update
            .PushEach(x => x.SubTasks, subTasks)
            .Set(x => x.LastUpdatedAt, DateTime.UtcNow)
            .Inc(x => x.Version, 1);

        await _collection
            .WithWriteConcern(WriteConcern.WMajority)
            .UpdateOneAsync(
                x => x.RequestId == requestId,
                update);
    }

    public async Task MarkTaskStatus(Guid requestId, CrackStatus status, string errorMessage)
    {
        var updateBuilder = Builders<CrackRequestEntity>.Update;
        var updateDefinitions = new List<UpdateDefinition<CrackRequestEntity>>
    {
        updateBuilder.Set(x => x.Status, status),
        updateBuilder.Set(x => x.LastUpdatedAt, DateTime.UtcNow),
        updateBuilder.Inc(x => x.Version, 1)
    };

        if (errorMessage != null)
        {
            updateDefinitions.Add(updateBuilder.Set(x => x.ErrorMessage, errorMessage));
        }

        var update = updateBuilder.Combine(updateDefinitions);

        await _collection
            .WithWriteConcern(WriteConcern.WMajority)
            .UpdateOneAsync(
                x => x.RequestId == requestId,
                update);
    }

    public async Task<List<CrackRequestEntity>> GetPendingRequests()
    {
        try
        {
            var filter = Builders<CrackRequestEntity>.Filter.Eq(r => r.Status, CrackStatus.PENDING);

            var result = await _collection.WithReadPreference(ReadPreference.SecondaryPreferred)
                .Find(filter)
                .ToListAsync();

            _logger.LogInformation("Found {Count} pending requests to restore", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pending requests from MongoDB");
            throw;
        }
    }

    public async Task ResetTimedOutSubtasks(
    Guid requestId,
    List<Guid> subTaskIds)
    {
        foreach (var subTaskId in subTaskIds)
        {
            var filter = Builders<CrackRequestEntity>.Filter.Eq(
                x => x.RequestId,
                requestId);

            var update = Builders<CrackRequestEntity>.Update
                .Set("subTasks.$[st].status", SubTaskStatus.PENDING)
                .Set("subTasks.$[st].workerId", (Guid?)null)
                .Set("subTasks.$[st].lastHeartbeat", (DateTime?)null)
                .Set("lastUpdatedAt", DateTime.UtcNow)
                .Inc("subTasks.$[st].retryCount", 1);

            var options = new UpdateOptions
            {
                ArrayFilters = new[]
                {
                new BsonDocumentArrayFilterDefinition<BsonDocument>(
                    new BsonDocument(
                        "st.subTaskId",new BsonBinaryData(subTaskId, GuidRepresentation.Standard)))
            }
            };

            await _collection.UpdateOneAsync(
                filter,
                update,
                options);
        }
    }
    public async Task<List<CrackRequestEntity>> GetRequestsWithTimedOutSubtasks(TimeSpan timeout)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow - timeout;

            var filter = Builders<CrackRequestEntity>.Filter.And(
                Builders<CrackRequestEntity>.Filter.Eq(
                    r => r.Status,
                    CrackStatus.IN_PROGRESS),

                Builders<CrackRequestEntity>.Filter.ElemMatch(
                    r => r.SubTasks,
                    st =>
                        st.Status == SubTaskStatus.IN_PROGRESS &&
                        st.LastHeartbeat != null &&
                        st.LastHeartbeat.Value < cutoffTime)
            );

            var result = await _collection
                .WithReadPreference(ReadPreference.SecondaryPreferred)
                .Find(filter)
                .ToListAsync();

            if (result.Any())
            {
                _logger.LogWarning(
                    "Found {Count} requests with timed out subtasks",
                    result.Count);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to get requests with timed out subtasks");

            throw;
        }
    }


    public async Task<bool> UpdateSubtaskProgress(RabbitProgressMessage progress)
    {
        try
        {
            if (progress == null)
                return false;


            var filter = Builders<CrackRequestEntity>.Filter.And(
                        Builders<CrackRequestEntity>.Filter.Eq(
                            x => x.RequestId,
                            progress.RequestId),

                        Builders<CrackRequestEntity>.Filter.ElemMatch(
                            x => x.SubTasks,
                            st =>
                                st.SubTaskId == progress.SubTaskId &&
                                (st.WorkerId == null || st.WorkerId == progress.WorkerId))
                    );


            var arrayFilter = new[]
            {
                new BsonDocumentArrayFilterDefinition<BsonDocument>(
                    new BsonDocument("st.SubTaskId",new BsonBinaryData(progress.SubTaskId, GuidRepresentation.Standard) ))
            };

            var update = Builders<CrackRequestEntity>.Update
                .Set("subTasks.$[st].currentIndex", progress.CurrentIndex)
                .Set("subTasks.$[st].lastHeartbeat", DateTime.UtcNow)
                .Set("subTasks.$[st].workerId", progress.WorkerId)
                .Set("subTasks.$[st].status", progress.IsCompleted
                    ? SubTaskStatus.COMPLETED
                    : SubTaskStatus.IN_PROGRESS)
                .Set("lastUpdatedAt", DateTime.UtcNow)
              .Inc(x => x.Version, 1);


            if (progress.IsCompleted)
            {
                update = update
                    .Set("subTasks.$[st].status", SubTaskStatus.COMPLETED)
                    .Set("subTasks.$[st].completedAt", DateTime.UtcNow)
                    .Set("subTasks.$[st].currentIndex", progress.CurrentIndex);
            }


            if (progress.FoundWords?.Any() == true)
            {
                update = update.AddToSetEach(x => x.FoundWords, progress.FoundWords);
            }


            var result = await _collection
                .WithWriteConcern(WriteConcern.WMajority)
                .UpdateOneAsync(
                    filter,
                    update,
                    new UpdateOptions
                    {
                        ArrayFilters = arrayFilter
                    });


            bool wasUpdated = result.ModifiedCount > 0;

            if (wasUpdated)
            {
                _logger.LogDebug("SubTask {SubTaskId} updated successfully", progress.SubTaskId);
            }
            else
            {
                _logger.LogDebug("SubTask {SubTaskId} NOT FOUND in document {RequestId}. " +
                                  "This may indicate mismatch of Guid representation.",
                                  progress.SubTaskId, progress.RequestId);
            }

            return wasUpdated;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update subtask progress {SubTaskId}", progress?.SubTaskId);
            throw;
        }
    }

    public async Task CheckAndMarkTaskCompleted(Guid requestId)
    {
        const int maxRetries = 3;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var entity = await GetById(requestId);

                if (entity == null)
                    return;

                long totalChecked = 0;
                bool allCompleted = true;

                foreach (var st in entity.SubTasks)
                {
                    if (st.Status == SubTaskStatus.COMPLETED)
                    {
                        totalChecked +=
                            (long)(st.EndIndex - st.StartIndex + 1);
                    }
                    else
                    {
                        allCompleted = false;

                        long checkedNow =
                        Math.Max(
                            0,
                            st.CurrentIndex - st.StartIndex);

                        totalChecked += checkedNow;
                    }
                }

                int progressPercent = entity.TotalCombinations > 0
                    ? (int)((double)totalChecked
                        / entity.TotalCombinations * 100)
                    : 0;

                if (progressPercent > 100)
                {
                    progressPercent = 100;
                }

                var filter = Builders<CrackRequestEntity>.Filter.And(
                    Builders<CrackRequestEntity>.Filter.Eq(
                        x => x.RequestId,
                        requestId),

                    Builders<CrackRequestEntity>.Filter.Eq(
                        x => x.Version,
                        entity.Version)
                );

                var update = Builders<CrackRequestEntity>.Update
                    .Set(x => x.CheckedCombinations, totalChecked)
                    .Set(x => x.LastUpdatedAt, DateTime.UtcNow)
                    .Set(x => x.Progress, progressPercent)
                    .Inc(x => x.Version, 1);

                if (allCompleted &&
                    entity.Status != CrackStatus.READY)
                {
                    update = update
                        .Set(x => x.Status, CrackStatus.READY)
                        .Set(x => x.CompletedAt, DateTime.UtcNow);

                    _logger.LogInformation(
                        "TASK {RequestId} COMPLETED! Found {FoundCount} words: {Words}",
                        requestId,
                        entity.FoundWords.Count,
                        string.Join(", ", entity.FoundWords));
                }

                var result = await _collection
                    .WithWriteConcern(WriteConcern.WMajority)
                    .UpdateOneAsync(filter, update);


                if (result.ModifiedCount > 0)
                {
                    if (allCompleted)
                    {
                        _logger.LogInformation(
                            "Task {RequestId} successfully marked as READY",
                            requestId);
                    }

                    return;
                }

                _logger.LogWarning(
                    "Race condition detected while updating task {RequestId}. Retry attempt {Attempt}",
                    requestId,
                    attempt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to check/mark completion for task {RequestId}",
                    requestId);

                return;
            }
        }

        _logger.LogError(
            "Failed to update task {RequestId} after retries",
            requestId);
    }



    public async Task<List<CrackRequestEntity>> GetRequestsWithUnpublishedSubtasks()
    {
        try
        {
            var filter = Builders<CrackRequestEntity>.Filter.And(
                Builders<CrackRequestEntity>.Filter.Eq(r => r.Status, CrackStatus.IN_PROGRESS),
                Builders<CrackRequestEntity>.Filter.ElemMatch(r => r.SubTasks,
                    st => st.Status == SubTaskStatus.PENDING && st.WorkerId == null)
            );
            var result = await _collection.Find(filter).ToListAsync();
            _logger.LogInformation("Found {Count} requests with unpublished subtasks", result.Count);
            return result;

        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to get requests with unpublished subtasks");

            throw;
        }

    }

}