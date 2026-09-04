using LMS.Api.infrastructure.Persistence;
using LMS.Api.presentation.middleware;
using Microsoft.EntityFrameworkCore;

namespace LMS.Api.infrastructure;

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
}