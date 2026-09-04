using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.caching;
using LMS.Api.application.abstractions.mail;
using LMS.Api.application.abstractions.storage;
using LMS.Api.application.abstractions.time;
using LMS.Api.domain.repositories;
using LMS.Api.infrastructure.Persistence;
using LMS.Api.infrastructure.Persistence.Repositories;
using LMS.Api.infrastructure.authentication.PasswordHashing;
using LMS.Api.infrastructure.caching.Redis;
using LMS.Api.infrastructure.mail.Gmail;
using LMS.Api.infrastructure.storage.Local;
using LMS.Api.infrastructure.time;
using LMS.Api.infrastructure.authentication.JWT;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

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
        services.AddScoped<ITransactionManager>(serviceProvider =>
            serviceProvider.GetRequiredService<AppDbContext>());
        services.AddScoped<ICourseRepository, CourseRepository>();
        services.AddScoped<IEnrollmentRepository, EnrollmentRepository>();
        services.AddScoped<ILessonRepository, LessonRepository>();
        services.AddScoped<IMaterialRepository, MaterialRepository>();
        services.AddScoped<IExerciseRepository, ExerciseRepository>();
        services.AddScoped<ISubmissionRepository, SubmissionRepository>();
        services.AddScoped<IQuestionBankRepository, QuestionBankRepository>();
        services.AddScoped<IQuestionRepository, QuestionRepository>();
        services.AddScoped<IExaminationRepository, ExaminationRepository>();
        services.AddScoped<IExaminationAttemptRepository, ExaminationAttemptRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserManagementRepository, UserRepository>();
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IClock, SystemClock>();
        var redisConnectionString = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Redis connection string is not configured.");
        services.AddSingleton(new Lazy<IConnectionMultiplexer>(() =>
            ConnectionMultiplexer.Connect(redisConnectionString)));
        services.AddSingleton<ICache, RedisCache>();
        services.AddSingleton<IPasswordHasher, AspNetCorePasswordHasher>();
        services.Configure<MailOptions>(configuration.GetSection(MailOptions.SectionName));
        services.AddSingleton<IMailSender, GmailService>();
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey == "SET_VIA_ENVIRONMENT" || Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes long.");
        }
        services.AddSingleton<ITokenProvider, JwtTokenProvider>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = jwt.Issuer,
                ValidateAudience = true, ValidAudience = jwt.Audience,
                ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30)
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    var userId = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    var role = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
                    var versionValue = context.Principal?.FindFirst("auth_version")?.Value;
                    if (!Guid.TryParse(userId, out var parsedUserId) || !long.TryParse(versionValue, out var tokenVersion))
                    {
                        context.Fail("Invalid authentication claims.");
                        return;
                    }

                    var users = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
                    var user = await users.GetByIdAsync(parsedUserId, context.HttpContext.RequestAborted);
                    var cache = context.HttpContext.RequestServices.GetRequiredService<ICache>();
                    var currentVersion = await cache.GetAsync<long?>($"auth:version:{parsedUserId:N}", context.HttpContext.RequestAborted) ?? 1;
                    if (user is null || user.Status != LMS.Api.domain.Enums.Users.AccountStatus.Active || !string.Equals(role, user.Role.ToString(), StringComparison.OrdinalIgnoreCase) || tokenVersion != currentVersion)
                    {
                        context.Fail("User authentication is no longer valid.");
                    }
                }
            };
        });
        return services;
    }
}
