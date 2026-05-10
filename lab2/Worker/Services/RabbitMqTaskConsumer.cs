using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using Shared.DTO;
using Worker.Models;
using Worker.Exceptions;

namespace Worker.Services;

public class RabbitMqTaskConsumer : BackgroundService
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RabbitMqTaskConsumer> _logger;
    private readonly WorkerConfig _config;

    public RabbitMqTaskConsumer(
        IServiceProvider serviceProvider,
        ILogger<RabbitMqTaskConsumer> logger,
        WorkerConfig config)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _config = config;

        var factory = new ConnectionFactory
        {
            HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "rabbitmq",
            UserName = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "admin",
            Password = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "admin"
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

        _channel.BasicQos(0, 1, false);

        _logger.LogInformation("RabbitMQ Task Consumer initialized, waiting for tasks...");
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += async (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);

            _logger.LogInformation("Received task: {Message}", message);

            var task = JsonSerializer.Deserialize<RabbitTaskMessage>(message);
            if (task == null)
            {
                _logger.LogWarning("Failed to deserialize task");
                _channel.BasicAck(ea.DeliveryTag, false);
                return;
            }



            try
            {
                using var scope = _serviceProvider.CreateScope();
                var crackService = scope.ServiceProvider.GetRequiredService<IHashCrackService>();
                var resultPublisher = scope.ServiceProvider.GetRequiredService<RabbitMqResultPublisher>();

                await crackService.ProcessTask(task, resultPublisher, _config.WorkerId, stoppingToken);

                _channel.BasicAck(ea.DeliveryTag, false);
                _logger.LogInformation("Task {RequestId} completed successfully", task.RequestId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process task {RequestId}", task.RequestId);


                if (ex is BomDetectedException)
                {
                    _logger.LogError("BOM detected! Moving task {RequestId} directly to DLQ without retry", task.RequestId);
                    _channel.BasicNack(ea.DeliveryTag, false, false);
                    return;
                }

                var retryCount = GetRetryCount(ea.BasicProperties);

                if (retryCount >= 3)
                {
                    _logger.LogError("Task {RequestId} failed 3 times, moving to DLQ", task.RequestId);
                    _channel.BasicNack(ea.DeliveryTag, false, false);
                }
                else
                {
                    _logger.LogWarning("Task {RequestId} failed, retry {RetryCount}/3", task.RequestId, retryCount + 1);
                    var properties = _channel.CreateBasicProperties();
                    properties.Headers = new Dictionary<string, object>
                    {
                        { "x-retry-count", retryCount + 1 }
                    };
                    properties.Persistent = true;

                    _channel.BasicPublish("", "crack_tasks", properties, body);
                    _channel.BasicAck(ea.DeliveryTag, false);
                }
            }
        };

        _channel.BasicConsume("crack_tasks", autoAck: false, consumer: consumer);
        return Task.CompletedTask;
    }

    private int GetRetryCount(IBasicProperties properties)
    {
        if (properties?.Headers != null && properties.Headers.ContainsKey("x-retry-count"))
        {
            return Convert.ToInt32(properties.Headers["x-retry-count"]);
        }
        return 0;
    }

    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
}