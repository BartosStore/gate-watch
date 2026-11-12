using Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Api.Blacklist;

/// <summary>
/// On startup imports URLhaus (or the stored snapshot) and custom entries into the DB,
/// then loads the whole table into memory and marks the blacklist as ready.
/// </summary>
public sealed class BlacklistLoader(
    IServiceScopeFactory scopes,
    BlacklistStore blacklist,
    IConfiguration config,
    IHostEnvironment env,
    ILogger<BlacklistLoader> log) : BackgroundService
{
    public const string SourceUrlhaus = "urlhaus";
    public const string SourceSnapshot = "urlhaus-snapshot";
    public const string SourceCustom = "custom";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ImportAndLoadAsync(ct);
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log.LogError(ex, "Blacklist import failed, retrying in 30 s");
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
        }
    }

    private async Task ImportAndLoadAsync(CancellationToken ct)
    {
        var dataDir = Path.GetFullPath(config["DataDir"] ?? Path.Combine(env.ContentRootPath, "data"));
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notes = new List<string>();

        // 1) URLhaus: live export if an Auth-Key is configured, otherwise keep previous import or use the snapshot.
        var live = await TryDownloadUrlhausAsync(ct);
        if (live is not null)
        {
            await ReplaceSourceAsync(db, SourceUrlhaus, live, ct);
            await ReplaceSourceAsync(db, SourceSnapshot, [], ct);
            notes.Add($"urlhaus: staženo {live.Count}");
        }
        else if (await db.BlacklistEntries.AnyAsync(e => e.Source == SourceUrlhaus, ct))
        {
            notes.Add("urlhaus: stažení nedostupné, ponechán předchozí import");
        }
        else
        {
            var snapshot = BlacklistParser.ParseText(await ReadFileAsync(Path.Combine(dataDir, "urlhaus-snapshot.txt"), ct));
            await ReplaceSourceAsync(db, SourceSnapshot, snapshot, ct);
            notes.Add($"urlhaus: stažení nedostupné, použit snapshot ({snapshot.Count})");
        }

        // 2) Custom test entries (always re-read, so edits of the file apply on restart).
        var custom = BlacklistParser.ParseText(await ReadFileAsync(Path.Combine(dataDir, "blacklist-custom.txt"), ct));
        await ReplaceSourceAsync(db, SourceCustom, custom, ct);
        notes.Add($"custom: {custom.Count}");

        // 3) Load the DB table into memory (DB is the source of truth).
        var all = await db.BlacklistEntries.AsNoTracking()
            .Select(e => new KeyValuePair<string, string>(e.Url, e.Source))
            .ToListAsync(ct);

        var meta = await db.BlacklistMeta.FindAsync([1], ct);
        if (meta is null) db.BlacklistMeta.Add(meta = new BlacklistMeta());
        meta.LastImportAt = DateTimeOffset.UtcNow;
        meta.EntryCount = all.Count;
        meta.SourceNote = string.Join("; ", notes);
        await db.SaveChangesAsync(ct);

        blacklist.Load(all);
        log.LogInformation("Blacklist ready: {Count} entries ({Note})", all.Count, meta.SourceNote);
    }

    private async Task<List<string>?> TryDownloadUrlhausAsync(CancellationToken ct)
    {
        var key = config["URLHAUS_AUTH_KEY"];
        if (string.IsNullOrWhiteSpace(key))
        {
            log.LogWarning("URLHAUS_AUTH_KEY is not set; URLhaus export requires an Auth-Key, skipping download");
            return null;
        }
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var csv = await http.GetStringAsync($"https://urlhaus-api.abuse.ch/v2/files/exports/{Uri.EscapeDataString(key)}/recent.csv", ct);
            var urls = BlacklistParser.ParseUrlhausCsv(csv);
            if (urls.Count == 0) throw new InvalidDataException("URLhaus export contained no URLs");
            return urls;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("URLhaus download failed: {Message}", ex.Message);
            return null;
        }
    }

    private async Task<string> ReadFileAsync(string path, CancellationToken ct)
    {
        if (File.Exists(path)) return await File.ReadAllTextAsync(path, ct);
        log.LogWarning("Blacklist file {Path} not found", path);
        return "";
    }

    /// <summary>Makes the rows of one source equal to <paramref name="urls"/>; unchanged rows keep their added_at.</summary>
    private static async Task ReplaceSourceAsync(AppDbContext db, string source, List<string> urls, CancellationToken ct)
    {
        var arr = urls.ToArray();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM blacklist_entries WHERE source = {source} AND NOT (url = ANY({arr}))", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO blacklist_entries (url, source, added_at) SELECT u, {source}, now() FROM unnest({arr}) AS u ON CONFLICT (url) DO NOTHING", ct);
        await tx.CommitAsync(ct);
    }
}
