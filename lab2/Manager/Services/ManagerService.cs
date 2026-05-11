using Manager.DTO;
using Manager.Models.Database;
using Manager.Models;
using Microsoft.Extensions.Options;
using Shared.DTO;

namespace Manager.Services;

public class ManagerService : IManagerService
{
    private readonly MongoRepository _mongoRepo;
    private readonly RabbitMqTaskPublisher _rabbitPublisher;

    private readonly ILogger<ManagerService> _logger;
    private readonly ManagerConfig _config;


    public ManagerService(
    IOptions<ManagerConfig> config,
    ILogger<ManagerService> logger,
    MongoRepository mongoRepo,
    RabbitMqTaskPublisher rabbitPublisher)
    {
        _config = config.Value;
        _logger = logger;
        _mongoRepo = mongoRepo;
        _rabbitPublisher = rabbitPublisher;
    }
    /// <inheritdoc />
    public async Task<Guid> CreateCrackTask(ManagerCrackRequest request)
    {
        var id = Guid.NewGuid();

        _logger.LogInformation("Creating crack task {RequestId}", id);

        var total = CalculateTotalCombinations(request.MaxLength);

        _logger.LogInformation("Task {RequestId}: total combinations = {Total}", id, total);

        var entity = new CrackRequestEntity
        {
            RequestId = id,
            Hash = request.Hash,
            MaxLength = request.MaxLength,
            Status = CrackStatus.PENDING,
            TotalCombinations = (long)total,
            CheckedCombinations = 0,
            FoundWords = new List<string>(),
            CreatedAt = DateTime.UtcNow,
            SubTasks = new List<SubTaskEntity>()
        };

        await _mongoRepo.Create(entity);

        _logger.LogInformation("Dispatching task {RequestId} to queue", id);

        await CreateAndDispatchSubTasks(id, request.Hash, request.MaxLength, total, entity);


        await _mongoRepo.MarkTaskStatus(id, CrackStatus.IN_PROGRESS, null);

        _logger.LogInformation("Task {RequestId} dispatched and set to IN_PROGRESS", id);

        return id;
    }


    private async Task CreateAndDispatchSubTasks(Guid requestId, string hash, int maxLength, long total, CrackRequestEntity entity)
    {
        var workerCount = _config.WorkerNumber;
        long chunkSize = total / workerCount;
        var subTasks = new List<SubTaskEntity>();


        for (int i = 0; i < workerCount; i++)
        {
            long start = i * chunkSize;
            long end = (i == workerCount - 1) ? total - 1 : (i + 1) * chunkSize - 1;

            var subTask = new SubTaskEntity
            {
                SubTaskId = Guid.NewGuid(),
                StartIndex = start,
                EndIndex = end,
                CurrentIndex = start,
                Status = SubTaskStatus.PENDING,
                RetryCount = 0
            };

            subTasks.Add(subTask);
        }

        _logger.LogWarning("BEFORE UPDATE: Task has {Count} subtasks", entity.SubTasks.Count);

        await _mongoRepo.AddSubTasks(
            requestId,
            subTasks);


        foreach (var subTask in subTasks)
        {
            try
            {
                _rabbitPublisher.PublishTask(
                    new RabbitTaskMessage
                    {
                        RequestId = requestId,
                        SubTaskId = subTask.SubTaskId,
                        Hash = hash,
                        MaxLength = maxLength,
                        StartIndex = subTask.StartIndex,
                        EndIndex = subTask.EndIndex,
                        CreatedAt = DateTime.UtcNow
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish subtask {SubTaskId} to RabbitMQ. It will be retried later.", subTask.SubTaskId);

            }


        }

    }


    /// <inheritdoc />
    public async Task<ManagerStatusResponse> GetStatus(Guid requestId)
    {
        if (requestId == Guid.Empty)
        {
            _logger.LogError("Empty requestId provided");
            throw new ArgumentException("RequestId cannot be empty", nameof(requestId));
        }

        var entity = await _mongoRepo.GetById(requestId);
        if (entity == null)
        {
            _logger.LogError("Task {RequestId} not found", requestId);
            throw new KeyNotFoundException($"Task {requestId} not found");
        }

        _logger.LogInformation("Task {RequestId} status: {Status}, checked: {Checked}/{Total}",
            requestId, entity.Status, entity.CheckedCombinations, entity.TotalCombinations);

        int progress = (int)(100.0 * entity.CheckedCombinations / entity.TotalCombinations);

        var status = entity.Status switch
        {
            CrackStatus.PENDING => "IN_PROGRESS",
            CrackStatus.IN_PROGRESS => "IN_PROGRESS",
            CrackStatus.READY => "READY",
            CrackStatus.ERROR => "ERROR",
            _ => "IN_PROGRESS"
        };

        return new ManagerStatusResponse(
            status,
            progress,
            entity.Status == CrackStatus.READY ? entity.FoundWords : null
        );
    }


    /// <inheritdoc />
    public async Task ProcessProgress(RabbitProgressMessage progress)
    {
        if (progress == null)
            return;

        _logger.LogInformation(
            "Processing progress: task={RequestId}, subtask={SubTaskId}, currentIndex={CurrentIndex}, found={FoundCount}",
            progress.RequestId, progress.SubTaskId, progress.CurrentIndex, progress.FoundWords.Count);

        try
        {
            var updated = await _mongoRepo.UpdateSubtaskProgress(progress);

            if (updated && progress.IsCompleted)
            {
                await _mongoRepo.CheckAndMarkTaskCompleted(progress.RequestId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process progress for task {RequestId}", progress.RequestId);
        }
    }


    private long CalculateTotalCombinations(int maxLength)
    {
        long alphabet = _config.Alphabet.Length;
        long total = 0;

        for (int i = 1; i <= maxLength; i++)
        {
            total += (long)Math.Pow(alphabet, i);
        }

        return total;
    }


    /// <inheritdoc />
    public async Task CheckTimedOutSubtasks(TimeSpan timeout)
    {
        _logger.LogDebug("Checking for timed out subtasks (timeout: {Timeout})", timeout);

        var timedOutRequests = await _mongoRepo.GetRequestsWithTimedOutSubtasks(timeout);

        foreach (var request in timedOutRequests)
        {
            var now = DateTime.UtcNow;
            var timeoutThreshold = now - timeout;

            var timedOutSubtasks = request.SubTasks
                .Where(st => st.Status == SubTaskStatus.IN_PROGRESS
                            && st.LastHeartbeat.HasValue
                            && st.LastHeartbeat.Value < timeoutThreshold)
                .ToList();

            foreach (var subTask in timedOutSubtasks)
            {
                _logger.LogWarning(
                    "SubTask {SubTaskId} for request {RequestId} timed out. " +
                    "Last heartbeat: {LastHeartbeat}, CurrentIndex: {CurrentIndex}",
                    subTask.SubTaskId, request.RequestId,
                    subTask.LastHeartbeat, subTask.CurrentIndex);


                if (subTask.RetryCount >= 3)
                {
                    _logger.LogError(
                        "SubTask {SubTaskId} for request {RequestId} has been retried {RetryCount} times. Marking task as ERROR",
                        subTask.SubTaskId, request.RequestId, subTask.RetryCount);
                    await MarkTaskAsError(request.RequestId, "Max retries exceeded for subtask");
                    continue;
                }

                _rabbitPublisher.PublishTask(new RabbitTaskMessage
                {
                    RequestId = request.RequestId,
                    SubTaskId = subTask.SubTaskId,
                    Hash = request.Hash,
                    MaxLength = request.MaxLength,
                    StartIndex = subTask.CurrentIndex,
                    EndIndex = subTask.EndIndex,
                    CreatedAt = DateTime.UtcNow
                });
            }


            var subtasksToReset = timedOutSubtasks
                .Where(st => st.RetryCount < 3)
                .Select(st => st.SubTaskId)
                .ToList();

            if (subtasksToReset.Any())
            {
                await _mongoRepo.ResetTimedOutSubtasks(request.RequestId, subtasksToReset);
            }
        }

        if (timedOutRequests.Any())
        {
            _logger.LogInformation("Processed {Count} requests with timed out subtasks", timedOutRequests.Count);
        }


    }


    /// <inheritdoc />
    public async Task RestorePendingTasks()
    {
        _logger.LogInformation("Restoring pending tasks after restart...");

        var pendingRequests = await _mongoRepo.GetPendingRequests();

        foreach (var request in pendingRequests)
        {
            _logger.LogInformation("Restoring request {RequestId} with status {Status}",
                request.RequestId, request.Status);

            if (request.CreatedAt < DateTime.UtcNow.AddHours(-1))
            {
                await MarkTaskAsError(request.RequestId, "Task timed out in PENDING state");
                continue;
            }

            var unfinishedSubtasks = request.SubTasks
                .Where(st => st.Status != SubTaskStatus.COMPLETED)
                .ToList();

            foreach (var subTask in unfinishedSubtasks)
            {
                _logger.LogInformation(
                    "Restoring subtask {SubTaskId} for request {RequestId} from index {CurrentIndex} to {EndIndex}",
                    subTask.SubTaskId, request.RequestId, subTask.CurrentIndex, subTask.EndIndex);


                _rabbitPublisher.PublishTask(new RabbitTaskMessage
                {
                    RequestId = request.RequestId,
                    SubTaskId = subTask.SubTaskId,
                    Hash = request.Hash,
                    MaxLength = request.MaxLength,
                    StartIndex = subTask.CurrentIndex,
                    EndIndex = subTask.EndIndex,
                    CreatedAt = DateTime.UtcNow
                });

                subTask.Status = SubTaskStatus.PENDING;
                subTask.RetryCount++;
                subTask.WorkerId = null;
            }

            if (unfinishedSubtasks.Any())
            {
                await _mongoRepo.ResetTimedOutSubtasks(request.RequestId,
                    unfinishedSubtasks.Select(x => x.SubTaskId).ToList());
            }
        }

        _logger.LogInformation("Restored {Count} pending tasks", pendingRequests.Count);
    }


    /// <inheritdoc />
    public async Task RetryPendingPublishes()
    {
        var requests = await _mongoRepo.GetRequestsWithUnpublishedSubtasks();
        foreach (var req in requests)
        {
            var unpublished = req.SubTasks.Where(st => st.Status == SubTaskStatus.PENDING && st.WorkerId == null).ToList();
            foreach (var st in unpublished)
            {
                if (st.LastHeartbeat.HasValue &&
                (DateTime.UtcNow - st.LastHeartbeat.Value) < TimeSpan.FromSeconds(30))
                {
                    continue;
                }
                try
                {
                    _rabbitPublisher.PublishTask(new RabbitTaskMessage
                    {
                        RequestId = req.RequestId,
                        SubTaskId = st.SubTaskId,
                        Hash = req.Hash,
                        MaxLength = req.MaxLength,
                        StartIndex = st.CurrentIndex,
                        EndIndex = st.EndIndex,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Retry publish failed for subtask {SubTaskId}", st.SubTaskId);
                }
            }
        }
    }


    /// <inheritdoc />
    public async Task MarkTaskAsError(Guid requestId, string errorMessage)
    {
        await _mongoRepo.MarkTaskStatus(requestId, CrackStatus.ERROR, errorMessage);
    }

}
