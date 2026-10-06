using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.time;
using LMS.Api.application.features.examinationattempts;
using LMS.Api.domain.Enums.Examinations;
using LMS.Api.domain.repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LMS.Api.infrastructure.workers;

public sealed class ExaminationAutoSubmitWorker : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(30);
    private const int MaxBatchSize = 100;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExaminationAutoSubmitWorker> _logger;

    public ExaminationAutoSubmitWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ExaminationAutoSubmitWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollingInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessExpiredAttemptsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to auto-submit expired examination attempts.");
            }
        }
    }

    private async Task ProcessExpiredAttemptsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var attempts = scope.ServiceProvider.GetRequiredService<IExaminationAttemptRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var expired = await attempts.ListExpiredAsync(clock.UtcNow, MaxBatchSize, cancellationToken);
        if (expired.Count == 0)
        {
            return;
        }

        foreach (var attempt in expired)
        {
            var reason = AttemptDeadline.GetExpiredReason(
                attempt,
                attempt.Examination!,
                clock.UtcNow);
            AttemptGrader.CompleteAndGrade(attempt, attempt.Examination!, reason, autoSubmit: true);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
