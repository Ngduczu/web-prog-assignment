using LMS.Api.application.abstractions.auth;
using Microsoft.AspNetCore.Authentication;

namespace LMS.Api.infrastructure.authentication.OAuth;

public class OAuthResolver
{
    private readonly IReadOnlyDictionary<string, IOAuthProvider> _providers;

    public OAuthResolver(IEnumerable<IOAuthProvider> providers)
    {
        _providers = providers.ToDictionary(provider => provider.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IOAuthProvider Get(string provider) =>
        _providers.TryGetValue(provider, out var result)
            ? result
            : throw new KeyNotFoundException($"OAuth provider '{provider}' is not configured.");
}