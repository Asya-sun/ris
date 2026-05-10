using Shared.DTO;

namespace Worker.Services;

public interface IHashCrackService
{
    Task ProcessTask(RabbitTaskMessage task, RabbitMqResultPublisher publisher, Guid workerId, CancellationToken cancellationToken);

}