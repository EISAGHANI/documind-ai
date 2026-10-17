using System.Security.Cryptography;
using System.Text;

namespace DocuMind.Api.Tenancy;

public static class InvitationTokens
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    // 32 random bytes, URL-safe, so the token can go straight into an invitation link
    public static string Create() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
