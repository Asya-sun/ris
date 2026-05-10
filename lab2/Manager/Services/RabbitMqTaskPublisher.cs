using RabbitMQ.Client;
using Shared.DTO;
using System.Text;
using System.Text.Json;

public class RabbitMqTaskPublisher : IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly ILogger<RabbitMqTaskPublisher> _logger;

    public RabbitMqTaskPublisher(ILogger<RabbitMqTaskPublisher> logger)
    {
        _logger = logger;

        var factory = new ConnectionFactory
        {
            HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "rabbitmq",
            UserName = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "admin",
            Password = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "admin",
            Port = 5672,
            VirtualHost = "/"
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        var args = new Dictionary<string, object>
        {
            { "x-dead-letter-exchange", "" },
            { "x-dead-letter-routing-key", "crack_tasks_dlq" },
            { "x-max-retries", 3 }
        };

        _channel.QueueDeclare("crack_tasks", durable: true, exclusive: false, autoDelete: false, arguments: args);
        _channel.QueueDeclare("crack_tasks_dlq", durable: true, exclusive: false, autoDelete: false);

        _logger.LogInformation("RabbitMQ Publisher initialized");
    }

    public void PublishTask(RabbitTaskMessage task)
    {
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(task));

        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.DeliveryMode = 2;
        properties.Headers = new Dictionary<string, object>
        {
            { "x-retry-count", 0 }
        };

        _channel.BasicPublish(exchange: "", routingKey: "crack_tasks", basicProperties: properties, body: body);

        _logger.LogInformation("Published subtask {SubTaskId} for request {RequestId} range [{Start}-{End}]",
            task.SubTaskId, task.RequestId, task.StartIndex, task.EndIndex);
    }

    public List<DeadLetterMessage> GetDeadLetterMessages(int count = 10)
    {
        var messages = new List<DeadLetterMessage>();

        for (int i = 0; i < count; i++)
        {
            var result = _channel.BasicGet("crack_tasks_dlq", autoAck: false);
            if (result == null)
                break;

            var body = System.Text.Encoding.UTF8.GetString(result.Body.ToArray());
            var message = JsonSerializer.Deserialize<Dictionary<string, object>>(body);

            messages.Add(new DeadLetterMessage
            {
                DeliveryTag = result.DeliveryTag,
                Content = body,
                Reason = message?.ContainsKey("x-death") == true ? "See x-death header" : "Unknown",
                ReceivedAt = DateTime.UtcNow
            });

            _channel.BasicNack(result.DeliveryTag, false, true);
        }

        return messages;
    }


    public void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
    }
}
