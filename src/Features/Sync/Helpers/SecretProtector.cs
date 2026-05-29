using System;
using System.Security.Cryptography;
using System.Text;

namespace OrderLog.Features.Sync.Helpers;

/// <summary>
/// Tiny wrapper around Windows DPAPI for protecting secrets at rest. The
/// encryption is keyed to the current user account, so the protected value
/// can only be decrypted by the same Windows user on the same machine.
/// Persisted as base64.
/// </summary>
public static class SecretProtector
{
    // App-specific entropy ensures even an attacker with raw DPAPI access to
    // another file from this same user can't trivially decrypt OUR blobs.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OrderLog.Sync.v1");

    public static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64)) return string.Empty;
        try
        {
            var protectedBytes = Convert.FromBase64String(protectedBase64);
            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            // If decryption fails (different user, corrupted, etc.) treat
            // the secret as missing so the user gets re-prompted to enter
            // their master key rather than the app crashing.
            return string.Empty;
        }
    }
}
