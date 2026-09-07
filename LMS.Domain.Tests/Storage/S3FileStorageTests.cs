using Amazon.S3;
using Amazon.S3.Model;
using LMS.Api.application.abstractions.storage;
using LMS.Api.domain.exceptions;
using LMS.Api.infrastructure;
using LMS.Api.infrastructure.storage.Local;
using LMS.Api.infrastructure.storage.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task OpenReadNormalizesSeparatorsAndReturnsResponseStream()
    {
        var responseContent = new MemoryStream([1, 2, 3]);
        var client = new Mock<IAmazonS3>();
        client
            .Setup(s3 => s3.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse { ResponseStream = responseContent });
        var storage = CreateStorage(client.Object);

        await using var content = await storage.OpenReadAsync("courses\\course\\file.pdf");
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy);

        Assert.Equal([1, 2, 3], copy.ToArray());
        client.Verify(s3 => s3.GetObjectAsync(
            It.Is<GetObjectRequest>(request =>
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

    [Fact]
    public void DisabledS3RegistersLocalStorage()
    {
        var services = new ServiceCollection();

        services.AddInfrastructure(CreateConfiguration(s3Enabled: false));

        var registration = services.Single(service => service.ServiceType == typeof(IFileStorage));
        Assert.Equal(typeof(LocalFileStorage), registration.ImplementationType);
    }

    [Fact]
    public void EnabledS3RegistersS3Storage()
    {
        var services = new ServiceCollection();

        services.AddInfrastructure(CreateConfiguration(s3Enabled: true));

        var registration = services.Single(service => service.ServiceType == typeof(IFileStorage));
        Assert.Equal(typeof(S3FileStorage), registration.ImplementationType);
    }

    private static S3FileStorage CreateStorage(IAmazonS3 client) =>
        new(
            Options.Create(new S3StorageOptions { BucketName = "test-bucket" }),
            client);

    private static IConfiguration CreateConfiguration(bool s3Enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test",
                ["ConnectionStrings:Redis"] = "localhost:6379",
                ["Jwt:Issuer"] = "LMS.Api",
                ["Jwt:Audience"] = "LMS.Client",
                ["Jwt:SigningKey"] = "integration-test-signing-key-that-is-at-least-32-bytes",
                ["S3Storage:Enabled"] = s3Enabled.ToString()
            })
            .Build();
}
