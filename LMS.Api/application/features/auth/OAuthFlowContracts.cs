namespace LMS.Api.application.features.auth;

public enum OAuthFlowPurpose
{
    Login,
    Link
}

public sealed record OAuthAuthorizationResult(
    string AuthorizationUrl,
    string State,
    string BrowserBinding,
    TimeSpan Lifetime);

public sealed record OAuthCallbackRequest(
    string Provider,
    string Code,
    string State,
    string? BrowserBinding);

public sealed record OAuthCallbackResult(object Payload);

public interface IOAuthFlowService
{
    Task<OAuthAuthorizationResult> BeginLoginAsync(
        string provider,
        CancellationToken cancellationToken = default);

    Task<OAuthAuthorizationResult> BeginLinkAsync(
        string provider,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<OAuthCallbackResult> CompleteAsync(
        OAuthCallbackRequest request,
        CancellationToken cancellationToken = default);
}
