using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using LMS.Api.application.abstractions.auth;
using LMS.Api.application.features.chats;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace LMS.Domain.Tests.Integration;

public sealed class SignalRAuthenticationTests : IClassFixture<ApiWebApplicationFactory>
{
    private const string SigningKey = "integration-test-signing-key-that-is-at-least-32-bytes";
    private readonly ApiWebApplicationFactory _factory;

    public SignalRAuthenticationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/hubs/chat", true)]
    [InlineData("/hubs/chat/negotiate", true)]
    [InlineData("/api/users", false)]
    [InlineData("/hubs/chat-other", false)]
    public async Task QueryAccessTokenIsAcceptedOnlyForChatHub(string path, bool shouldAccept)
    {
        var options = _factory.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;
        httpContext.Request.QueryString = new QueryString("?access_token=query-token");
        var scheme = new AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme,
            JwtBearerDefaults.AuthenticationScheme,
            typeof(JwtBearerHandler));
        var context = new MessageReceivedContext(httpContext, scheme, options);

        await options.Events.OnMessageReceived(context);

        Assert.Equal(shouldAccept ? "query-token" : null, context.Token);
    }

    [Fact]
    public void ChatHubClosesConnectionsWhenAuthenticationExpires()
    {
        var dispatcherOptions = _factory.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .Select(endpoint => endpoint.Metadata.GetMetadata<HttpConnectionDispatcherOptions>())
            .Where(options => options is not null)
            .ToArray();

        Assert.Contains(dispatcherOptions, options => options!.CloseOnAuthenticationExpiration);
    }

    [Fact]
    public async Task ChatHubNegotiationRejectsMissingInvalidAndExpiredBearerTokens()
    {
        using var factory = new ApiWebApplicationFactory { UseRealJwtAuthentication = true };
        using var client = factory.CreateClient();

        var missing = await NegotiateAsync(client, null);
        var invalid = await NegotiateAsync(client, "not-a-jwt");
        var expired = await NegotiateAsync(
            client,
            CreateJwt(factory.SignalRUserId, DateTime.UtcNow.AddMinutes(-2)));

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Fact]
    public async Task NativeBearerHeaderCanAuthenticateHubNegotiation()
    {
        using var factory = new ApiWebApplicationFactory { UseRealJwtAuthentication = true };
        using var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<ITokenProvider>()
            .CreateAccessToken(factory.SignalRUserId, "signalr@example.com", "Student", 1)
            .Token;
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/hubs/chat/negotiate?negotiateVersion=1");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(string.Empty);

        using var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task PersistentAuthVersionRevokesExistingAccessToken()
    {
        using var factory = new ApiWebApplicationFactory { UseRealJwtAuthentication = true };
        using var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<ITokenProvider>()
            .CreateAccessToken(factory.SignalRUserId, "signalr@example.com", "Student", 1)
            .Token;
        factory.RevokeSignalRUserSessions();

        using var response = await NegotiateAsync(client, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedClientCanConnectReconnectAndInvokeProtectedHubMethod()
    {
        using var factory = new ApiWebApplicationFactory { UseRealJwtAuthentication = true };
        _ = factory.CreateClient();
        var token = factory.Services.GetRequiredService<ITokenProvider>()
            .CreateAccessToken(factory.SignalRUserId, "signalr@example.com", "Student", 1)
            .Token;
        var firstRoomId = Guid.NewGuid();
        var secondRoomId = Guid.NewGuid();

        await ConnectAndJoinAsync(factory, token, firstRoomId);
        await ConnectAndJoinAsync(factory, token, secondRoomId);

        factory.SignalRChatService.Verify(service => service.EnsureRoomJoinAccessAsync(
            firstRoomId,
            It.IsAny<CancellationToken>()), Times.Once);
        factory.SignalRChatService.Verify(service => service.EnsureRoomJoinAccessAsync(
            secondRoomId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EstablishedHubConnectionClosesWhenJwtExpires()
    {
        using var factory = new ApiWebApplicationFactory { UseRealJwtAuthentication = true };
        _ = factory.CreateClient();
        var token = CreateJwt(factory.SignalRUserId, DateTime.UtcNow.AddSeconds(4));
        using var socket = await ConnectAsync(factory, token);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[256];

        var result = await socket.ReceiveAsync(buffer, timeout.Token);

        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        Assert.Contains(
            "\"type\":7",
            Encoding.UTF8.GetString(buffer, 0, result.Count),
            StringComparison.Ordinal);
        result = await socket.ReceiveAsync(buffer, timeout.Token);
        Assert.Equal(WebSocketMessageType.Close, result.MessageType);
    }

    private static async Task<HttpResponseMessage> NegotiateAsync(HttpClient client, string? token)
    {
        var path = "/hubs/chat/negotiate?negotiateVersion=1";
        if (token is not null)
        {
            path += $"&access_token={Uri.EscapeDataString(token)}";
        }

        return await client.PostAsync(path, new StringContent(string.Empty));
    }

    private static async Task ConnectAndJoinAsync(
        ApiWebApplicationFactory factory,
        string token,
        Guid roomId)
    {
        using var socket = await ConnectAsync(factory, token);

        await SendTextAsync(
            socket,
            $"{{\"type\":1,\"invocationId\":\"1\",\"target\":\"JoinRoom\",\"arguments\":[\"{roomId}\"]}}\u001e");
        var completion = await ReceiveTextAsync(socket);
        Assert.Contains("\"type\":3", completion, StringComparison.Ordinal);
        Assert.DoesNotContain("\"error\"", completion, StringComparison.Ordinal);
    }

    private static async Task<WebSocket> ConnectAsync(
        ApiWebApplicationFactory factory,
        string token)
    {
        using var httpClient = factory.CreateClient();
        using var negotiation = await NegotiateAsync(httpClient, token);
        negotiation.EnsureSuccessStatusCode();
        var payload = await negotiation.Content.ReadFromJsonAsync<NegotiationResponse>()
            ?? throw new InvalidOperationException("SignalR negotiation returned an empty response.");
        var webSocketClient = factory.Server.CreateWebSocketClient();
        var socket = await webSocketClient.ConnectAsync(
            new Uri(
                $"ws://localhost/hubs/chat?id={Uri.EscapeDataString(payload.ConnectionToken)}" +
                $"&access_token={Uri.EscapeDataString(token)}"),
            CancellationToken.None);

        await SendTextAsync(socket, "{\"protocol\":\"json\",\"version\":1}\u001e");
        var handshake = await ReceiveTextAsync(socket);
        Assert.Equal("{}\u001e", handshake);
        return socket;
    }

    private static async Task SendTextAsync(WebSocket socket, string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<string> ReceiveTextAsync(WebSocket socket)
    {
        var buffer = new byte[4096];
        using var content = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            content.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(content.ToArray());
    }

    private static string CreateJwt(Guid userId, DateTime expires)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            "LMS.Api",
            "LMS.Client",
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Email, "signalr@example.com"),
                new Claim(ClaimTypes.Role, "Student"),
                new Claim("auth_version", "1")
            ],
            notBefore: expires.AddMinutes(-5),
            expires: expires,
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record NegotiationResponse(string ConnectionToken);
}
