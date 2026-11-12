using System.Security.Cryptography;
using System.Text;

namespace Api.Domain;

public static class FindingId
{
    /// <summary>First 32 hex chars of SHA-256(merchant_id + "\n" + normalized_url). Stable across checks.</summary>
    public static string Compute(string merchantId, string normalizedUrl)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(merchantId + "\n" + normalizedUrl));
        return Convert.ToHexStringLower(hash)[..32];
    }
}
