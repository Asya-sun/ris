using Worker.Models;
using Worker.Services;
using Polly;
using Polly.Extensions.Http;


var builder = WebApplication.CreateBuilder(args);

var config = new WorkerConfig
{
    WorkerName = Environment.GetEnvironmentVariable("WORKER_NAME") ?? "MyPrettyName",
    Alphabet = Environment.GetEnvironmentVariable("ALPHABET") ?? "abcdefghijklmnopqrstuvwxyz0123456789",
    WorkerId = Guid.NewGuid(),
    StopWord = Environment.GetEnvironmentVariable("STOP_WORD") ?? "bom",
};

// Register Configuration
builder.Services.AddSingleton(config);


// Services
builder.Services.AddSingleton<IHashCrackService, HashCrackService>();
builder.Services.AddSingleton<RabbitMqResultPublisher>();
builder.Services.AddHostedService<RabbitMqTaskConsumer>();

var app = builder.Build();


app.Lifetime.ApplicationStarted.Register(() =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Worker {WorkerName} started, waiting for tasks from RabbitMQ...", config.WorkerName);
});

app.Run();
