using Manager.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using Shared.DTO;

public class RabbitMqResultConsumer : BackgroundService
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RabbitMqResultConsumer> _logger;

    public RabbitMqResultConsumer(IServiceProvider serviceProvider, ILogger<RabbitMqResultConsumer> logger)
    {
        _serviceProvider = serviceProvider;
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
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += async (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);

            var progress = JsonSerializer.Deserialize<RabbitProgressMessage>(message);
            if (progress == null)
            {
                _logger.LogWarning("Failed to deserialize progress message");
                _channel.BasicAck(ea.DeliveryTag, false);
                return;
            }

            _logger.LogInformation("Received progress from worker for task {RequestId}", progress.RequestId);

            using var scope = _serviceProvider.CreateScope();
            var managerService = scope.ServiceProvider.GetRequiredService<IManagerService>();

            await managerService.ProcessProgress(progress);

            _channel.BasicAck(ea.DeliveryTag, false);
        };

        _channel.BasicConsume("crack_results", autoAck: false, consumer: consumer);
        return Task.CompletedTask;
    }
}