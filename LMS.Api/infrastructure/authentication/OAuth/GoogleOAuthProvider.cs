using LMS.Api.application.abstractions.auth;
using LMS.Api.domain.exceptions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace LMS.Api.infrastructure.authentication.OAuth;

public sealed class GoogleOAuthProvider : IOAuthProvider
{
    private readonly HttpClient _httpClient;
    private readonly GoogleOAuthOptions _options;

    public GoogleOAuthProvider(HttpClient httpClient, IOptions<GoogleOAuthOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public string Name => "Google";

    public string BuildAuthorizationUrl(OAuthAuthorizationRequest request)
    {
        EnsureConfigured();
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = request.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid email profile",
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = request.State
        };

        if (!string.IsNullOrWhiteSpace(request.CodeChallenge))
        {
            query["code_challenge"] = request.CodeChallenge;
            query["code_challenge_method"] = "S256";
        }

        return QueryHelpers.AddQueryString(
            "https://accounts.google.com/o/oauth2/v2/auth",
            query);
    }

    public async Task<OAuthTokenResult> ExchangeCodeAsync(
        string code,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = codeVerifier
        });
        using var response = await SendAsync(
            () => _httpClient.PostAsync("https://oauth2.googleapis.com/token", content, cancellationToken),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Google returned an empty token response.");
        return new OAuthTokenResult(
            token.AccessToken,
            token.RefreshToken,
            token.ExpiresIn is null ? null : DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn.Value),
            token.TokenType ?? "Bearer");
    }

    public async Task<OAuthUserInfo> GetUserInfoAsync(
        OAuthTokenResult token,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://openidconnect.googleapis.com/v1/userinfo");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            token.TokenType,
            token.AccessToken);

        using var response = await SendAsync(
            () => _httpClient.SendAsync(request, cancellationToken),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var user = await response.Content.ReadFromJsonAsync<GoogleUserInfoResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Google returned an empty user response.");

        if (string.IsNullOrWhiteSpace(user.Subject) || string.IsNullOrWhiteSpace(user.Email))
        {
            throw new InvalidOperationException("Google user response is missing required fields.");
        }

        return new OAuthUserInfo(user.Subject, user.Email, user.Name, user.Picture, user.EmailVerified);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        Func<Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        try
        {
            return await send();
        }
        catch (HttpRequestException exception) when (exception.StatusCode is null)
        {
            throw new ExternalServiceUnavailableException("Unable to reach Google. Please try again later.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ExternalServiceUnavailableException("Google did not respond in time. Please try again later.", exception);
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) ||
            string.IsNullOrWhiteSpace(_options.ClientSecret) ||
            string.IsNullOrWhiteSpace(_options.RedirectUri))
        {
            throw new InvalidOperationException("OAuth:Google ClientId, ClientSecret and RedirectUri must be configured.");
        }
    }

    private sealed record GoogleTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn,
        [property: JsonPropertyName("token_type")] string? TokenType);

    private sealed record GoogleUserInfoResponse(
        [property: JsonPropertyName("sub")] string Subject,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("picture")] string? Picture,
        [property: JsonPropertyName("email_verified")] bool EmailVerified);
}
