using System.Security.Cryptography;
using System.Text;

namespace AvellSucks.Core.Service;

/// <summary>
/// Hashes bearer tokens and compares hashes in constant time. Only the hash is
/// ever stored or logged; the plaintext token exists only in the request header
/// and, briefly, in the UI at generation time.
/// </summary>
public static class TokenHasher
{
    /// <summary>Lowercase hex SHA-256 of the token's UTF-8 bytes.</summary>
    public static string HashHex(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>
    /// Constant-time comparison of two hex hashes (case-insensitive). Returns
    /// false if either is null or their byte lengths differ. Uses
    /// <see cref="CryptographicOperations.FixedTimeEquals"/> to avoid leaking
    /// match position via timing.
    /// </summary>
    public static bool FixedTimeEqualsHex(string? presentedHashHex, string? expectedHashHex)
    {
        if (presentedHashHex is null || expectedHashHex is null) return false;
        byte[] a, b;
        try
        {
            a = Convert.FromHexString(presentedHashHex);
            b = Convert.FromHexString(expectedHashHex);
        }
        catch (FormatException)
        {
            return false;
        }
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
