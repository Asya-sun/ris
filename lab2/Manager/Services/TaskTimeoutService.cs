using Manager.Models;
using Manager.Services;
using Microsoft.Extensions.Options;

public class TaskTimeoutService : BackgroundService
{
    private readonly ILogger<TaskTimeoutService> _logger;
    private readonly IManagerService _managerService;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _subtaskTimeout = TimeSpan.FromSeconds(60);

    private readonly ManagerConfig _config;

    public TaskTimeoutService(
        ILogger<TaskTimeoutService> logger,
        IManagerService managerService,
        IOptions<ManagerConfig> config)
    {
        _config = config.Value;
        _logger = logger;
        _managerService = managerService;

        _checkInterval = _config.CheckInterval;
        _subtaskTimeout = _config.TaskTimeout;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _managerService.RetryPendingPublishes();
            await _managerService.CheckTimedOutSubtasks(_subtaskTimeout);
            await Task.Delay(_checkInterval, stoppingToken);
        }
    }
}