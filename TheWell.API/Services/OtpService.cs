using System.Security.Cryptography;
using System.Text;
using TheWell.Core.Interfaces;

namespace TheWell.API.Services;

public class OtpService : IOtpService
{
    public string Generate()
    {
        // Cryptographically secure 6-digit OTP (Random.Shared is not crypto-safe)
        var value = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000);
        return value.ToString();
    }

    public string Hash(string otp)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(otp));
        return Convert.ToHexString(bytes);
    }

    public bool Verify(string otp, string storedHash, DateTime expiresAt)
    {
        if (DateTime.UtcNow > expiresAt) return false;
        var computed = Hash(otp);
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(computed),
            System.Text.Encoding.UTF8.GetBytes(storedHash));
    }
}
