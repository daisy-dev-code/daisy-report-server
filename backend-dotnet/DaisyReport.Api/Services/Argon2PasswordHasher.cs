using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace DaisyReport.Api.Services;

public class Argon2PasswordHasher : IPasswordHasher
{
    private readonly int _memorySize;
    private readonly int _iterations;
    private readonly int _parallelism;
    private readonly int _hashLength;
    private readonly int _saltLength;

    public Argon2PasswordHasher(IConfiguration configuration)
    {
        var section = configuration.GetSection("Argon2");
        _memorySize = section.GetValue("MemorySize", 65536);
        _iterations = section.GetValue("Iterations", 3);
        _parallelism = section.GetValue("Parallelism", 4);
        _hashLength = section.GetValue("HashLength", 32);
        _saltLength = section.GetValue("SaltLength", 16);
    }

    public string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(_saltLength);

        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password));
        argon2.Salt = salt;
        argon2.MemorySize = _memorySize;
        argon2.Iterations = _iterations;
        argon2.DegreeOfParallelism = _parallelism;

        var hash = argon2.GetBytes(_hashLength);

        var saltB64 = Convert.ToBase64String(salt);
        var hashB64 = Convert.ToBase64String(hash);

        return $"$argon2id$v=19$m={_memorySize},t={_iterations},p={_parallelism}${saltB64}${hashB64}";
    }

    public bool VerifyPassword(string password, string hash)
    {
        try
        {
            var parts = hash.Split('$');
            // Expected format: $argon2id$v=19$m=X,t=X,p=X$<salt>$<hash>
            // parts[0] = "" (before first $)
            // parts[1] = "argon2id"
            // parts[2] = "v=19"
            // parts[3] = "m=65536,t=3,p=4"
            // parts[4] = base64(salt)
            // parts[5] = base64(hash)
            if (parts.Length != 6) return false;

            var paramParts = parts[3].Split(',');
            var memory = int.Parse(paramParts[0].Split('=')[1]);
            var iterations = int.Parse(paramParts[1].Split('=')[1]);
            var parallelism = int.Parse(paramParts[2].Split('=')[1]);

            var salt = Convert.FromBase64String(parts[4]);
            var expectedHash = Convert.FromBase64String(parts[5]);

            using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password));
            argon2.Salt = salt;
            argon2.MemorySize = memory;
            argon2.Iterations = iterations;
            argon2.DegreeOfParallelism = parallelism;

            var computedHash = argon2.GetBytes(expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(computedHash, expectedHash);
        }
        catch
        {
            return false;
        }
    }
}
