using System.Threading.Channels;
using LMS.Api.application.abstractions.storage;

namespace LMS.Api.infrastructure.storage;

public sealed class FileCleanupWorker : BackgroundService, IFileCleanupQueue
{
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>();
    private readonly IFileStorage _storage;
    private readonly ILogger<FileCleanupWorker> _logger;

    public FileCleanupWorker(IFileStorage storage, ILogger<FileCleanupWorker> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public void Enqueue(string objectKey)
    {
        if (!string.IsNullOrWhiteSpace(objectKey))
        {
            _queue.Writer.TryWrite(objectKey);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var objectKey in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    await _storage.DeleteIfExistsAsync(objectKey, stoppingToken);
                    break;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception) when (attempt < 5)
                {
                    _logger.LogWarning(exception, "File cleanup attempt {Attempt} failed for {ObjectKey}.", attempt, objectKey);
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), stoppingToken);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "File cleanup exhausted retries for {ObjectKey}.", objectKey);
                }
            }
        }
    }
}
