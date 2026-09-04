using LMS.Api.application.abstractions.caching;
using StackExchange.Redis;
using System.Text.Json;

namespace LMS.Api.infrastructure.caching.Redis;

public sealed class RedisCache : ICache
{
    private const string GetAndDeleteScript = "local value = redis.call('GET', KEYS[1]); if value then redis.call('DEL', KEYS[1]); end; return value;";
    private const string IncrementScript = "local value = redis.call('INCR', KEYS[1]); if value == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[1]); end; return value;";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly Lazy<IConnectionMultiplexer> _connection;

    public RedisCache(Lazy<IConnectionMultiplexer> connection)
    {
        _connection = connection;
    }

    private IDatabase Database => _connection.Value.GetDatabase();

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await Database.StringGetAsync(key);
        return value.IsNull ? default : JsonSerializer.Deserialize<T>(value.ToString(), SerializerOptions);
    }

    public async Task<T?> ConsumeAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await Database.ScriptEvaluateAsync(GetAndDeleteScript, [key]);
        return value.IsNull
            ? default
            : JsonSerializer.Deserialize<T>(value.ToString(), SerializerOptions);
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Database.StringSetAsync(key, JsonSerializer.Serialize(value, SerializerOptions), expiration);
    }

    public async Task<bool> SetIfNotExistsAsync<T>(
        string key,
        T value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Database.StringSetAsync(
            key,
            JsonSerializer.Serialize(value, SerializerOptions),
            expiration,
            When.NotExists);
    }

    public async Task<long> IncrementAsync(
        string key,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await Database.ScriptEvaluateAsync(
            IncrementScript,
            [key],
            [(long)expiration.TotalMilliseconds]);
        return (long)result;
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Database.KeyDeleteAsync(key);
    }
}
