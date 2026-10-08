using System.Security.Cryptography;
using System.Text;

namespace LMS.Api.application.features.auth;

internal static class AuthSecurity
{
    internal const string HumanCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    internal static string GenerateHumanCode(int length)
    {
        Span<char> characters = length <= 128
            ? stackalloc char[length]
            : new char[length];
        for (var index = 0; index < characters.Length; index++)
        {
            characters[index] = HumanCodeAlphabet[
                RandomNumberGenerator.GetInt32(HumanCodeAlphabet.Length)];
        }

        return new string(characters);
    }

    internal static string GenerateUrlToken(int byteLength) =>
        Base64Url(RandomNumberGenerator.GetBytes(byteLength));

    internal static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    internal static string Sha256Base64Url(string value) =>
        Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal static string Sha256AsciiBase64Url(string value) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(value)));

    internal static bool FixedTimeEquals(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(actual));

    internal static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
