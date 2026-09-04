using LMS.Api.application.abstractions.auth;
using Microsoft.AspNetCore.Authentication;

namespace LMS.Api.infrastructure.authentication.OAuth;

public class OAuthResolver
{
    IEnumerable<IOAuthProvider> _providers;
    
}