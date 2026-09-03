using LMS.Api.infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LMS.Api.application;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>((option) =>
            option.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"))
        );
        return  services;
    }

    public static IServiceCollection AddApplication(
        this IServiceCollection services
    )
    {
        return services;
    }
}