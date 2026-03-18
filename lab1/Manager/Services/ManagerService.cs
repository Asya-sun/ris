using System.Collections.Concurrent;
using Manager.DTO;
using Manager.Models;
using Microsoft.Extensions.Options;
using Shared.DTO;

namespace Manager.Services;

public class ManagerService : IManagerService
{
    private ConcurrentDictionary<Guid, CrackTaskState> _taskStates = new();
    private readonly HttpClient _httpClient;
    private readonly ILogger<ManagerService> _logger;
    private readonly ManagerConfig _config;
    
    private readonly ConcurrentDictionary<Guid, WorkerInfo> _workers = new();

    public ManagerService(
        IOptions<ManagerConfig> config,
        // HttpClient httpClient,
        IHttpClientFactory httpClientFactory,
        ILogger<ManagerService> logger)
    {
        _config = config.Value;
        // _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _httpClient = httpClientFactory.CreateClient();
        _logger = logger;
    }

    public async Task<Guid> RegisterWorker(WorkerRegisterRequest request)
    {
        var id = Guid.NewGuid();
        var worker = new WorkerInfo
        {
            WorkerId = id,
            WorkerName = request.WorkerName,
            Url = request.Url
        };

        _workers[id] = worker;

        _logger.LogInformation(
            "Worker registered {WorkerId} with {WorkerName} at {Url}",
            worker.WorkerId,
            worker.WorkerName,
            worker.Url
        );

        await TryDispatchPendingTasks();

        return id;
    }

    public async Task<Guid> CreateCrackTask(ManagerCrackRequest request)
    {
        var id = Guid.NewGuid();

        _logger.LogInformation("Creating crack task {RequestId}", id);

        var total = CalculateTotalCombinations(request.MaxLength);

        _logger.LogInformation("Task {RequestId}: total combinations = {Total}", id, total);

        var task = new CrackTaskState
        {
            RequestId = id,
            Hash = request.Hash,
            TotalCombinations = total,
            CheckedCombinations = 0,
            Status = CrackStatus.PENDING,
            MaxLength = request.MaxLength,
            FoundWords = new List<string>(),
            StartedAt = DateTime.UtcNow,
        };

        _taskStates[id] = task;
        _logger.LogInformation("Task {RequestId} saved to dictionary, workers count = {WorkerCount}", id, _workers.Count);


        if (_workers.Count > 0)
        {
            _logger.LogInformation("Dispatching task {RequestId} to {WorkerCount} workers", id, _workers.Count);
            await DispatchTasks(id, request.Hash, request.MaxLength, total);
            task.Status = CrackStatus.IN_PROGRESS;
            _logger.LogInformation("Task {RequestId} dispatched and set to IN_PROGRESS", id);
        }
            else
        {
            _logger.LogWarning("No workers available for task {RequestId}, status PENDING", id);
        }

        return id;
    }

    public ManagerStatusResponse GetStatus(Guid requestId)
    {
        if (requestId == Guid.Empty)
        {
            _logger.LogError("Empty requestId provided");
            throw new ArgumentException("RequestId cannot be empty", nameof(requestId));
        }

        if (!_taskStates.TryGetValue(requestId, out var task))
        {
            _logger.LogError("Task {RequestId} not found", requestId);
            throw new KeyNotFoundException($"Task {requestId} not found");
        }

        _logger.LogInformation("Task {RequestId} status: {Status}, checked: {Checked}/{Total}", 
            requestId, task.Status, task.CheckedCombinations, task.TotalCombinations);
        
        int progress = (int)(100.0 * task.CheckedCombinations / task.TotalCombinations);

        // TODO 
        // put to method?..
        var status = task.Status switch
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
            task.Status == CrackStatus.READY ? task.FoundWords : null
        );
    }

    public void ProcessWorkerResult(WorkerTaskResponse response)
    {

        _logger.LogInformation(
            "PROCESS RESULT: task={TaskId}, checkedCount={CheckedCount}, foundWords={FoundWords}",
            response.TaskRequestId,
            response.CheckedCount,
            string.Join(",", response.FoundWords)
        );
        if (!_taskStates.TryGetValue(response.TaskRequestId, out var task))
            return;

        lock (task)
        {

            _logger.LogInformation(
                "BEFORE: task={TaskId}, totalCombinations={Total}, currentChecked={CurrentChecked}",
                response.TaskRequestId,
                task.TotalCombinations,
                task.CheckedCombinations
            );

            task.CheckedCombinations += response.CheckedCount;

            _logger.LogInformation(
                "AFTER ADD: task={TaskId}, newChecked={NewChecked}",
                response.TaskRequestId,
                task.CheckedCombinations
            );

            foreach (var word in response.FoundWords)
            {
                if (!task.FoundWords.Contains(word))
                {
                    task.FoundWords.Add(word);
                    _logger.LogInformation("ADDED WORD '{Word}' to task {TaskId}", word, response.TaskRequestId);
                }
            }
    
            // task.FoundWords.AddRange(response.FoundWords);

            if (task.CheckedCombinations >= task.TotalCombinations)
            {
                task.Status = CrackStatus.READY;
                task.CompletedAt = DateTime.UtcNow;
                _logger.LogInformation(
                    "TASK {TaskId} COMPLETED! Total words found: {FoundCount}",
                    response.TaskRequestId,
                    task.FoundWords.Count
                );
            }
        }
    }

    private double CalculateTotalCombinations(int maxLength)
    {
        long alphabet = _config.Alphabet.Length;
        double total = 0;

        for (int i = 1; i <= maxLength; i++)
        {
            total += (double) Math.Pow(alphabet, i);
        }

        return total;
    }

    private async Task DispatchTasks(Guid requestId, string hash, int maxLength, double total)
    {
        var workers = _workers.Values.ToList();
         _logger.LogInformation("DispatchTasks for {RequestId}: found {WorkerCount} workers", requestId, workers.Count);

        if (workers.Count == 0)
        {
            _logger.LogWarning("No workers registered, dispatch postponed");
            return;
        }

        var aliveWorkers = workers.Where(w => w.IsAlive).ToList();

        if (aliveWorkers.Count == 0)
        {
            _logger.LogWarning("No alive workers for task {RequestId}", requestId);
            return;
        }
        
        double chunkSize = total / aliveWorkers.Count;

        for (int i = 0; i < aliveWorkers.Count; i++)
        {
            var worker = aliveWorkers[i];
            double start = i * chunkSize;
            double end = (i == aliveWorkers.Count - 1)
                ? total - 1
                : (i + 1) * chunkSize - 1;

            _logger.LogInformation(
                "Dispatching to worker {WorkerName} ({WorkerUrl}): range [{Start}-{End}] for task {RequestId}",
                worker.WorkerName,
                worker.Url,
                start,
                end,
                requestId
            );

            var task = new WorkerTaskRequest(
                requestId,
                hash,
                maxLength,
                start,
                end
            );

            try
            {
                var response = await _httpClient.PostAsJsonAsync(
                    $"{worker.Url}/internal/api/worker/hash/crack/task",
                    task
                );

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Successfully dispatched to worker {WorkerName}", worker.WorkerName);
                }
                else
                {
                    _logger.LogWarning(
                        "Worker {WorkerName} returned {StatusCode} for task {RequestId}",
                        worker.WorkerName,
                        response.StatusCode,
                        requestId
                    );
                }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to dispatch task to worker");
                }
        }
    }


    private async Task TryDispatchPendingTasks()
    {
        foreach (var task in _taskStates.Values)
        {
            if (task.Status != CrackStatus.PENDING)
                continue;

            _logger.LogInformation(
                "Dispatching pending task {TaskId}",
                task.RequestId
            );

            try
            {
                await DispatchTasks(
                    task.RequestId,
                    task.Hash,
                    task.MaxLength,
                    task.TotalCombinations
                );

                task.Status = CrackStatus.IN_PROGRESS;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispatch pending task");
            }
        }
    }



    public List<WorkerInfo> GetAllWorkers()
    {
        return _workers.Values.ToList();
    }

    public void UpdateWorkerHealth(Guid workerId, bool isAlive)
    {
        if (_workers.TryGetValue(workerId, out var worker))
        {
            worker.IsAlive = isAlive;
            worker.LastSeen = DateTime.UtcNow;
            
            _logger.LogDebug("Worker {WorkerName} health updated: {IsAlive}", 
                worker.WorkerName, isAlive);
        }
    }


    public void CheckTaskTimeouts(TimeSpan timeout)
    {
        var now = DateTime.UtcNow;
        var timedOutTasks = _taskStates.Values
            .Where(t => t.Status == CrackStatus.IN_PROGRESS 
                        && t.StartedAt.HasValue 
                        && now - t.StartedAt.Value > timeout)
            .ToList();

        foreach (var task in timedOutTasks)
        {
            lock (task)
            {
                task.Status = CrackStatus.ERROR;
                _logger.LogWarning("Task {TaskId} timed out", task.RequestId);
            }
        }
    }

    public async Task CancelTask(Guid taskId)
    {   
        _logger.LogInformation("CancelTask called for {TaskId}", taskId);
        
        if (!_taskStates.TryGetValue(taskId, out var task))
        {
            _logger.LogWarning("Task {TaskId} not found for cancellation", taskId);
            return;
        }
    
        lock (task)
        {
            if (task.Status == CrackStatus.IN_PROGRESS || task.Status == CrackStatus.PENDING)
            {
                task.Status = CrackStatus.ERROR;
            }
            else
            {
                _logger.LogInformation("Task {TaskId} already in state {Status}, skipping cancel", 
                    taskId, task.Status);
                return;
            }
        }
    
        // Отправляем сигнал отмены всем воркерам
        var workers = _workers.Values.Where(w => w.IsAlive).ToList();
        var cancelTasks = new List<Task>();
    
        foreach (var worker in workers)
        {
            cancelTasks.Add(SendCancelToWorker(worker, taskId));
        }
    
        await Task.WhenAll(cancelTasks);
        
        _logger.LogInformation("Task {TaskId} cancelled", taskId);
    }

    private async Task SendCancelToWorker(WorkerInfo worker, Guid taskId)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{worker.Url}/internal/api/worker/hash/crack/cancel",
                new { TaskId = taskId }
            );
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Cancel signal sent to worker {WorkerName} for task {TaskId}", 
                    worker.WorkerName, taskId);
            }
            else
            {
                _logger.LogWarning("Worker {WorkerName} returned {StatusCode} for cancel task {TaskId}", 
                    worker.WorkerName, response.StatusCode, taskId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send cancel to worker {WorkerName} for task {TaskId}", 
                worker.WorkerName, taskId);
        }
    }

    public List<CrackTaskState> GetTimedOutTasks(TimeSpan timeout)
    {
        var now = DateTime.UtcNow;
        return _taskStates.Values
            .Where(t => t.Status == CrackStatus.IN_PROGRESS 
                        && t.StartedAt.HasValue 
                        && now - t.StartedAt.Value > timeout)
            .ToList();
    }
}
