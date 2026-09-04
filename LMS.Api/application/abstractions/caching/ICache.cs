namespace LMS.Api.application.abstractions.caching;

public interface ICache
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    async Task<T?> ConsumeAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var value = await GetAsync<T>(key, cancellationToken);
        if (value is not null)
        {
            await RemoveAsync(key, cancellationToken);
        }

        return value;
    }
    Task SetAsync<T>(
        string key,
        T value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default);
    Task<bool> SetIfNotExistsAsync<T>(
        string key,
        T value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default);
    Task<long> IncrementAsync(
        string key,
        TimeSpan expiration,
        CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
