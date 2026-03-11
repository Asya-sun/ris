using Manager.DTO;
using Shared.DTO;

namespace Manager.Services;

public interface IManagerService
{
    Task<Guid> CreateCrackTask(ManagerCrackRequest request);

    ManagerStatusResponse GetStatus(Guid requestId);

    void ProcessWorkerResult(WorkerTaskResponse response);

    Task<Guid> RegisterWorker(WorkerRegisterRequest request);
    
}