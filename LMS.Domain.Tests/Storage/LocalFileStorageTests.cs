using LMS.Api.application.abstractions.storage;
using LMS.Api.domain.exceptions;
using LMS.Api.infrastructure.storage.Local;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LMS.Domain.Tests.Storage;

public class LocalFileStorageTests
{
    [Fact]
    public void CourseUploadHasNoApplicationSizeLimitButChatUploadDoes()
    {
        var upload = new FileUpload(
            "large-file.bin",
            "application/octet-stream",
            FileUploadPolicy.MaximumChatFileSizeBytes,
            Stream.Null);

        FileUploadPolicy.Validate(upload);

        Assert.Throws<BadRequestException>(() =>
            FileUploadPolicy.Validate(upload, FileUploadPolicy.MaximumChatFileSizeBytes));
    }

    [Fact]
    public void CourseUploadRejectsConfiguredMaximumSize()
    {
        var upload = new FileUpload("large-file.bin", "application/octet-stream", 5, Stream.Null);

        Assert.Throws<BadRequestException>(() =>
            FileUploadPolicy.Validate(upload, 5));
    }

    [Fact]
    public async Task DeleteRejectsPathTraversal()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"lms-storage-{Guid.NewGuid():N}");

        try
        {
            var storage = CreateStorage(rootPath);

            await Assert.ThrowsAsync<BadRequestException>(() =>
                storage.DeleteIfExistsAsync("../outside.txt"));
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, true);
            }
        }
    }

    [Fact]
    public async Task UploadAndDeleteUseConfiguredStorageRoot()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"lms-storage-{Guid.NewGuid():N}");

        try
        {
            var storage = CreateStorage(rootPath);
            await using var content = new MemoryStream([1, 2, 3, 4]);

            var storedFile = await storage.UploadAsync(
                new FileUpload("document.pdf", "application/pdf", content.Length, content),
                "courses/course/materials");
            var fullPath = Path.Combine(rootPath, storedFile.ObjectKey.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(File.Exists(fullPath));
            Assert.Equal(content.Length, new FileInfo(fullPath).Length);

            await storage.DeleteIfExistsAsync(storedFile.ObjectKey);

            Assert.False(File.Exists(fullPath));
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, true);
            }
        }
    }

    [Fact]
    public async Task UploadRejectsContentThatExceedsDeclaredLength()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"lms-storage-{Guid.NewGuid():N}");

        try
        {
            var storage = CreateStorage(rootPath);
            await using var content = new MemoryStream([1, 2, 3, 4]);

            await Assert.ThrowsAsync<BadRequestException>(() => storage.UploadAsync(
                new FileUpload("document.pdf", "application/pdf", 3, content),
                "courses/course/materials"));

            Assert.Empty(Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, true);
            }
        }
    }

    private static LocalFileStorage CreateStorage(string rootPath) =>
        new(
            Options.Create(new FileStorageOptions { RootPath = rootPath }),
            new FakeHostEnvironment(rootPath));

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
        }

        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "LMS.Domain.Tests";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
