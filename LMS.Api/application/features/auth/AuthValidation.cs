using System.Net.Mail;
using LMS.Api.domain.exceptions;

namespace LMS.Api.application.features.auth;

internal static class AuthValidation
{
    internal const int HumanCodeLength = 6;

    internal static bool TryNormalizeEmail(string? value, out string normalizedEmail)
    {
        normalizedEmail = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(normalizedEmail) &&
               normalizedEmail.Length <= 255 &&
               MailAddress.TryCreate(normalizedEmail, out var address) &&
               address.Address == normalizedEmail;
    }

    internal static bool IsPasswordValid(string? password) =>
        !string.IsNullOrEmpty(password) && password.Length is >= 8 and <= 128;

    internal static bool IsHumanCodeValid(string? code) =>
        !string.IsNullOrWhiteSpace(code) &&
        code.Trim().Length == HumanCodeLength &&
        code.Trim().All(character =>
            AuthSecurity.HumanCodeAlphabet.Contains(char.ToUpperInvariant(character)));

    internal static string NormalizeEmail(string? value)
    {
        if (!TryNormalizeEmail(value, out var normalizedEmail))
        {
            throw new BadRequestException("Email address is invalid.");
        }

        return normalizedEmail;
    }

    internal static string NormalizeExternalEmail(string? value)
    {
        if (!TryNormalizeEmail(value, out var normalizedEmail))
        {
            throw new UnauthorizeException(
                "The external account returned an invalid email address.");
        }

        return normalizedEmail;
    }

    internal static string NormalizeExternalFullName(
        string? fullName,
        string normalizedEmail)
    {
        var value = string.IsNullOrWhiteSpace(fullName)
            ? normalizedEmail[..normalizedEmail.IndexOf('@')]
            : fullName.Trim();
        return Truncate(value, 100);
    }

    internal static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
