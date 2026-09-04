using LMS.Api.application.abstractions.auth;
using Microsoft.AspNetCore.Identity;

namespace LMS.Api.infrastructure.authentication.PasswordHashing;

public sealed class AspNetCorePasswordHasher : IPasswordHasher
{
    private static readonly object HasherUser = new();
    private readonly PasswordHasher<object> _passwordHasher = new();

    public string Hash(string value) => _passwordHasher.HashPassword(HasherUser, value);

    public bool Verify(string value, string hash) =>
        _passwordHasher.VerifyHashedPassword(HasherUser, hash, value) != PasswordVerificationResult.Failed;
}
