using Api.Blacklist;

namespace Api.Scanning;

/// <summary>
/// Developer command: `dotnet Api.dll scan &lt;url&gt; [&lt;url&gt;...]`.
/// Scans pages without DB or web server; the blacklist is read straight from the data files.
/// </summary>
public static class ScanCli
{
    public static async Task<int> RunAsync(string[] urls, string dataDir)
    {
        if (urls.Length == 0)
        {
            Console.Error.WriteLine("Usage: scan <url> [<url>...]");
            return 2;
        }

        var blacklist = new BlacklistStore();
        var entries = new Dictionary<string, string>();
        foreach (var (file, source) in new[] { ("urlhaus-snapshot.txt", BlacklistLoader.SourceSnapshot), ("blacklist-custom.txt", BlacklistLoader.SourceCustom) })
        {
            var path = Path.Combine(dataDir, file);
            if (!File.Exists(path)) continue;
            foreach (var u in BlacklistParser.ParseText(await File.ReadAllTextAsync(path))) entries.TryAdd(u, source);
        }
        blacklist.Load(entries);
        Console.WriteLine($"Blacklist: {blacklist.Count} entries from {dataDir}");

        await using var host = new BrowserHost();
        var scanner = new PageScanner(host, blacklist, new ScannerOptions());
        foreach (var url in urls)
        {
            var r = await scanner.ScanAsync(url, CancellationToken.None);
            Console.WriteLine();
            Console.WriteLine($"== {url}");
            Console.WriteLine($"   status={r.Status} duration={r.DurationMs}ms urls={r.Urls.Count} final={r.FinalUrl} error={r.Error}");
            foreach (var u in r.Urls.OrderBy(u => u.Kind).ThenBy(u => u.Url))
            {
                var match = blacklist.TryMatch(u.Url, out var src) ? $"  <-- BLACKLIST ({src})" : "";
                Console.WriteLine($"   {u.Kind,-10} {(u.Blocked ? "BLOCKED " : "        ")}{u.Url}{match}");
            }
        }
        return 0;
    }
}
