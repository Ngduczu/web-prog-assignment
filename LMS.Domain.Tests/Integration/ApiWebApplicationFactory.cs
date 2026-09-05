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
using LMS.Api.application.abstractions.caching;
using LMS.Api.domain.exceptions;
using LMS.Api.application.abstractions.auth;
using LMS.Api.application.features.chats;
using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.repositories;
using Moq;

namespace LMS.Domain.Tests.Integration;

public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly User _signalRUser = new(
        "signalr@example.com",
        "SignalR User",
        null,
        "unused",
        Role.Student,
        AccountStatus.Active);

    public bool UseRealJwtAuthentication { get; init; }
    public Guid SignalRUserId => _signalRUser.Id;
    public Mock<IChatService> SignalRChatService { get; } = CreateSignalRChatService();

    public void ExpireOAuthState(string state) =>
        Services.GetRequiredService<FakeCache>().RemoveForTest($"auth:oauth-state:{state}");

    public void ExpireOAuthLoginCode(string loginCode) =>
        Services.GetRequiredService<FakeOAuthLoginCodeStore>().Expire(loginCode);

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
                ["Jwt:Audience"] = "LMS.Client",
                ["OAuth:Google:ClientId"] = "integration-google-client",
                ["OAuth:Google:ClientSecret"] = "integration-google-secret",
                ["OAuth:Google:RedirectUri"] = "https://localhost/api/auth/oauth/Google/callback"
            });
        });
        builder.ConfigureTestServices(services =>
        {
            if (!UseRealJwtAuthentication)
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
            }
            else
            {
                services.RemoveAll<IUserRepository>();
                services.AddSingleton<IUserRepository>(new SignalRUserRepository(_signalRUser));
                services.RemoveAll<IChatService>();
                services.AddSingleton(SignalRChatService.Object);
            }

            services.RemoveAll<ApplicationAuthenticationService>();
            services.AddScoped<ApplicationAuthenticationService, FakeAuthenticationService>();
            services.RemoveAll<ICache>();
            services.AddSingleton<FakeCache>();
            services.AddSingleton<ICache>(provider => provider.GetRequiredService<FakeCache>());
            services.AddSingleton<FakeOAuthLoginCodeStore>();
        });
    }

    private static Mock<IChatService> CreateSignalRChatService()
    {
        var service = new Mock<IChatService>();
        service.Setup(chat => chat.EnsureRoomJoinAccessAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return service;
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

    private sealed class FakeAuthenticationService(FakeOAuthLoginCodeStore loginCodes) : ApplicationAuthenticationService
    {
        public Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateResponse());

        public Task<LoginResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateResponse());

        public Task<OAuthLoginCodeResponse> LoginWithOAuthAsync(
            OAuthLoginRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(loginCodes.Issue());

        public Task<LoginResponse> RedeemOAuthLoginCodeAsync(
            OAuthLoginCodeRequest request,
            CancellationToken cancellationToken = default)
        {
            if (!loginCodes.TryConsume(request.LoginCode))
            {
                throw new UnauthorizeException("OAuth login code is invalid or expired.");
            }

            return Task.FromResult(CreateResponse());
        }

        public Task<OAuthLinkResponse> LinkOAuthAsync(OAuthLinkRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OAuthLinkResponse(request.UserId, request.Provider, "external@example.com"));

        private static LoginResponse CreateResponse() => new(
            "integration-access-token",
            DateTime.UtcNow.AddMinutes(60),
            "integration-refresh-token",
            DateTime.UtcNow.AddDays(30));
    }

    private sealed class FakeOAuthLoginCodeStore
    {
        private readonly object _sync = new();
        private readonly HashSet<string> _codes = [];

        public OAuthLoginCodeResponse Issue()
        {
            var code = $"integration-login-code-{Guid.NewGuid():N}";
            lock (_sync)
            {
                _codes.Add(code);
            }

            return new OAuthLoginCodeResponse(code, DateTime.UtcNow.AddMinutes(2));
        }

        public bool TryConsume(string code)
        {
            lock (_sync)
            {
                return _codes.Remove(code);
            }
        }

        public void Expire(string code)
        {
            lock (_sync)
            {
                _codes.Remove(code);
            }
        }
    }

    private sealed class FakeCache : ICache
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, object> _values = [];

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                return Task.FromResult(_values.TryGetValue(key, out var value) ? (T?)value : default);
            }
        }

        public Task<T?> ConsumeAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                if (!_values.Remove(key, out var value)) return Task.FromResult<T?>(default);
                return Task.FromResult((T?)value);
            }
        }

        public Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            lock (_sync) _values[key] = value!;
            return Task.CompletedTask;
        }

        public Task<bool> SetIfNotExistsAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                if (_values.ContainsKey(key)) return Task.FromResult(false);
                _values[key] = value!;
                return Task.FromResult(true);
            }
        }

        public Task<long> IncrementAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                var value = _values.TryGetValue(key, out var current) ? (long)current + 1 : 1;
                _values[key] = value;
                return Task.FromResult(value);
            }
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            lock (_sync) _values.Remove(key);
            return Task.CompletedTask;
        }

        public void RemoveForTest(string key)
        {
            lock (_sync)
            {
                _values.Remove(key);
            }
        }
    }

    private sealed class SignalRUserRepository(User user) : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<User?>(id == user.Id ? user : null);

        public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            Task.FromResult<User?>(normalizedEmail == user.Email ? user : null);

        public Task<User?> GetByExternalLoginAsync(
            ExternalProvider provider,
            string externalUserId,
            CancellationToken cancellationToken) => Task.FromResult<User?>(null);

        public Task<ExternalUser?> GetExternalLoginAsync(
            Guid userId,
            ExternalProvider provider,
            CancellationToken cancellationToken) => Task.FromResult<ExternalUser?>(null);

        public void Add(User userToAdd) => throw new NotSupportedException();
        public void AddExternalUser(ExternalUser externalUser) => throw new NotSupportedException();
    }
}
