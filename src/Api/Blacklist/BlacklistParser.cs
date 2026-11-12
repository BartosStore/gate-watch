using Api.Domain;

namespace Api.Blacklist;

public static class BlacklistParser
{
    /// <summary>Plain list: one URL per line, '#' starts a comment line. Returns normalized, de-duplicated URLs.</summary>
    public static List<string> ParseText(string content) =>
        Normalize(content.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#')));

    /// <summary>
    /// URLhaus CSV export: comment lines start with '#', every field is quoted,
    /// columns: id, dateadded, url, url_status, last_online, threat, tags, urlhaus_link, reporter.
    /// </summary>
    public static List<string> ParseUrlhausCsv(string content) =>
        Normalize(content.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split("\",\""))
            .Where(f => f.Length >= 3)
            .Select(f => f[2]));

    private static List<string> Normalize(IEnumerable<string> raw) =>
        raw.Select(u => UrlNormalizer.Normalize(u))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
