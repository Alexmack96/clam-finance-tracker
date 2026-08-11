using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Generators;

namespace Clam.Api.Infrastructure.Auth;

/// Produces password hashes in Better Auth's stored format, so a user this
/// service creates can sign in through the Express auth routes.
///
/// Better Auth owns login; this service only ever *writes* a credential. That
/// asymmetry is the reason the constants below are hardcoded rather than
/// configurable: they are not a policy this service gets to choose, they are the
/// format the other service will read back. Changing one silently locks out
/// every account created afterwards.
///
/// Format: <c>hex(salt) + ":" + hex(key)</c>, where the scrypt salt is the ASCII
/// bytes of the hex string — not the 16 raw bytes it encodes. That looks like a
/// bug and is not: Better Auth passes the hex string straight to scrypt.
public static class BetterAuthPasswordHasher
{
    private const int CostN = 16384;
    private const int BlockSizeR = 16;
    private const int Parallelism = 1;
    private const int KeyLength = 64;
    private const int SaltBytes = 16;

    public static string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var saltHex = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SaltBytes));

        // NFKC first: two byte sequences that render identically must hash
        // identically, or a password typed on a different keyboard fails to
        // match the one that was registered.
        var key = SCrypt.Generate(
            Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormKC)),
            Encoding.UTF8.GetBytes(saltHex),
            CostN,
            BlockSizeR,
            Parallelism,
            KeyLength);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{saltHex}:{Convert.ToHexStringLower(key)}");
    }
}
