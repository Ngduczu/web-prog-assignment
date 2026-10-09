using LMS.Api.application.abstractions.auth;
using LMS.Api.application.features.registration;
using LMS.Api.application.features.passwordreset;
using LMS.Api.application.features.auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using LMS.Api.infrastructure.authentication.OAuth;
using LMS.Api.presentation;

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
        [FromServices] IOAuthFlowService oauthFlow,
        CancellationToken ct)
    {
        var result = await oauthFlow.BeginLoginAsync(provider, ct);
        Response.AppendOAuthBindingCookie(result);
        Response.SetNoStore();
        return Ok(new { authorizationUrl = result.AuthorizationUrl });
    }

    [HttpGet("oauth/{provider}/link/authorize")]
    [Authorize]
    public async Task<ActionResult<object>> OAuthLinkAuthorize(
        string provider,
        [FromServices] ICurrentUser currentUser,
        [FromServices] IOAuthFlowService oauthFlow,
        CancellationToken ct)
    {
        var result = await oauthFlow.BeginLinkAsync(
            provider,
            currentUser.GetRequiredUserId(),
            ct);
        Response.AppendOAuthBindingCookie(result);
        Response.SetNoStore();
        return Ok(new { authorizationUrl = result.AuthorizationUrl });
    }

    [HttpGet("oauth/{provider}/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> OAuthCallback(
        string provider,
        [FromQuery] string code,
        [FromQuery] string state,
        [FromServices] IOAuthFlowService oauthFlow,
        [FromServices] IOptions<GoogleOAuthOptions> googleOptions,
        CancellationToken ct)
    {
        Request.Cookies.TryGetValue(
            OAuthHttpResponseExtensions.GetOAuthBindingCookieName(state),
            out var browserBinding);
        var result = await oauthFlow.CompleteAsync(
            new OAuthCallbackRequest(provider, code, state, browserBinding),
            ct);
        Response.SetNoStore();
        Response.DeleteOAuthBindingCookie(state);

        if (result.Payload is OAuthLoginCodeResponse loginCode &&
            provider.Equals("Google", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(googleOptions.Value.ClientCallbackUri))
        {
            return Redirect(QueryHelpers.AddQueryString(
                googleOptions.Value.ClientCallbackUri,
                "loginCode",
                loginCode.LoginCode));
        }

        return Ok(result.Payload);
    }
}
