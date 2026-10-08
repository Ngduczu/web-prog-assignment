using System.Security.Cryptography;
using System.Text;
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
        var stateKey = GetStateKey(request.State);
        var storedState = await _cache.GetAsync<OAuthState>(stateKey, cancellationToken)
            ?? throw new BadRequestException("OAuth state is invalid or expired.");
        if (!storedState.Provider.Equals(request.Provider, StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException("OAuth state does not match the provider.");
        }

        if (string.IsNullOrWhiteSpace(request.BrowserBinding) ||
            !FixedTimeEquals(
                storedState.BrowserBindingHash,
                HashBrowserBinding(request.BrowserBinding)))
        {
            throw new BadRequestException("OAuth state is not valid for this browser.");
        }

        storedState = await _cache.ConsumeAsync<OAuthState>(stateKey, cancellationToken)
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
        catch (Exception exception) when (IsCredentialRestorableFailure(exception))
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

        var codeVerifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var codeChallenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        var browserBinding = Base64Url(RandomNumberGenerator.GetBytes(32));
        string? state = null;
        for (var attempt = 0; attempt < 3 && state is null; attempt++)
        {
            var candidate = Base64Url(RandomNumberGenerator.GetBytes(32));
            if (await _cache.SetIfNotExistsAsync(
                    GetStateKey(candidate),
                    new OAuthState(
                        provider,
                        redirectUri,
                        codeVerifier,
                        HashBrowserBinding(browserBinding),
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

    private async Task TryRestoreStateAsync(string key, OAuthState state)
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

    private static string GetStateKey(string state) => $"auth:oauth-state:{state}";
    private static string HashBrowserBinding(string value) =>
        Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool FixedTimeEquals(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(actual));
    private static bool IsCredentialRestorableFailure(Exception exception) =>
        exception is not BadRequestException
            and not UnauthorizeException
            and not ForbiddenException
            and not NotFoundException
            and not ConflictException;
    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record OAuthState(
        string Provider,
        string RedirectUri,
        string CodeVerifier,
        string BrowserBindingHash,
        OAuthFlowPurpose Purpose,
        Guid? UserId);
}
