using LMS.Api.application.features.auth;

namespace LMS.Api.presentation;

internal static class OAuthHttpResponseExtensions
{
    private const string BindingCookiePrefix = "__Host-LMS.OAuth.";

    internal static string GetOAuthBindingCookieName(string state) =>
        $"{BindingCookiePrefix}{state}";

    internal static void AppendOAuthBindingCookie(
        this HttpResponse response,
        OAuthAuthorizationResult result)
    {
        response.Cookies.Append(
            GetOAuthBindingCookieName(result.State),
            result.BrowserBinding,
            CreateCookieOptions(result.Lifetime));
    }

    internal static void DeleteOAuthBindingCookie(
        this HttpResponse response,
        string state)
    {
        response.Cookies.Delete(
            GetOAuthBindingCookieName(state),
            CreateCookieOptions(TimeSpan.Zero));
    }

    internal static void SetNoStore(this HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.Pragma = "no-cache";
    }

    private static CookieOptions CreateCookieOptions(TimeSpan maxAge) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        IsEssential = true,
        Path = "/",
        MaxAge = maxAge
    };
}
