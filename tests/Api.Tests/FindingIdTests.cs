using Api.Domain;

namespace Api.Tests;

public class FindingIdTests
{
    [Fact]
    public void Is_32_lowercase_hex_chars_and_stable()
    {
        var id = FindingId.Compute("shop-1", "https://evil.example/x.js");
        Assert.Matches("^[0-9a-f]{32}$", id);
        Assert.Equal(id, FindingId.Compute("shop-1", "https://evil.example/x.js"));
    }

    [Fact]
    public void Matches_spec_formula()
    {
        // sha256("shop-1\nhttps://evil.example/x.js"), first 32 hex chars
        var full = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("shop-1\nhttps://evil.example/x.js")));
        Assert.Equal(full[..32], FindingId.Compute("shop-1", "https://evil.example/x.js"));
    }

    [Fact]
    public void Differs_per_merchant_and_per_url()
    {
        var a = FindingId.Compute("shop-1", "https://evil.example/x.js");
        Assert.NotEqual(a, FindingId.Compute("shop-2", "https://evil.example/x.js"));
        Assert.NotEqual(a, FindingId.Compute("shop-1", "https://evil.example/y.js"));
    }
}
