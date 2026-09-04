using ApplicationAuthenticationService = LMS.Api.application.features.auth.IAuthenticationService;
using LMS.Api.application.features.auth;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LMS.Domain.Tests.Integration;

public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-that-is-at-least-32-bytes");
        builder.UseSetting("Jwt:Issuer", "LMS.Api");
        builder.UseSetting("Jwt:Audience", "LMS.Client");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            foreach (var source in configuration.Sources
                         .OfType<EnvironmentVariablesConfigurationSource>()
                         .ToArray())
            {
                configuration.Sources.Remove(source);
            }

            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5433;Database=test;Username=test;Password=test",
                ["ConnectionStrings:Redis"] = "localhost:6379,abortConnect=false",
                ["Jwt:SigningKey"] = "integration-test-signing-key-that-is-at-least-32-bytes",
                ["Jwt:Issuer"] = "LMS.Api",
                ["Jwt:Audience"] = "LMS.Client"
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
            }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
            services.RemoveAll<ApplicationAuthenticationService>();
            services.AddScoped<ApplicationAuthenticationService, FakeAuthenticationService>();
        });
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            if (string.IsNullOrWhiteSpace(role))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role)],
                "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class FakeAuthenticationService : ApplicationAuthenticationService
    {
        public Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateResponse());

        public Task<LoginResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateResponse());

        public Task<LoginResponse> LoginWithOAuthAsync(OAuthLoginRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateResponse());

        private static LoginResponse CreateResponse() => new(
            "integration-access-token",
            DateTime.UtcNow.AddMinutes(60),
            "integration-refresh-token",
            DateTime.UtcNow.AddDays(30));
    }
}