using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LMS.Domain.Tests.Integration;

public sealed class SignalRAuthenticationTests : IClassFixture<ApiWebApplicationFactory>
{
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
}
