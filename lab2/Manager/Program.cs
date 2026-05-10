using Manager.Models;
using Manager.Services;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;


var builder = WebApplication.CreateBuilder(args);

var config = new ManagerConfig
{
    WorkerNumber = int.Parse(Environment.GetEnvironmentVariable("WORKER_NUMBER") ?? "3"),
    Alphabet = Environment.GetEnvironmentVariable("ALPHABET") ?? "abcdefghijklmnopqrstuvwxyz0123456789",
    CheckInterval = TimeSpan.FromSeconds(
        int.Parse(Environment.GetEnvironmentVariable("CHECK_INTERVAL_SEC") ?? "60")),
    TaskTimeout = TimeSpan.FromSeconds(
        int.Parse(Environment.GetEnvironmentVariable("TASK_TIMEOUT_SEC") ?? "2"))
};

// Register Configuration
builder.Services.AddSingleton(Options.Create(config));

// Controllers
builder.Services.AddControllers();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


BsonSerializer.RegisterSerializer(
    new GuidSerializer(GuidRepresentation.Standard)
);

// Services
builder.Services.AddSingleton<IManagerService, ManagerService>();
builder.Services.AddHostedService<TaskTimeoutService>();
builder.Services.AddSingleton<MongoRepository>();
builder.Services.AddSingleton<RabbitMqTaskPublisher>();
builder.Services.AddHostedService<RabbitMqResultConsumer>();


var app = builder.Build();

app.Lifetime.ApplicationStarted.Register(() =>
{
    Task.Run(async () =>
    {
        using var scope = app.Services.CreateScope();
        var managerService = scope.ServiceProvider.GetRequiredService<IManagerService>();
        await managerService.RestorePendingTasks();
    });
});


// Swagger
app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.Run();
