using System;
using System.Security.Cryptography;
using System.Text;

namespace EorzeaBingo;

/// <summary>
/// Provides stable, deterministic hashing for bingo seed generation.
/// Do not use string.GetHashCode() as it is randomized per .NET runtime.
/// </summary>
public static class CryptoUtils
{
    /// <summary>
    /// Converts a seed string to a stable 32-bit integer using SHA256.
    /// Same input always produces the same output across runs and machines.
    /// </summary>
    public static int SeedToInt32(string seedString)
    {
        if (string.IsNullOrEmpty(seedString))
            return 0;

        var bytes = Encoding.UTF8.GetBytes(seedString);
        var hash = SHA256.HashData(bytes);

        // Use first 4 bytes for a deterministic int32 seed
        return BitConverter.ToInt32(hash, 0);
    }
}
