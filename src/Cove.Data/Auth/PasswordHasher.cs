using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Cove.Data.Auth;

public static class PasswordHasher
{
    public const string Algorithm = "argon2id";

    private const int SaltSize = 16;
    private const int DefaultHashSize = 32;
    private const int DefaultMemorySizeKiB = 65536;
    private const int DefaultIterations = 3;
    private const int DefaultParallelism = 2;
    private const int Argon2Version = 19;

    private static readonly string DummyHash = HashPassword("cove_dummy_password");

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = DeriveHash(password, salt, DefaultHashSize, DefaultMemorySizeKiB, DefaultIterations, DefaultParallelism);
        return $"$argon2id$v={Argon2Version}$m={DefaultMemorySizeKiB},t={DefaultIterations},p={DefaultParallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string storedHash, string? algorithm)
    {
        if (string.IsNullOrWhiteSpace(storedHash))
            return false;

        var detectedAlgorithm = DetectAlgorithm(storedHash, algorithm);
        return detectedAlgorithm switch
        {
            Algorithm => VerifyArgon2Id(password, storedHash),
            "bcrypt" => VerifyBcrypt(password, storedHash),
            _ => false,
        };
    }

    public static bool NeedsRehash(string storedHash, string? algorithm)
    {
        if (DetectAlgorithm(storedHash, algorithm) != Algorithm)
            return true;

        if (!TryParseArgon2IdHash(storedHash, out var parsed))
            return true;

        return parsed.MemorySizeKiB != DefaultMemorySizeKiB
            || parsed.Iterations != DefaultIterations
            || parsed.Parallelism != DefaultParallelism
            || parsed.Hash.Length != DefaultHashSize;
    }

    public static string DetectAlgorithm(string? storedHash, string? algorithm = null)
    {
        if (!string.IsNullOrWhiteSpace(algorithm))
        {
            var normalized = algorithm.Trim().ToLowerInvariant();
            if (normalized is Algorithm or "bcrypt")
                return normalized;
        }

        if (!string.IsNullOrWhiteSpace(storedHash) && storedHash.StartsWith("$argon2id$", StringComparison.Ordinal))
            return Algorithm;

        return "bcrypt";
    }

    public static void VerifyDummy(string password)
    {
        _ = VerifyArgon2Id(password, DummyHash);
    }

    private static bool VerifyArgon2Id(string password, string storedHash)
    {
        if (!TryParseArgon2IdHash(storedHash, out var parsed))
            return false;

        try
        {
            var computed = DeriveHash(password, parsed.Salt, parsed.Hash.Length, parsed.MemorySizeKiB, parsed.Iterations, parsed.Parallelism);
            return CryptographicOperations.FixedTimeEquals(computed, parsed.Hash);
        }
        catch
        {
            return false;
        }
    }

    private static bool VerifyBcrypt(string password, string storedHash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, storedHash);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] DeriveHash(string password, byte[] salt, int hashSize, int memorySizeKiB, int iterations, int parallelism)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memorySizeKiB,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };

        return argon2.GetBytes(hashSize);
    }

    private static bool TryParseArgon2IdHash(string storedHash, out ParsedHash parsed)
    {
        parsed = default;

        if (string.IsNullOrWhiteSpace(storedHash))
            return false;

        var parts = storedHash.Split('$', StringSplitOptions.None);
        if (parts.Length != 6 || !string.Equals(parts[1], "argon2id", StringComparison.Ordinal))
            return false;

        if (!TryParseVersion(parts[2], out var version) || version != Argon2Version)
            return false;

        if (!TryParseParameters(parts[3], out var memorySizeKiB, out var iterations, out var parallelism))
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[4]);
            var hash = Convert.FromBase64String(parts[5]);
            parsed = new ParsedHash(version, memorySizeKiB, iterations, parallelism, salt, hash);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseVersion(string segment, out int version)
    {
        version = 0;
        return segment.StartsWith("v=", StringComparison.Ordinal)
            && int.TryParse(segment[2..], out version);
    }

    private static bool TryParseParameters(string segment, out int memorySizeKiB, out int iterations, out int parallelism)
    {
        memorySizeKiB = 0;
        iterations = 0;
        parallelism = 0;

        foreach (var part in segment.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("m=", StringComparison.Ordinal) && int.TryParse(part[2..], out var memory))
            {
                memorySizeKiB = memory;
                continue;
            }

            if (part.StartsWith("t=", StringComparison.Ordinal) && int.TryParse(part[2..], out var rounds))
            {
                iterations = rounds;
                continue;
            }

            if (part.StartsWith("p=", StringComparison.Ordinal) && int.TryParse(part[2..], out var lanes))
                parallelism = lanes;
        }

        return memorySizeKiB > 0 && iterations > 0 && parallelism > 0;
    }

    private readonly record struct ParsedHash(
        int Version,
        int MemorySizeKiB,
        int Iterations,
        int Parallelism,
        byte[] Salt,
        byte[] Hash);
}