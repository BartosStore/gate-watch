using System.Runtime.CompilerServices;
using System.Text.Json;
using Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Api.Merchants;

public sealed record PagedList<T>(List<T> Items, int Total, int Page, int PageSize);

public sealed record AlertListItem(
    string Id, string MerchantId, string MerchantUrl, string Url, string Kind, string BlacklistSource,
    DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt, bool Resolved, DateTimeOffset? ResolvedAt, string Note,
    Guid FirstCheckId, Guid LastCheckId);

public sealed record MerchantListItem(
    string Id, string Url, string? LastOutcome, string? LastError, int? LastDurationMs, DateTimeOffset? LastCheckAt, int OpenAlerts);

/// <summary>Row shape for the raw SQL merchant list (column aliases match property names).</summary>
public sealed class MerchantRow
{
    public string Id { get; set; } = "";
    public string Url { get; set; } = "";
    public string? LastOutcome { get; set; }
    public string? LastError { get; set; }
    public int? LastDurationMs { get; set; }
    public DateTimeOffset? LastCheckAt { get; set; }
    public int OpenAlerts { get; set; }
}

public sealed class MerchantQueries(AppDbContext db)
{
    public static readonly string[] Statuses = [CheckOutcome.Ok, CheckOutcome.Found, CheckOutcome.Timeout, CheckOutcome.Unreachable, "unchecked"];

    /// <summary>Findings for the main screen, newest activity first.</summary>
    public async Task<PagedList<AlertListItem>> AlertsAsync(bool? resolved, string? merchantId, int page, int pageSize, CancellationToken ct)
    {
        var q = db.Findings.AsNoTracking().AsQueryable();
        if (resolved is not null) q = q.Where(f => f.Resolved == resolved);
        if (!string.IsNullOrEmpty(merchantId)) q = q.Where(f => f.MerchantId == merchantId);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(f => f.LastSeenAt).ThenBy(f => f.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Join(db.Merchants, f => f.MerchantId, m => m.Id, (f, m) => new AlertListItem(
                f.Id, f.MerchantId, m.Url, f.Url, f.Kind, f.BlacklistSource, f.FirstSeenAt, f.LastSeenAt,
                f.Resolved, f.ResolvedAt, f.Note ?? "", f.FirstCheckId, f.LastCheckId))
            .ToListAsync(ct);
        return new PagedList<AlertListItem>(items, total, page, pageSize);
    }

    /// <summary>
    /// Merchants with their latest check and number of unresolved findings.
    /// Order: unresolved findings first, then unfinished/unreachable, then OK, then never checked.
    /// </summary>
    public async Task<PagedList<MerchantListItem>> MerchantsAsync(string? search, string? status, int page, int pageSize, CancellationToken ct)
    {
        var s = search?.Trim() ?? "";
        var pattern = "%" + s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        var st = status ?? "";
        var offset = (page - 1) * pageSize;

        // Npgsql needs typed parameters, so "no filter" is an empty string rather than NULL.
        FormattableString Rows(string select, string tail) => FormattableStringFactory.Create($$"""
            WITH last AS (
                SELECT DISTINCT ON (merchant_id) merchant_id, outcome, error, duration_ms, started_at
                FROM checks ORDER BY merchant_id, started_at DESC
            ), open AS (
                SELECT merchant_id, count(*)::int AS n FROM findings WHERE NOT resolved GROUP BY merchant_id
            ), r AS (
                SELECT m.id AS "Id", m.url AS "Url", l.outcome AS "LastOutcome", l.error AS "LastError",
                       l.duration_ms AS "LastDurationMs", l.started_at AS "LastCheckAt", COALESCE(o.n, 0) AS "OpenAlerts",
                       CASE WHEN COALESCE(o.n, 0) > 0 THEN 0
                            WHEN l.outcome IN ('timeout', 'unreachable') THEN 1
                            WHEN l.outcome IS NOT NULL THEN 2
                            ELSE 3 END AS grp
                FROM merchants m
                LEFT JOIN last l ON l.merchant_id = m.id
                LEFT JOIN open o ON o.merchant_id = m.id
            )
            SELECT {{select}} FROM r
            WHERE ({0} = '' OR "Id" ILIKE {1} OR "Url" ILIKE {1})
              AND ({2} = '' OR ({2} = 'unchecked' AND "LastOutcome" IS NULL) OR "LastOutcome" = {2})
            {{tail}}
            """, s, pattern, st, pageSize, offset);

        var total = await db.Database.SqlQuery<int>(Rows("count(*)::int AS \"Value\"", "")).SingleAsync(ct);
        var rows = await db.Database.SqlQuery<MerchantRow>(
                Rows("\"Id\", \"Url\", \"LastOutcome\", \"LastError\", \"LastDurationMs\", \"LastCheckAt\", \"OpenAlerts\"",
                     "ORDER BY grp, \"OpenAlerts\" DESC, \"Id\" LIMIT {3} OFFSET {4}"))
            .ToListAsync(ct);

        var items = rows.Select(r => new MerchantListItem(r.Id, r.Url, r.LastOutcome, r.LastError, r.LastDurationMs, r.LastCheckAt, r.OpenAlerts)).ToList();
        return new PagedList<MerchantListItem>(items, total, page, pageSize);
    }

    /// <summary>Seeds the merchants table from data/merchants-sample.json when it is empty.</summary>
    public async Task<int> SeedIfEmptyAsync(string path, CancellationToken ct)
    {
        if (await db.Merchants.AnyAsync(ct) || !File.Exists(path)) return 0;
        var items = JsonSerializer.Deserialize<List<SampleMerchant>>(await File.ReadAllTextAsync(path, ct),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        var now = DateTimeOffset.UtcNow;
        db.Merchants.AddRange(items
            .Where(i => !string.IsNullOrWhiteSpace(i.Id) && !string.IsNullOrWhiteSpace(i.Url))
            .DistinctBy(i => i.Id)
            .Select(i => new Merchant { Id = i.Id, Url = i.Url, CreatedAt = now }));
        return await db.SaveChangesAsync(ct);
    }

    private sealed record SampleMerchant(string Id, string Url);
}
