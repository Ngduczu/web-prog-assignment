using System.Security.Claims;
using LMS.Api.application.abstractions.auth;
using LMS.Api.domain.Enums.Users;

namespace LMS.Api.presentation.security;

public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => Guid.TryParse(
        Principal?.FindFirstValue(ClaimTypes.NameIdentifier),
        out var userId)
        ? userId
        : null;

    public Role? Role => Enum.TryParse<Role>(
        Principal?.FindFirstValue(ClaimTypes.Role),
        true,
        out var role)
        ? role
        : null;
}
