using Shared.DTO;

namespace Worker.Services;

public interface IHashCrackService
{
    // Task<WorkerTaskResponse> Crack(WorkerTaskRequest request);
    void StartTask(WorkerTaskRequest request);
    // Task SendProgress(); // ?
}