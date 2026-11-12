namespace Api.Domain;

/// <summary>
/// The single shared URL normalization used for requests, links, forms and blacklist entries.
/// Rules: resolve relative against the page, http/https only, lowercase scheme and host,
/// drop default port, fragment and user info, empty path becomes "/", keep the query string.
/// </summary>
public static class UrlNormalizer
{
    /// <returns>Normalized absolute URL, or null when the input is not a usable http(s) URL.</returns>
    public static string? Normalize(string? raw, Uri? baseUri = null)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = raw.Trim();

        Uri? uri;
        if (baseUri is not null)
        {
            if (!Uri.TryCreate(baseUri, raw, out uri)) return null;
        }
        else if (!Uri.TryCreate(raw, UriKind.Absolute, out uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        if (string.IsNullOrEmpty(uri.Host)) return null;

        var host = uri.HostNameType == UriHostNameType.Dns ? uri.IdnHost : uri.Host;
        var port = uri.IsDefaultPort ? "" : ":" + uri.Port;
        var path = string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath;

        return $"{uri.Scheme}://{host.ToLowerInvariant()}{port}{path}{uri.Query}";
    }

    public static string? Normalize(string? raw, string baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var b) ? Normalize(raw, b) : Normalize(raw);
}
