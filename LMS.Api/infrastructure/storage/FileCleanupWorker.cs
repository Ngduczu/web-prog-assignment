using LMS.Api.application.abstractions.storage;
using LMS.Api.infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LMS.Api.infrastructure.storage;

public sealed class FileCleanupWorker : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(10);
    private const int BatchSize = 100;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FileCleanupWorker> _logger;

    public FileCleanupWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<FileCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ProcessPendingAsync(stoppingToken);
        using var timer = new PeriodicTimer(PollingInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ProcessPendingAsync(stoppingToken);
        }
    }

    private async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            var now = DateTime.UtcNow;
            var tasks = await dbContext.FileCleanupTasks
                .Where(task => task.NextAttemptAt <= now)
                .OrderBy(task => task.NextAttemptAt)
                .ThenBy(task => task.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            foreach (var task in tasks)
            {
                try
                {
                    await storage.DeleteIfExistsAsync(task.ObjectKey, cancellationToken);
                    dbContext.FileCleanupTasks.Remove(task);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    var delayMinutes = Math.Min(60, 1 << Math.Min(task.AttemptCount, 6));
                    task.RecordFailure(exception.Message, now.AddMinutes(delayMinutes));
                    _logger.LogWarning(
                        exception,
                        "File cleanup attempt {Attempt} failed for {ObjectKey}; it remains queued.",
                        task.AttemptCount,
                        task.ObjectKey);
                }
            }

            if (tasks.Count > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to process the persistent file cleanup queue.");
        }
    }
}
