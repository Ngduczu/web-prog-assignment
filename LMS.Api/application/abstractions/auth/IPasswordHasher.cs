namespace LMS.Api.application.abstractions.auth;

public interface IPasswordHasher
{
    string Hash(string value);
    bool Verify(string value, string hash);
}
