using LMS.Api.application.abstractions.auth;
using LMS.Api.application.features.registration;
using LMS.Api.application.features.passwordreset;
using LMS.Api.application.features.auth;
using LMS.Api.application.abstractions.caching;
using LMS.Api.domain.exceptions;
using LMS.Api.infrastructure.authentication.OAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace LMS.Api.presentation.controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private static readonly TimeSpan OAuthStateLifetime = TimeSpan.FromMinutes(10);
    private const string OAuthBindingCookiePrefix = "__Host-LMS.OAuth.";

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<RegistrationDto>> Register(
        [FromBody] RegisterAccountRequest request,
        [FromServices] IRegistrationService registrationService,
        CancellationToken cancellationToken)
    {
        var result = await registrationService.RegisterAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("verify-email")]
    [AllowAnonymous]
    public async Task<ActionResult<EmailVerificationResultDto>> VerifyEmail(
        [FromBody] VerifyEmailRequest request,
        [FromServices] IRegistrationService registrationService,
        CancellationToken cancellationToken)
    {
        return Ok(await registrationService.VerifyEmailAsync(request, cancellationToken));
    }

    [HttpPost("resend-verification")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendVerification(
        [FromBody] ResendVerificationRequest request,
        [FromServices] IRegistrationService registrationService,
        CancellationToken cancellationToken)
    {
        await registrationService.ResendVerificationAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<ActionResult<PasswordResetRequestedDto>> ForgotPassword(ForgotPasswordRequest request, IPasswordResetService service, CancellationToken ct)
        => Ok(await service.RequestAsync(request, ct));

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<ActionResult<PasswordResetResultDto>> ResetPassword(ResetPasswordRequest request, IPasswordResetService service, CancellationToken ct)
        => Ok(await service.ResetAsync(request, ct));

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, IAuthenticationService service, CancellationToken ct)
        => Ok(await service.LoginAsync(request, ct));

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Refresh(RefreshTokenRequest request, IAuthenticationService service, CancellationToken ct)
        => Ok(await service.RefreshAsync(request, ct));

    [HttpPost("login-code")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> RedeemOAuthLoginCode(
        OAuthLoginCodeRequest request,
        IAuthenticationService service,
        CancellationToken ct) =>
        Ok(await service.RedeemOAuthLoginCodeAsync(request, ct));

    [HttpGet("oauth/{provider}/authorize")]
    [AllowAnonymous]
    public async Task<ActionResult<object>> OAuthAuthorize(
        string provider,
        [FromServices] OAuthResolver resolver,
        [FromServices] ICache cache,
        [FromServices] IOptions<GoogleOAuthOptions> googleOptions,
        CancellationToken ct)
    {
        return await BeginOAuthAsync(
            provider,
            OAuthFlowPurpose.Login,
            null,
            resolver,
            cache,
            googleOptions,
            ct);
    }

    [HttpGet("oauth/{provider}/link/authorize")]
    [Authorize]
    public async Task<ActionResult<object>> OAuthLinkAuthorize(
        string provider,
        [FromServices] ICurrentUser currentUser,
        [FromServices] OAuthResolver resolver,
        [FromServices] ICache cache,
        [FromServices] IOptions<GoogleOAuthOptions> googleOptions,
        CancellationToken ct)
    {
        return await BeginOAuthAsync(
            provider,
            OAuthFlowPurpose.Link,
            currentUser.GetRequiredUserId(),
            resolver,
            cache,
            googleOptions,
            ct);
    }

    [HttpGet("oauth/{provider}/callback")]
    [AllowAnonymous]
    public async Task<ActionResult<object>> OAuthCallback(
        string provider,
        [FromQuery] string code,
        [FromQuery] string state,
        [FromServices] ICache cache,
        [FromServices] IAuthenticationService service,
        CancellationToken ct)
    {
        var stateKey = GetOAuthStateKey(state);
        var storedState = await cache.GetAsync<OAuthState>(stateKey, ct)
            ?? throw new BadRequestException("OAuth state is invalid or expired.");
        if (!storedState.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException("OAuth state does not match the provider.");
        }

        var bindingCookieName = GetOAuthBindingCookieName(state);
        if (!Request.Cookies.TryGetValue(bindingCookieName, out var browserBinding) ||
            string.IsNullOrWhiteSpace(browserBinding) ||
            !FixedTimeEquals(storedState.BrowserBindingHash, HashBrowserBinding(browserBinding)))
        {
            throw new BadRequestException("OAuth state is not valid for this browser.");
        }

        storedState = await cache.ConsumeAsync<OAuthState>(stateKey, ct)
            ?? throw new BadRequestException("OAuth state is invalid or expired.");
        Response.Cookies.Delete(bindingCookieName, CreateOAuthBindingCookieOptions(TimeSpan.Zero));
        SetNoStoreHeaders();
        if (storedState.Purpose == OAuthFlowPurpose.Login && storedState.UserId is null)
        {
            return Ok(await service.LoginWithOAuthAsync(
                new OAuthLoginRequest(provider, code, storedState.RedirectUri, storedState.CodeVerifier), ct));
        }

        if (storedState.Purpose == OAuthFlowPurpose.Link && storedState.UserId is not null)
        {
            return Ok(await service.LinkOAuthAsync(
                new OAuthLinkRequest(
                    storedState.UserId.Value,
                    provider,
                    code,
                    storedState.RedirectUri,
                    storedState.CodeVerifier),
                ct));
        }

        throw new BadRequestException("OAuth state has an invalid flow purpose.");
    }

    private async Task<ActionResult<object>> BeginOAuthAsync(
        string provider,
        OAuthFlowPurpose purpose,
        Guid? userId,
        OAuthResolver resolver,
        ICache cache,
        IOptions<GoogleOAuthOptions> googleOptions,
        CancellationToken ct)
    {
        if (!provider.Equals("Google", StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException($"OAuth provider '{provider}' is not supported.");
        }

        if (purpose == OAuthFlowPurpose.Link && userId is null ||
            purpose == OAuthFlowPurpose.Login && userId is not null)
        {
            throw new BadRequestException("OAuth flow purpose is invalid.");
        }

        var redirectUri = googleOptions.Value.RedirectUri;
        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            throw new InvalidOperationException("OAuth:Google:RedirectUri must be configured.");
        }

        var codeVerifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var codeChallenge = Base64Url(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(codeVerifier)));
        var browserBinding = Base64Url(RandomNumberGenerator.GetBytes(32));
        string? state = null;
        for (var attempt = 0; attempt < 3 && state is null; attempt++)
        {
            var candidate = Base64Url(RandomNumberGenerator.GetBytes(32));
            if (await cache.SetIfNotExistsAsync(
                    GetOAuthStateKey(candidate),
                    new OAuthState(
                        provider,
                        redirectUri,
                        codeVerifier,
                        HashBrowserBinding(browserBinding),
                        purpose,
                        userId),
                    OAuthStateLifetime,
                    ct))
            {
                state = candidate;
            }
        }

        if (state is null)
        {
            throw new InvalidOperationException("Could not create a unique OAuth state.");
        }

        Response.Cookies.Append(
            GetOAuthBindingCookieName(state),
            browserBinding,
            CreateOAuthBindingCookieOptions(OAuthStateLifetime));
        SetNoStoreHeaders();
        var url = resolver.Get(provider).BuildAuthorizationUrl(
            new OAuthAuthorizationRequest(redirectUri, state, codeChallenge));
        return Ok(new { authorizationUrl = url });
    }

    private void SetNoStoreHeaders()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
    }

    private static CookieOptions CreateOAuthBindingCookieOptions(TimeSpan maxAge) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        IsEssential = true,
        Path = "/",
        MaxAge = maxAge
    };

    private static string GetOAuthStateKey(string state) => $"auth:oauth-state:{state}";
    private static string GetOAuthBindingCookieName(string state) => $"{OAuthBindingCookiePrefix}{state}";
    private static string HashBrowserBinding(string value) =>
        Base64Url(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    private static bool FixedTimeEquals(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(expected),
            System.Text.Encoding.ASCII.GetBytes(actual));
    private sealed record OAuthState(
        string Provider,
        string RedirectUri,
        string CodeVerifier,
        string BrowserBindingHash,
        OAuthFlowPurpose Purpose,
        Guid? UserId);
    private enum OAuthFlowPurpose
    {
        Login,
        Link
    }
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
