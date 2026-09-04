namespace LMS.Api.application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services
    )
    {
        services.AddScoped<IServiceCollection, ServiceCollection>(); // DI chỗ này
        return services;
    }
}

