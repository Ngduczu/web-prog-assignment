namespace LMS.Api.infrastructure.storage.S3;

public sealed class S3StorageOptions
{
    public const string SectionName = "S3Storage";

    public bool Enabled { get; init; }
    public string BucketName { get; init; } = string.Empty;
    public string Region { get; init; } = "us-east-1";
    public string? ServiceUrl { get; init; }
    public string AccessKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public bool ForcePathStyle { get; init; }
}