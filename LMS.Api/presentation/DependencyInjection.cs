using LMS.Api.application.abstractions.auth;
using LMS.Api.presentation.middleware;
using LMS.Api.presentation.security;

namespace LMS.Api.presentation;

public static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddAuthorization();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
        return services;
    }
}
