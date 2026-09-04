using Amazon.S3;
using Amazon.S3.Model;
using LMS.Api.application.abstractions.storage;
using LMS.Api.domain.exceptions;
using LMS.Api.infrastructure.storage.S3;
using Microsoft.Extensions.Options;
using Moq;

namespace LMS.Domain.Tests.Storage;

public sealed class S3FileStorageTests
{
    [Fact]
    public async Task UploadSendsValidatedObjectRequest()
    {
        var client = new Mock<IAmazonS3>();
        client
            .Setup(s3 => s3.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutObjectResponse());
        var storage = CreateStorage(client.Object);
        await using var content = new MemoryStream([1, 2, 3]);

        var storedFile = await storage.UploadAsync(
            new FileUpload("lesson.PDF", " application/pdf ", content.Length, content),
            "courses/course/materials",
            10);

        Assert.Equal("lesson.PDF", storedFile.FileName);
        Assert.Equal("application/pdf", storedFile.ContentType);
        Assert.Equal(3, storedFile.FileSize);
        Assert.StartsWith("courses/course/materials/", storedFile.ObjectKey);
        Assert.EndsWith(".pdf", storedFile.ObjectKey);
        client.Verify(s3 => s3.PutObjectAsync(
            It.Is<PutObjectRequest>(request =>
                request.BucketName == "test-bucket" &&
                request.Key == storedFile.ObjectKey &&
                request.ContentType == "application/pdf" &&
                ReferenceEquals(request.InputStream, content)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteNormalizesSeparatorsAndSendsRequest()
    {
        var client = new Mock<IAmazonS3>();
        client
            .Setup(s3 => s3.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());
        var storage = CreateStorage(client.Object);

        await storage.DeleteIfExistsAsync("courses\\course\\file.pdf");

        client.Verify(s3 => s3.DeleteObjectAsync(
            It.Is<DeleteObjectRequest>(request =>
                request.BucketName == "test-bucket" && request.Key == "courses/course/file.pdf"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteRejectsPathTraversalBeforeCallingS3()
    {
        var client = new Mock<IAmazonS3>();
        var storage = CreateStorage(client.Object);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            storage.DeleteIfExistsAsync("courses/../outside.txt"));

        client.Verify(s3 => s3.DeleteObjectAsync(
            It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static S3FileStorage CreateStorage(IAmazonS3 client) =>
        new(
            Options.Create(new S3StorageOptions { BucketName = "test-bucket" }),
            client);
}