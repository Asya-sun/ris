using Shared.DTO;

namespace Worker.Services;

public interface IHashCrackService
{
    Task<WorkerTaskResponse> Crack(WorkerTaskRequest request);
}