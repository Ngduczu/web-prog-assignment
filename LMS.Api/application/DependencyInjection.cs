using LMS.Api.application.features.coursecontents;
using LMS.Api.application.features.courses;
using LMS.Api.application.features.enrollments;
using LMS.Api.application.features.examinations;
using LMS.Api.application.features.examinationattempts;
using LMS.Api.application.features.questions;
using LMS.Api.application.features.registration;
using LMS.Api.application.features.users;
using LMS.Api.application.features.passwordreset;
using LMS.Api.application.features.auth;

namespace LMS.Api.application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services
    )
    {
        services.AddScoped<ICourseService, CourseService>();
        services.AddScoped<IEnrollmentService, EnrollmentService>();
        services.AddScoped<ILessonService, LessonService>();
        services.AddScoped<IExerciseService, ExerciseService>();
        services.AddScoped<ISubmissionService, SubmissionService>();
        services.AddScoped<IQuestionService, QuestionService>();
        services.AddScoped<IExaminationService, ExaminationService>();
        services.AddScoped<IExaminationAttemptService, ExaminationAttemptService>();
        services.AddScoped<IRegistrationService, RegistrationService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        return services;
    }
}
