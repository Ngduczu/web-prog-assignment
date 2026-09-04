using LMS.Api.application.abstractions.auth;
using LMS.Api.application.features.registration;
using LMS.Api.application.features.passwordreset;
using LMS.Api.application.features.auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    
    [HttpPost("oauth")]
    [AllowAnonymous]
    public IActionResult LoginWithExternalProvider([FromQuery]string provider)
    {
        return StatusCode(StatusCodes.Status501NotImplemented, new ProblemDetails
        {
            Status = StatusCodes.Status501NotImplemented,
            Title = "OAuth is not implemented",
            Detail = $"OAuth provider '{provider}' is not available yet."
        });
    }
}
