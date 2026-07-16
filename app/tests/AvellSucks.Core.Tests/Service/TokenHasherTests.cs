using AvellSucks.Core.Service;
using Xunit;

namespace AvellSucks.Core.Tests.Service;

public class TokenHasherTests
{
    [Fact]
    public void HashHex_is_stable_lowercase_sha256()
    {
        // Known SHA-256 of "hello" (lowercase hex).
        Assert.Equal(
            "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
            TokenHasher.HashHex("hello"));
    }

    [Fact]
    public void FixedTimeEqualsHex_matches_same_hash_regardless_of_case()
    {
        var h = TokenHasher.HashHex("s3cret-token");
        Assert.True(TokenHasher.FixedTimeEqualsHex(h, h));
        Assert.True(TokenHasher.FixedTimeEqualsHex(h.ToUpperInvariant(), h));
    }

    [Fact]
    public void FixedTimeEqualsHex_false_on_mismatch_or_null()
    {
        var a = TokenHasher.HashHex("a");
        var b = TokenHasher.HashHex("b");
        Assert.False(TokenHasher.FixedTimeEqualsHex(a, b));
        Assert.False(TokenHasher.FixedTimeEqualsHex(null, a));
        Assert.False(TokenHasher.FixedTimeEqualsHex(a, null));
        Assert.False(TokenHasher.FixedTimeEqualsHex("tooShort", a));
    }
}
