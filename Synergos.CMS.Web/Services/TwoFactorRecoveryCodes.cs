using System.Security.Cryptography;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Pure helpers para 2FA recovery codes — generación + hashing
/// PBKDF2 + verification constant-time. Aislado de
/// <see cref="UmbracoMemberTwoFactorService"/> para tests directos.
/// </summary>
/// <remarks>
/// Olas 197 + 207. Phase 2.A pattern:
/// - 8 codes × 8 chars del alphabet "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"
///   (sin 0/O/I/1/L para evitar ambigüedad visual al copiar a mano).
/// - Storage hashed con PBKDF2 SHA-256 + 100k iterations + 16-byte
///   random salt per code. Format <c>"{base64Salt}:{base64Hash}"</c>.
/// - Verification con <see cref="CryptographicOperations.FixedTimeEquals"/>
///   contra timing attacks.
/// </remarks>
public static class TwoFactorRecoveryCodes
{
    public const int CodeCount = 8;
    public const int CodeChars = 8;
    public const int Pbkdf2Iterations = 100_000;
    public const int Pbkdf2HashBytes = 32;
    public const int Pbkdf2SaltBytes = 16;
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>
    /// Genera <see cref="CodeCount"/> códigos plaintext + sus hashes
    /// emparejados. El caller persiste los hashes y muestra los plaintext
    /// al member UNA vez.
    /// </summary>
    public static (IReadOnlyList<string> hashes, IReadOnlyList<string> plaintext) GenerateBatch()
    {
        var plaintext = new List<string>(CodeCount);
        var hashes = new List<string>(CodeCount);
        var charBuf = new char[CodeChars];

        for (var i = 0; i < CodeCount; i++)
        {
            for (var c = 0; c < CodeChars; c++)
            {
                charBuf[c] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            }
            var code = new string(charBuf);
            plaintext.Add(code);
            hashes.Add(Hash(code));
        }

        return (hashes, plaintext);
    }

    /// <summary>
    /// Hashea un código plaintext con PBKDF2 + salt random por code.
    /// Returns formato <c>"{base64Salt}:{base64Hash}"</c>.
    /// </summary>
    public static string Hash(string code)
    {
        var salt = new byte[Pbkdf2SaltBytes];
        RandomNumberGenerator.Fill(salt);
        using var pbkdf2 = new Rfc2898DeriveBytes(code, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256);
        var hash = pbkdf2.GetBytes(Pbkdf2HashBytes);
        return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Verifica un candidate plaintext contra un stored hash. Usa
    /// constant-time compare contra timing attacks. Retorna false si
    /// el hash es malformado.
    /// </summary>
    public static bool Verify(string candidate, string saltColonHash)
    {
        if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(saltColonHash))
        {
            return false;
        }

        var sep = saltColonHash.IndexOf(':');
        if (sep < 0) return false;
        try
        {
            var salt = Convert.FromBase64String(saltColonHash[..sep]);
            var expected = Convert.FromBase64String(saltColonHash[(sep + 1)..]);
            using var pbkdf2 = new Rfc2898DeriveBytes(candidate, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256);
            var actual = pbkdf2.GetBytes(Pbkdf2HashBytes);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
