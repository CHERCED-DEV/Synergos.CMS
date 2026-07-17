using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="TwoFactorRecoveryCodes"/> (Ola 207).
/// Pure crypto helper sin deps Umbraco — testeable directamente.
/// </summary>
public sealed class TwoFactorRecoveryCodesTests
{
    [Fact]
    public void GenerateBatch_Produces8CodesAnd8Hashes()
    {
        var (hashes, plaintext) = TwoFactorRecoveryCodes.GenerateBatch();

        Assert.Equal(TwoFactorRecoveryCodes.CodeCount, hashes.Count);
        Assert.Equal(TwoFactorRecoveryCodes.CodeCount, plaintext.Count);
    }

    [Fact]
    public void GenerateBatch_PlaintextCodesAreEightChars()
    {
        var (_, plaintext) = TwoFactorRecoveryCodes.GenerateBatch();

        Assert.All(plaintext, code => Assert.Equal(TwoFactorRecoveryCodes.CodeChars, code.Length));
    }

    [Fact]
    public void GenerateBatch_PlaintextCodesUseSafeAlphabet()
    {
        var (_, plaintext) = TwoFactorRecoveryCodes.GenerateBatch();
        var allowed = TwoFactorRecoveryCodes.Alphabet.ToHashSet();

        foreach (var code in plaintext)
        {
            foreach (var c in code)
            {
                Assert.Contains(c, allowed);
            }
        }
    }

    [Fact]
    public void GenerateBatch_PlaintextCodesAreUnique()
    {
        var (_, plaintext) = TwoFactorRecoveryCodes.GenerateBatch();

        Assert.Equal(plaintext.Count, plaintext.Distinct().Count());
    }

    [Fact]
    public void GenerateBatch_HashesArePairedWithPlaintext()
    {
        var (hashes, plaintext) = TwoFactorRecoveryCodes.GenerateBatch();

        for (var i = 0; i < hashes.Count; i++)
        {
            Assert.True(TwoFactorRecoveryCodes.Verify(plaintext[i], hashes[i]),
                $"plaintext[{i}] should verify against hashes[{i}]");
        }
    }

    [Fact]
    public void GenerateBatch_HashesAreCrosswiseUnique()
    {
        // Diferentes salts → mismas plaintext darían hashes distintos.
        var (hashes, _) = TwoFactorRecoveryCodes.GenerateBatch();

        Assert.Equal(hashes.Count, hashes.Distinct().Count());
    }

    [Fact]
    public void Verify_WrongCode_ReturnsFalse()
    {
        var hash = TwoFactorRecoveryCodes.Hash("CORRECT1");

        Assert.False(TwoFactorRecoveryCodes.Verify("WRONGCD1", hash));
    }

    [Fact]
    public void Verify_CorrectCode_ReturnsTrue()
    {
        var hash = TwoFactorRecoveryCodes.Hash("MATCHME8");

        Assert.True(TwoFactorRecoveryCodes.Verify("MATCHME8", hash));
    }

    [Fact]
    public void Verify_EmptyCandidate_ReturnsFalse()
    {
        var hash = TwoFactorRecoveryCodes.Hash("ANYCODE1");

        Assert.False(TwoFactorRecoveryCodes.Verify("", hash));
        Assert.False(TwoFactorRecoveryCodes.Verify(null!, hash));
    }

    [Fact]
    public void Verify_MalformedHash_ReturnsFalse()
    {
        Assert.False(TwoFactorRecoveryCodes.Verify("ANYCODE1", "no-colon-here"));
        Assert.False(TwoFactorRecoveryCodes.Verify("ANYCODE1", ":only-hash-no-salt"));
        Assert.False(TwoFactorRecoveryCodes.Verify("ANYCODE1", "not-base64:also-not-base64"));
        Assert.False(TwoFactorRecoveryCodes.Verify("ANYCODE1", ""));
    }

    [Fact]
    public void Hash_SameCodeProducesDifferentHashes_BecauseRandomSalt()
    {
        var hash1 = TwoFactorRecoveryCodes.Hash("SAMECODE");
        var hash2 = TwoFactorRecoveryCodes.Hash("SAMECODE");

        Assert.NotEqual(hash1, hash2);
        Assert.True(TwoFactorRecoveryCodes.Verify("SAMECODE", hash1));
        Assert.True(TwoFactorRecoveryCodes.Verify("SAMECODE", hash2));
    }

    [Fact]
    public void Verify_CaseSensitive()
    {
        var hash = TwoFactorRecoveryCodes.Hash("UPPERCAS");

        Assert.False(TwoFactorRecoveryCodes.Verify("uppercas", hash));
    }
}
