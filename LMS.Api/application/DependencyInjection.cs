using LMS.Api.application.features.courses;
using LMS.Api.application.features.enrollments;

namespace LMS.Api.application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services
    )
    {
        services.AddScoped<ICourseService, CourseService>();
        services.AddScoped<IEnrollmentService, EnrollmentService>();
        return services;
    }
}
