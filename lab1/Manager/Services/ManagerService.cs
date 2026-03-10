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

    public ManagerService(
        IOptions<ManagerConfig> config,
        HttpClient httpClient,
        ILogger<ManagerService> logger)
    {
        _config = config.Value;
        _httpClient = httpClient;
        _logger = logger;
    }
    /*
    че тут вообще должно быть
    1 воркеры
    2 реакции на http- запросы

    */

    public async Task<Guid> CreateCrackTask(ManagerCrackRequest request)
    {
        var id = Guid.NewGuid();

        var total = CalculateTotalCombinations(request.MaxLength);

        var task = new CrackTaskState
        {
            RequestId = id,
            Hash = request.Hash,
            TotalCombinations = total,
            CheckedCombinations = 0
        };

        _taskStates[id] = task;

        _taskStates[id] = task;

        await DispatchTasks(id, request.Hash, request.MaxLength, total);

        return id;
    }

    public ManagerStatusResponse GetStatus(Guid requestId)
    {
        if (!_taskStates.TryGetValue(requestId, out var task))
            throw new Exception("Task not found");

        int progress = (int)(100.0 * task.CheckedCombinations / task.TotalCombinations);

        return new ManagerStatusResponse(
            task.Status.ToString(),
            progress,
            task.Status == CrackStatus.READY ? task.FoundWords : null
        );
    }

    public void ProcessWorkerResult(WorkerTaskResponse response)
    {
        if (!_taskStates.TryGetValue(response.TaskRequestId, out var task))
            return;

        lock (task)
        {
            task.CheckedCombinations += response.CheckedCount;

            task.FoundWords.AddRange(response.FoundWords);

            if (task.CheckedCombinations >= task.TotalCombinations)
            {
                task.Status = CrackStatus.READY;
            }
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

    private async Task DispatchTasks(Guid requestId, string hash, int maxLength, long total)
    {
        int workers = _config.WorkerNumber;

        long chunkSize = total / workers;

        for (int i = 0; i < workers; i++)
        {
            long start = i * chunkSize;
            long end = (i == workers - 1)
                ? total - 1
                : (i + 1) * chunkSize - 1;

            var task = new WorkerTaskRequest(
                requestId,
                hash,
                maxLength,
                start,
                end
            );

            try
            {
                await _httpClient.PostAsJsonAsync(
                    $"{_config.WorkerUrl}/internal/api/worker/hash/crack/task",
                    task
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispatch task to worker");
            }
        }
    }
}
