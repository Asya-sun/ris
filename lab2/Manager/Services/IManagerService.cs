using Manager.DTO;
using Manager.Models;
using Shared.DTO;

namespace Manager.Services;

public interface IManagerService
{
    Task<Guid> CreateCrackTask(ManagerCrackRequest request);
    Task<ManagerStatusResponse> GetStatus(Guid requestId);
    Task ProcessProgress(RabbitProgressMessage progress);
    Task CheckTimedOutSubtasks(TimeSpan timeout);
    Task RestorePendingTasks();
    Task RetryPendingPublishes();
    Task MarkTaskAsError(Guid requestId, string errorMessage);
}