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

    [HttpGet("oauth/{provider}/authorize")]
    [AllowAnonymous]
    public async Task<ActionResult<object>> OAuthAuthorize(
        string provider,
        [FromServices] OAuthResolver resolver,
        [FromServices] ICache cache,
        [FromServices] IOptions<GoogleOAuthOptions> googleOptions,
        CancellationToken ct)
    {
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var codeVerifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var codeChallenge = Base64Url(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(codeVerifier)));
        if (!provider.Equals("Google", StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException($"OAuth provider '{provider}' is not supported.");
        }

        var redirectUri = googleOptions.Value.RedirectUri;
        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            throw new InvalidOperationException("OAuth:Google:RedirectUri must be configured.");
        }

        await cache.SetAsync($"auth:oauth-state:{state}", new OAuthState(provider, redirectUri, codeVerifier), TimeSpan.FromMinutes(10), ct);
        var url = resolver.Get(provider).BuildAuthorizationUrl(new OAuthAuthorizationRequest(redirectUri, state, codeChallenge));
        return Ok(new { authorizationUrl = url });
    }

    [HttpGet("oauth/{provider}/callback")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> OAuthCallback(
        string provider,
        [FromQuery] string code,
        [FromQuery] string state,
        [FromServices] ICache cache,
        [FromServices] IAuthenticationService service,
        CancellationToken ct)
    {
        var storedState = await cache.ConsumeAsync<OAuthState>($"auth:oauth-state:{state}", ct)
            ?? throw new BadRequestException("OAuth state is invalid or expired.");
        if (!storedState.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException("OAuth state does not match the provider.");
        }

        return Ok(await service.LoginWithOAuthAsync(
            new OAuthLoginRequest(provider, code, storedState.RedirectUri, storedState.CodeVerifier), ct));
    }

    private sealed record OAuthState(string Provider, string RedirectUri, string CodeVerifier);
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
