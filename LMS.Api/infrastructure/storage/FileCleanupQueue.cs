using LMS.Api.application.abstractions.storage;
using LMS.Api.domain.Entities.Storage;
using LMS.Api.infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LMS.Api.infrastructure.storage;

public sealed class FileCleanupQueue : IFileCleanupQueue
{
    private readonly AppDbContext _dbContext;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public FileCleanupQueue(
        AppDbContext dbContext,
        IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContext = dbContext;
        _dbContextFactory = dbContextFactory;
    }

    public void Enqueue(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey) ||
            _dbContext.FileCleanupTasks.Local.Any(task => task.ObjectKey == objectKey))
        {
            return;
        }

        _dbContext.FileCleanupTasks.Add(new FileCleanupTask(objectKey, DateTime.UtcNow));
    }

    public async Task EnqueueImmediatelyAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return;
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.FileCleanupTasks.Add(new FileCleanupTask(objectKey, DateTime.UtcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
