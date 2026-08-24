using System.Security.Cryptography;

namespace SoulExe.Services;

public static class MobileAccessPasswordHasher
{
    private const int Iterations = 210_000;
    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (password.Length > 1024) throw new ArgumentOutOfRangeException(nameof(password));
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string? encoded)
    {
        if (string.IsNullOrEmpty(password) || password.Length > 1024) return false;
        var parts = encoded?.Split('$');
        if (parts is not { Length: 4 } || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out var iterations) || iterations is < 100_000 or > 1_000_000) return false;
        try { return CryptographicOperations.FixedTimeEquals(Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[2]), iterations, HashAlgorithmName.SHA256, 32), Convert.FromBase64String(parts[3])); }
        catch (Exception exception) when (exception is FormatException or CryptographicException or ArgumentException) { return false; }
    }
}
