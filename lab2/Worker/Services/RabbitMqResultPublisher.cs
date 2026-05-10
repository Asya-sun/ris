using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using Shared.DTO;

namespace Worker.Services;

public class RabbitMqResultPublisher : IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly ILogger<RabbitMqResultPublisher> _logger;

    public RabbitMqResultPublisher(ILogger<RabbitMqResultPublisher> logger)
    {
        _logger = logger;

        var factory = new ConnectionFactory
        {
            HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "rabbitmq",
            UserName = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "admin",
            Password = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "admin"
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.QueueDeclare("crack_results", durable: true, exclusive: false, autoDelete: false);


        _channel.ConfirmSelect();

        _logger.LogInformation("RabbitMQ Result Publisher initialized");
    }

    public void PublishProgress(Guid requestId, Guid subTaskId, Guid workerId,
        double currentIndex, List<string> foundWords, bool isCompleted)
    {
        _logger.LogInformation("Publishing progress: request={RequestId}, found={FoundCount} words",
        requestId, foundWords.Count);

        var progress = new RabbitProgressMessage
        {
            RequestId = requestId,
            SubTaskId = subTaskId,
            WorkerId = workerId,
            CurrentIndex = currentIndex,
            FoundWords = foundWords,
            IsCompleted = isCompleted,
            Timestamp = DateTime.UtcNow
        };

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(progress));
        _logger.LogInformation("Serialized progress, body length: {Length}", body.Length);

        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.DeliveryMode = 2;

        _channel.BasicPublish(exchange: "", routingKey: "crack_results", basicProperties: properties, body: body);
        _logger.LogInformation("Published to RabbitMQ");
        _logger.LogDebug("Published progress for task {RequestId}, subtask {SubTaskId}, currentIndex: {CurrentIndex}, completed: {IsCompleted}",
            requestId, subTaskId, currentIndex, isCompleted);

        bool confirmed = _channel.WaitForConfirms(TimeSpan.FromSeconds(5));

        if (!confirmed)
        {
            _logger.LogError("Failed to confirm message delivery for task {RequestId}", requestId);
            throw new Exception("RabbitMQ did not confirm message");
        }

        _logger.LogInformation("Published and confirmed to RabbitMQ");
    }

    public void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
    }
}