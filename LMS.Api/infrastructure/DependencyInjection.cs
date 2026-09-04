using LMS.Api.application.abstractions.persistence;
using LMS.Api.domain.repositories;
using LMS.Api.infrastructure.Persistence;
using LMS.Api.infrastructure.Persistence.Repositories;
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
        services.AddScoped<IUnitOfWork>(serviceProvider =>
            serviceProvider.GetRequiredService<AppDbContext>());
        services.AddScoped<ICourseRepository, CourseRepository>();
        services.AddScoped<IEnrollmentRepository, EnrollmentRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        return services;
    }
}
