using System.Net;
using System.Net.Sockets;

namespace Api.Scanning;

/// <summary>Decides which request URLs the browser must never fetch (besides blacklisted ones).</summary>
public static class UrlGuard
{
    /// <summary>True for non-http(s) schemes, localhost names and private/loopback/link-local IP literals.</summary>
    public static bool IsForbidden(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return true;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return true;

        var host = uri.Host.Trim('[', ']').TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0 || host == "localhost" || host.EndsWith(".localhost")) return true;

        return IPAddress.TryParse(host, out var ip) && IsPrivate(ip);
    }

    public static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 0                                   // 0.0.0.0/8
                || b[0] == 127                                 // loopback
                || b[0] == 10                                  // 10/8
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)   // 172.16/12
                || (b[0] == 192 && b[1] == 168)                // 192.168/16
                || (b[0] == 169 && b[1] == 254);               // link-local
        }

        return IPAddress.IsLoopback(ip)
            || ip.Equals(IPAddress.IPv6Any)
            || ip.IsIPv6LinkLocal
            || ip.IsIPv6SiteLocal
            || ip.IsIPv6UniqueLocal;                           // fc00::/7
    }
}
