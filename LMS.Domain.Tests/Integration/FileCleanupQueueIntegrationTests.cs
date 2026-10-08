using LMS.Api.domain.Entities.Storage;
using LMS.Api.infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LMS.Domain.Tests.Integration;

public sealed class FileCleanupQueueIntegrationTests
{
    [Fact]
    public async Task ConcurrentDuplicateEnqueueCreatesOneTaskWithoutFailingEitherSave()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "CONNECTIONSTRINGS__DEFAULTCONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var objectKey = $"integration/cleanup/{Guid.NewGuid():N}";

        try
        {
            await using var first = new AppDbContext(options);
            await using var second = new AppDbContext(options);
            first.FileCleanupTasks.Add(new FileCleanupTask(objectKey, DateTime.UtcNow));
            second.FileCleanupTasks.Add(new FileCleanupTask(objectKey, DateTime.UtcNow));

            await Task.WhenAll(
                first.SaveChangesAsync(),
                second.SaveChangesAsync());

            await using var verification = new AppDbContext(options);
            Assert.Equal(
                1,
                await verification.FileCleanupTasks.CountAsync(task => task.ObjectKey == objectKey));
        }
        finally
        {
            await using var cleanup = new AppDbContext(options);
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM file_cleanup_tasks WHERE object_key = {objectKey}");
        }
    }
}
