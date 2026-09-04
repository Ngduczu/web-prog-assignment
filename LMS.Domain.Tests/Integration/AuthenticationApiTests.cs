using System.Net;
using System.Net.Http.Json;
using LMS.Api.application.features.auth;

namespace LMS.Domain.Tests.Integration;

public sealed class AuthenticationApiTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthenticationApiTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ProtectedEndpointRequiresBearerToken()
    {
        var response = await _client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LoginResponseContainsTokensButDoesNotExposeAccountProfile()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("student@example.com", "password123"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();

        Assert.NotNull(body);
        Assert.Equal("integration-access-token", body.AccessToken);
        Assert.DoesNotContain(body.GetType().GetProperties(), property =>
            property.Name is "Email" or "Role" or "UserId");
    }

    [Fact]
    public async Task RefreshEndpointReturnsRotatedTokenPair()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshTokenRequest("old-refresh-token"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();

        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(body.RefreshToken));
    }

    [Fact]
    public async Task AdminOnlyEndpointRejectsStudentRole()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/examination-attempts/00000000-0000-0000-0000-000000000001/auto-submit?reason=DurationExpired");
        request.Headers.Add("X-Test-Role", "Student");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MissingMultipartFileReturnsBadRequest()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/lessons/00000000-0000-0000-0000-000000000001/materials");
        request.Headers.Add("X-Test-Role", "Teacher");
        request.Content = new MultipartFormDataContent();

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}