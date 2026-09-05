using System.Net;
using System.Net.Http.Json;
using LMS.Api.application.features.auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;

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

    [Fact]
    public async Task OAuthCallbackRequiresTheBrowserThatStartedTheFlow()
    {
        using var factory = new ApiWebApplicationFactory();
        using var initiatingBrowser = CreateSecureClient(factory);
        using var otherBrowser = CreateSecureClient(factory);
        var state = await StartGoogleOAuthAsync(initiatingBrowser);

        var rejected = await otherBrowser.GetAsync(GetOAuthCallbackUrl(state));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var accepted = await initiatingBrowser.GetAsync(GetOAuthCallbackUrl(state));
        accepted.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task OAuthAuthorizeUsesRandomStateAndSecureHttpOnlyBrowserCookie()
    {
        using var factory = new ApiWebApplicationFactory();
        using var browser = CreateSecureClient(factory);

        var firstResponse = await browser.GetAsync("/api/auth/oauth/Google/authorize");
        var secondResponse = await browser.GetAsync("/api/auth/oauth/Google/authorize");
        firstResponse.EnsureSuccessStatusCode();
        secondResponse.EnsureSuccessStatusCode();
        var firstState = await ReadOAuthStateAsync(firstResponse);
        var secondState = await ReadOAuthStateAsync(secondResponse);
        var cookie = Assert.Single(firstResponse.Headers.GetValues("Set-Cookie"));

        Assert.NotEqual(firstState, secondState);
        Assert.True(firstState.Length >= 32);
        Assert.Contains("__Host-LMS.OAuth.", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=600", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OAuthCallbackRejectsBindingCookieFromAnotherFlow()
    {
        using var factory = new ApiWebApplicationFactory();
        using var firstBrowser = CreateSecureClient(factory);
        using var secondBrowser = CreateSecureClient(factory);
        var firstState = await StartGoogleOAuthAsync(firstBrowser);
        _ = await StartGoogleOAuthAsync(secondBrowser);

        var rejected = await secondBrowser.GetAsync(GetOAuthCallbackUrl(firstState));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var accepted = await firstBrowser.GetAsync(GetOAuthCallbackUrl(firstState));
        accepted.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task OAuthCallbackRejectsMissingWrongAndExpiredState()
    {
        using var factory = new ApiWebApplicationFactory();
        using var browser = CreateSecureClient(factory);

        var missing = await browser.GetAsync("/api/auth/oauth/Google/callback?code=integration-code");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var wrong = await browser.GetAsync(GetOAuthCallbackUrl("wrong-state"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var expiredState = await StartGoogleOAuthAsync(browser);
        factory.ExpireOAuthState(expiredState);
        var expired = await browser.GetAsync(GetOAuthCallbackUrl(expiredState));
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
    }

    [Fact]
    public async Task OAuthStateCanOnlyBeRedeemedOnce()
    {
        using var factory = new ApiWebApplicationFactory();
        using var browser = CreateSecureClient(factory);
        var state = await StartGoogleOAuthAsync(browser);

        var first = await browser.GetAsync(GetOAuthCallbackUrl(state));
        var replay = await browser.GetAsync(GetOAuthCallbackUrl(state));

        first.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task ConcurrentOAuthStateRedemptionHasOneWinner()
    {
        using var factory = new ApiWebApplicationFactory();
        using var browser = CreateSecureClient(factory);
        var state = await StartGoogleOAuthAsync(browser);

        var responses = await Task.WhenAll(
            browser.GetAsync(GetOAuthCallbackUrl(state)),
            browser.GetAsync(GetOAuthCallbackUrl(state)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OAuthLoginCallbackReturnsOnlyApplicationLoginCodeInBody()
    {
        using var factory = new ApiWebApplicationFactory();
        using var browser = CreateSecureClient(factory);
        var state = await StartGoogleOAuthAsync(browser);

        var response = await browser.GetAsync(GetOAuthCallbackUrl(state));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var result = await response.Content.ReadFromJsonAsync<OAuthLoginCodeResponse>();

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.LoginCode));
        Assert.DoesNotContain("accessToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(result.LoginCode, response.RequestMessage!.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OAuthLoginCodeCanBeExchangedOnceForTokenPair()
    {
        using var factory = new ApiWebApplicationFactory();
        using var browser = CreateSecureClient(factory);
        var callback = await CompleteGoogleOAuthLoginAsync(browser);

        var accepted = await browser.PostAsJsonAsync(
            "/api/auth/login-code",
            new OAuthLoginCodeRequest(callback.LoginCode));
        var replay = await browser.PostAsJsonAsync(
            "/api/auth/login-code",
            new OAuthLoginCodeRequest(callback.LoginCode));

        accepted.EnsureSuccessStatusCode();
        var tokens = await accepted.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(tokens);
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task ExpiredOAuthLoginCodeIsRejected()
    {
        using var factory = new ApiWebApplicationFactory();
        using var browser = CreateSecureClient(factory);
        var callback = await CompleteGoogleOAuthLoginAsync(browser);
        factory.ExpireOAuthLoginCode(callback.LoginCode);

        var response = await browser.PostAsJsonAsync(
            "/api/auth/login-code",
            new OAuthLoginCodeRequest(callback.LoginCode));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentOAuthLoginCodeRedemptionHasOneWinner()
    {
        using var factory = new ApiWebApplicationFactory();
        using var browser = CreateSecureClient(factory);
        var callback = await CompleteGoogleOAuthLoginAsync(browser);

        var responses = await Task.WhenAll(
            browser.PostAsJsonAsync(
                "/api/auth/login-code",
                new OAuthLoginCodeRequest(callback.LoginCode)),
            browser.PostAsJsonAsync(
                "/api/auth/login-code",
                new OAuthLoginCodeRequest(callback.LoginCode)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OAuthLinkMustBeStartedByAnAuthenticatedAccount()
    {
        using var factory = new ApiWebApplicationFactory();
        using var browser = CreateSecureClient(factory);

        var response = await browser.GetAsync("/api/auth/oauth/Google/link/authorize");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OAuthLinkCallbackUsesTheAccountCapturedAtAuthenticatedStart()
    {
        using var factory = new ApiWebApplicationFactory();
        using var browser = CreateSecureClient(factory);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/auth/oauth/Google/link/authorize");
        request.Headers.Add("X-Test-Role", "Student");
        var authorizationResponse = await browser.SendAsync(request);
        authorizationResponse.EnsureSuccessStatusCode();
        var state = await ReadOAuthStateAsync(authorizationResponse);

        var callbackResponse = await browser.GetAsync(GetOAuthCallbackUrl(state));
        callbackResponse.EnsureSuccessStatusCode();
        var result = await callbackResponse.Content.ReadFromJsonAsync<OAuthLinkResponse>();

        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.UserId);
        Assert.Equal("Google", result.Provider);
    }

    private static HttpClient CreateSecureClient(ApiWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost")
    });

    private static async Task<string> StartGoogleOAuthAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/oauth/Google/authorize");
        response.EnsureSuccessStatusCode();
        return await ReadOAuthStateAsync(response);
    }

    private static async Task<OAuthLoginCodeResponse> CompleteGoogleOAuthLoginAsync(HttpClient client)
    {
        var state = await StartGoogleOAuthAsync(client);
        var response = await client.GetAsync(GetOAuthCallbackUrl(state));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OAuthLoginCodeResponse>()
            ?? throw new InvalidOperationException("OAuth callback did not return a login code.");
    }

    private static async Task<string> ReadOAuthStateAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<OAuthAuthorizeResponse>();
        Assert.NotNull(body);
        return QueryHelpers.ParseQuery(new Uri(body.AuthorizationUrl).Query)["state"].ToString();
    }

    private static string GetOAuthCallbackUrl(string state) =>
        $"/api/auth/oauth/Google/callback?code=integration-code&state={Uri.EscapeDataString(state)}";

    private sealed record OAuthAuthorizeResponse(string AuthorizationUrl);
}
