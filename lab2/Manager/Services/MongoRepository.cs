using Manager.Models;
using Manager.Models.Database;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Bson;
using Shared.DTO;

namespace Manager.Services;


/// <summary>
/// MongoDB repository for managing password cracking tasks and their subtasks.
/// </summary>
/// <remarks>
/// <para>
/// This repository provides thread-safe CRUD operations for crack request entities with:
/// <list type="bullet">
/// <item><description>Optimistic concurrency control using Version field</description></item>
/// <item><description>Majority write concern for data durability</description></item>
/// <item><description>Secondary preferred read preference for read scalability</description></item>
/// <item><description>Replica set support for high availability</description></item>
/// </list>
/// </para>
/// <para>
/// The repository manages two main entities:
/// <list type="number">
/// <item><description><b>CrackRequestEntity</b> - Main task containing hash, status, and found passwords</description></item>
/// <item><description><b>SubTaskEntity</b> - Individual work unit assigned to workers</description></item>
/// </list>
/// </para>
/// </remarks>
public class MongoRepository
{
    private readonly IMongoCollection<CrackRequestEntity> _collection;
    private readonly ILogger<MongoRepository> _logger;



    /// <summary>
    /// Initializes a new instance of the MongoRepository with replica set support.
    /// </summary>
    /// <param name="config">Configuration for MongoDB connection settings (not used directly, uses environment variables).</param>
    /// <param name="logger">Logger for diagnostic operations.</param>
    /// <remarks>
    /// <para>
    /// Connection string priority:
    /// <list type="number">
    /// <item><description>Environment variable "MONGO_CONNECTION" (highest priority)</description></item>
    /// <item><description>Default replica set connection string (mongodb-primary:27017,...)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Creates indexes on:
    /// <list type="bullet">
    /// <item><description>Status field - for querying pending tasks</description></item>
    /// <item><description>RequestId field - for fast lookups by ID</description></item>
    /// </list>
    /// </para>
    /// </remarks>
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


    /// <summary>
    /// Creates a new crack request entity in the database.
    /// </summary>
    /// <param name="entity">The crack request entity to persist.</param>
    /// <remarks>
    /// Uses <b>majority write concern</b> to ensure data is committed to at least
    /// the majority of replica set members before acknowledging the write.
    /// </remarks>
    /// <exception cref="Exception">Thrown when MongoDB operation fails. Logs error details before rethrowing.</exception>
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



    /// <summary>
    /// Retrieves a crack request by its unique identifier.
    /// </summary>
    /// <param name="requestId">The unique identifier of the request.</param>
    /// <returns>The request entity if found; otherwise, null.</returns>
    /// <remarks>
    /// Uses default read preference. Returns null instead of throwing when entity not found.
    /// </remarks>
    /// <exception cref="Exception">Thrown when MongoDB operation fails (connection issues, etc.).</exception>
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

    /// <summary>
    /// Marks a task as started (IN_PROGRESS) and records start time.
    /// </summary>
    /// <param name="requestId">The unique identifier of the request.</param>
    /// <remarks>
    /// Updates are performed with optimistic concurrency control by incrementing Version field.
    /// Sets StartedAt timestamp and LastUpdatedAt to current UTC time.
    /// </remarks>
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



    /// <summary>
    /// Adds a list of subtasks to an existing crack request.
    /// </summary>
    /// <param name="requestId">The parent request identifier.</param>
    /// <param name="subTasks">List of subtask entities to add.</param>
    /// <remarks>
    /// Uses MongoDB's $pushEach to add multiple subtasks in a single operation.
    /// Does nothing if subTasks list is empty.
    /// </remarks>
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


    /// <summary>
    /// Updates the status of a crack request.
    /// </summary>
    /// <param name="requestId">The unique identifier of the request.</param>
    /// <param name="status">New status to set (PENDING, IN_PROGRESS, READY, or ERROR).</param>
    /// <param name="errorMessage">Optional error message (only relevant when status is ERROR).</param>
    /// <remarks>
    /// If errorMessage is provided, it will be stored in the ErrorMessage field.
    /// Always updates LastUpdatedAt and increments Version for concurrency control.
    /// </remarks>
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


    /// <summary>
    /// Retrieves all tasks with PENDING status.
    /// </summary>
    /// <returns>List of pending crack request entities.</returns>
    /// <remarks>
    /// <para>
    /// A <b>pending task</b> is a task that has been created but not yet started execution.
    /// </para>
    /// <para>
    /// Uses <b>SecondaryPreferred</b> read preference to offload read load from primary node.
    /// This is safe for recovery operations as slight staleness is acceptable.
    /// </para>
    /// </remarks>
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


    /// <summary>
    /// Resets timed-out subtasks to PENDING state for retry.
    /// </summary>
    /// <param name="requestId">The parent request identifier.</param>
    /// <param name="subTaskIds">List of subtask IDs to reset.</param>
    /// <remarks>
    /// <para>
    /// For each specified subtask:
    /// <list type="bullet">
    /// <item><description>Status changes from IN_PROGRESS to PENDING</description></item>
    /// <item><description>WorkerId is cleared (null)</description></item>
    /// <item><description>LastHeartbeat is cleared</description></item>
    /// <item><description>RetryCount is incremented by 1</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Uses MongoDB array filters to update specific elements in the SubTasks array.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Finds all IN_PROGRESS tasks that contain timed-out subtasks.
    /// </summary>
    /// <param name="timeout">The time threshold for considering a subtask as timed out.</param>
    /// <returns>List of crack request entities with at least one timed-out subtask.</returns>
    /// <remarks>
    /// <para>
    /// A <b>timed-out subtask</b> satisfies ALL of:
    /// <list type="bullet">
    /// <item><description>Status is IN_PROGRESS</description></item>
    /// <item><description>LastHeartbeat is not null</description></item>
    /// <item><description>LastHeartbeat is older than DateTime.UtcNow - timeout</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Uses <b>ElemMatch</b> filter to find documents with subtasks meeting the timeout criteria.
    /// Reads from secondaries via SecondaryPreferred preference for better performance.
    /// </para>
    /// </remarks>
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


    /// <summary>
    /// Updates a subtask's progress and optionally marks it as completed.
    /// </summary>
    /// <param name="progress">Progress message containing subtask state.</param>
    /// <returns>True if update was applied; false if subtask not found (e.g., wrong workerId).</returns>
    /// <remarks>
    /// <para>
    /// Update operations:
    /// <list type="bullet">
    /// <item><description>Updates CurrentIndex and LastHeartbeat</description></item>
    /// <item><description>Sets WorkerId (first assignment)</description></item>
    /// <item><description>If completed: sets status to COMPLETED and records CompletedAt timestamp</description></item>
    /// <item><description>If found words are present: adds them to FoundWords set (duplicates prevented)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Uses optimistic concurrency check: only updates if subtask belongs to the correct worker
    /// (WorkerId null OR matching progress.WorkerId). This prevents race conditions where
    /// two workers process the same subtask.
    /// </para>
    /// </remarks>
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


    /// <summary>
    /// Checks if all subtasks of a request are completed and marks the main task as READY.
    /// </summary>
    /// <param name="requestId">The unique identifier of the request to check.</param>
    /// <remarks>
    /// <para>
    /// This method implements <b>optimistic concurrency control</b> with retry logic:
    /// <list type="number">
    /// <item><description>Fetches current entity with Version</description></item>
    /// <item><description>Calculates total checked combinations across all subtasks</description></item>
    /// <item><description>Updates only if Version hasn't changed (prevents lost updates)</description></item>
    /// <item><description>Retries up to 3 times if race condition detected</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Progress calculation:
    /// <list type="bullet">
    /// <item><description>Completed subtasks contribute full chunk size (EndIndex - StartIndex + 1)</description></item>
    /// <item><description>In-progress subtasks contribute (CurrentIndex - StartIndex)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// When all subtasks are completed:
    /// <list type="bullet">
    /// <item><description>Status changes to READY</description></item>
    /// <item><description>CompletedAt timestamp is set</description></item>
    /// <item><description>Logs all found passwords</description></item>
    /// </list>
    /// </para>
    /// </remarks>
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


    /// <summary>
    /// Retrieves all IN_PROGRESS requests that have unpublished subtasks.
    /// </summary>
    /// <returns>List of requests with at least one pending, unassigned subtask.</returns>
    /// <remarks>
    /// <para>
    /// A <b>pending publish</b> is a subtask with:
    /// <list type="bullet">
    /// <item><description>Status = PENDING</description></item>
    /// <item><description>WorkerId = null (never assigned/not yet published)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// These subtasks failed to be sent to RabbitMQ during initial dispatch and need retry.
    /// Used by <see cref="RetryPendingPublishes"/> after RabbitMQ becomes available again.
    /// </para>
    /// </remarks>
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