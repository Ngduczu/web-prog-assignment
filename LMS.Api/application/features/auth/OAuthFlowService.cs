using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.caching;
using LMS.Api.domain.exceptions;
using LMS.Api.infrastructure.authentication.OAuth;
using Microsoft.Extensions.Options;

namespace LMS.Api.application.features.auth;

public sealed class OAuthFlowService : IOAuthFlowService
{
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StateRecoveryLifetime = TimeSpan.FromMinutes(1);
    private readonly OAuthResolver _resolver;
    private readonly ICache _cache;
    private readonly IAuthenticationService _authentication;
    private readonly GoogleOAuthOptions _googleOptions;

    public OAuthFlowService(
        OAuthResolver resolver,
        ICache cache,
        IAuthenticationService authentication,
        IOptions<GoogleOAuthOptions> googleOptions)
    {
        _resolver = resolver;
        _cache = cache;
        _authentication = authentication;
        _googleOptions = googleOptions.Value;
    }

    public Task<OAuthAuthorizationResult> BeginLoginAsync(
        string provider,
        CancellationToken cancellationToken = default) =>
        BeginAsync(provider, OAuthFlowPurpose.Login, null, cancellationToken);

    public Task<OAuthAuthorizationResult> BeginLinkAsync(
        string provider,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new BadRequestException("The account to link is required.");
        }

        return BeginAsync(provider, OAuthFlowPurpose.Link, userId, cancellationToken);
    }

    public async Task<OAuthCallbackResult> CompleteAsync(
        OAuthCallbackRequest request,
        CancellationToken cancellationToken = default)
    {
        var stateKey = AuthCacheKeys.OAuthState(request.State);
        var storedState = await _cache.GetAsync<OAuthStateCacheEntry>(stateKey, cancellationToken)
            ?? throw new BadRequestException("OAuth state is invalid or expired.");
        if (!storedState.Provider.Equals(request.Provider, StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException("OAuth state does not match the provider.");
        }

        if (string.IsNullOrWhiteSpace(request.BrowserBinding) ||
            !AuthSecurity.FixedTimeEquals(
                storedState.BrowserBindingHash,
                AuthSecurity.Sha256Base64Url(request.BrowserBinding)))
        {
            throw new BadRequestException("OAuth state is not valid for this browser.");
        }

        storedState = await _cache.ConsumeAsync<OAuthStateCacheEntry>(stateKey, cancellationToken)
            ?? throw new BadRequestException("OAuth state is invalid or expired.");
        try
        {
            object payload = storedState.Purpose switch
            {
                OAuthFlowPurpose.Login when storedState.UserId is null =>
                    await _authentication.LoginWithOAuthAsync(
                        new OAuthLoginRequest(
                            request.Provider,
                            request.Code,
                            storedState.RedirectUri,
                            storedState.CodeVerifier),
                        cancellationToken),
                OAuthFlowPurpose.Link when storedState.UserId is not null =>
                    await _authentication.LinkOAuthAsync(
                        new OAuthLinkRequest(
                            storedState.UserId.Value,
                            request.Provider,
                            request.Code,
                            storedState.RedirectUri,
                            storedState.CodeVerifier),
                        cancellationToken),
                _ => throw new BadRequestException("OAuth state has an invalid flow purpose.")
            };

            return new OAuthCallbackResult(payload);
        }
        catch (Exception exception) when (AuthFailurePolicy.IsCredentialRestorable(exception))
        {
            await TryRestoreStateAsync(stateKey, storedState);
            throw;
        }
    }

    private async Task<OAuthAuthorizationResult> BeginAsync(
        string provider,
        OAuthFlowPurpose purpose,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        if (!provider.Equals("Google", StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException($"OAuth provider '{provider}' is not supported.");
        }

        var redirectUri = _googleOptions.RedirectUri;
        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            throw new InvalidOperationException("OAuth:Google:RedirectUri must be configured.");
        }

        var codeVerifier = AuthSecurity.GenerateUrlToken(32);
        var codeChallenge = AuthSecurity.Sha256AsciiBase64Url(codeVerifier);
        var browserBinding = AuthSecurity.GenerateUrlToken(32);
        string? state = null;
        for (var attempt = 0; attempt < 3 && state is null; attempt++)
        {
            var candidate = AuthSecurity.GenerateUrlToken(32);
            if (await _cache.SetIfNotExistsAsync(
                    AuthCacheKeys.OAuthState(candidate),
                    new OAuthStateCacheEntry(
                        provider,
                        redirectUri,
                        codeVerifier,
                        AuthSecurity.Sha256Base64Url(browserBinding),
                        purpose,
                        userId),
                    StateLifetime,
                    cancellationToken))
            {
                state = candidate;
            }
        }

        if (state is null)
        {
            throw new InvalidOperationException("Could not create a unique OAuth state.");
        }

        var authorizationUrl = _resolver.Get(provider).BuildAuthorizationUrl(
            new OAuthAuthorizationRequest(redirectUri, state, codeChallenge));
        return new OAuthAuthorizationResult(
            authorizationUrl,
            state,
            browserBinding,
            StateLifetime);
    }

    private async Task TryRestoreStateAsync(string key, OAuthStateCacheEntry state)
    {
        try
        {
            await _cache.SetIfNotExistsAsync(
                key,
                state,
                StateRecoveryLifetime,
                CancellationToken.None);
        }
        catch
        {
            // Preserve the original provider/application failure.
        }
    }

}
