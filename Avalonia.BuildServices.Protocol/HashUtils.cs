using System;
using System.Security.Cryptography;
using System.Text;

namespace Avalonia.Telemetry;

/// <summary>
/// Canonical hashing primitive used across Avalonia telemetry surfaces.
/// </summary>
public static class HashUtils
{
    public static string Sha256Hex(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var textData = Encoding.UTF8.GetBytes(value);

#if NET8_0_OR_GREATER
        var hash = SHA256.HashData(textData);
#else
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(textData);
#endif

        return BitConverter.ToString(hash).Replace("-", string.Empty);
    }
}
