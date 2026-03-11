using Manager.Models;
using Manager.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

var config = new ManagerConfig
{
    WorkerNumber = int.Parse(Environment.GetEnvironmentVariable("WORKER_NUMBER") ?? "3"),
    Alphabet = Environment.GetEnvironmentVariable("ALPHABET") ?? "abcdefghijklmnopqrstuvwxyz0123456789",
};

// Register Configuration
builder.Services.AddSingleton(Options.Create(config)); 

// Controllers
builder.Services.AddControllers();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


// // Manager service
builder.Services.AddSingleton<IManagerService, ManagerService>();


builder.Services.AddHttpClient();

var app = builder.Build();

// Swagger
app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.Run();