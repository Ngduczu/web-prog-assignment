namespace LMS.Api.infrastructure.storage.Local;

public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";
    public string RootPath { get; init; } = "storage";
}
