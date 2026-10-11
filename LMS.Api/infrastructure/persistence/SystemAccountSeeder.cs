using LMS.Api.application.abstractions.auth;
using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Users;
using Microsoft.EntityFrameworkCore;

namespace LMS.Api.infrastructure.Persistence;

public static class SystemAccountSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(SystemAccountSeeder));

        var email = configuration["SystemAccount:Email"]?.Trim().ToLowerInvariant();
        var password = configuration["SystemAccount:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            logger.LogInformation("System account seed skipped: SYSTEMACCOUNT__EMAIL or SYSTEMACCOUNT__PASSWORD is not set.");
            return;
        }

        if (password.Length is < 8 or > 128)
        {
            logger.LogWarning("System account seed skipped: password must contain between 8 and 128 characters.");
            return;
        }

        var db = services.GetRequiredService<AppDbContext>();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            var hasher = services.GetRequiredService<IPasswordHasher>();
            var fullName = email[..email.IndexOf('@')];
            db.Users.Add(new User(
                email,
                fullName.Length > 100 ? fullName[..100] : fullName,
                null,
                hasher.Hash(password),
                Role.System,
                AccountStatus.Active));
            logger.LogInformation("System account created for {Email}.", email);
        }
        else if (user.Role != Role.System)
        {
            user.SetRole(Role.System);
            user.RevokeSessions();
            logger.LogInformation("Existing account {Email} promoted to System.", email);
        }
        else
        {
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
