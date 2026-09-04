using LMS.Api.application.abstractions.auth;
using LMS.Api.presentation.middleware;
using LMS.Api.presentation.security;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace LMS.Api.presentation;

public static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddAuthorization();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var isAuthenticationEndpoint = context.Request.Path.StartsWithSegments("/api/auth");
                var limit = isAuthenticationEndpoint ? 10 : 240;
                var window = isAuthenticationEndpoint ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(1);
                var partitionKey = $"{context.Connection.RemoteIpAddress}:{isAuthenticationEndpoint}";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limit,
                        Window = window,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
        return services;
    }
}
