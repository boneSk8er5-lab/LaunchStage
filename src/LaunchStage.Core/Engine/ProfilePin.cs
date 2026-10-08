using System.Security.Cryptography;
using System.Text;
using LaunchStage.Core.Models;

namespace LaunchStage.Core.Engine;

/// <summary>
/// The 4-digit PIN on a private profile. Only a salted one-way hash is stored, never the PIN itself.
/// This keeps other people using LaunchStage out of the profile; it isn't encryption of the profile file.
/// </summary>
public static class ProfilePin
{
    private const int Iterations = 200_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static bool IsValidPin(string pin) => pin.Length == 4 && pin.All(char.IsAsciiDigit);

    public static void Set(Profile profile, string pin)
    {
        if (!IsValidPin(pin))
        {
            throw new ArgumentException("The PIN must be exactly 4 digits.", nameof(pin));
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        profile.PinSalt = Convert.ToBase64String(salt);
        profile.PinHash = Convert.ToBase64String(Hash(pin, salt));
    }

    public static void Clear(Profile profile)
    {
        profile.PinHash = null;
        profile.PinSalt = null;
    }

    public static bool Verify(Profile profile, string pin)
    {
        if (!profile.IsPrivate)
        {
            return true;
        }

        if (!IsValidPin(pin) || string.IsNullOrEmpty(profile.PinSalt))
        {
            return false;
        }

        try
        {
            byte[] expected = Convert.FromBase64String(profile.PinHash!);
            byte[] actual = Hash(pin, Convert.FromBase64String(profile.PinSalt));
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false; // the file was edited by hand
        }
    }

    private static byte[] Hash(string pin, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
}
