using Api.Blacklist;
using Api.Data;
using Api.Domain;
using Api.Scanning;
using Microsoft.EntityFrameworkCore;

namespace Api.Findings;

public sealed class FindingService(AppDbContext db, BlacklistStore blacklist)
{
    public static string OutcomeOf(ScanResult r, int matchCount) => r.Status switch
    {
        ScanStatus.Completed => matchCount > 0 ? CheckOutcome.Found : CheckOutcome.Ok,
        ScanStatus.Timeout => CheckOutcome.Timeout,
        _ => CheckOutcome.Unreachable,
    };

    /// <summary>
    /// Stores one check of one merchant within a run and upserts its findings.
    /// Finding = (merchant, normalized URL): an existing one only gets last_seen_at/last_check_id,
    /// resolved and note are never touched by a check. Failed checks never delete findings.
    /// </summary>
    public async Task<Check> RecordCheckAsync(Guid runId, string merchantId, DateTimeOffset startedAt, ScanResult result, CancellationToken ct)
    {
        var matches = new List<(ScannedUrl Url, string Source)>();
        foreach (var u in result.Urls)
            if (blacklist.TryMatch(u.Url, out var source)) matches.Add((u, source));

        var now = DateTimeOffset.UtcNow;
        var check = new Check
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            MerchantId = merchantId,
            Outcome = OutcomeOf(result, matches.Count),
            Error = result.Error,
            DurationMs = result.DurationMs,
            StartedAt = startedAt,
            UrlsSeen = result.Urls.Count,
            MatchedUrls = matches.Select(m => m.Url.Url).Distinct().ToList(),
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Checks.Add(check);
        await db.SaveChangesAsync(ct);

        foreach (var (u, source) in matches)
        {
            var id = FindingId.Compute(merchantId, u.Url);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO findings (id, merchant_id, url, kind, blacklist_source, first_seen_at, last_seen_at,
                                      first_check_id, last_check_id, resolved, resolved_at, note)
                VALUES ({id}, {merchantId}, {u.Url}, {u.Kind}, {source}, {now}, {now}, {check.Id}, {check.Id}, false, NULL, NULL)
                ON CONFLICT (merchant_id, url) DO UPDATE
                SET last_seen_at = EXCLUDED.last_seen_at, last_check_id = EXCLUDED.last_check_id
                """, ct);
        }

        await db.RunItems
            .Where(i => i.RunId == runId && i.MerchantId == merchantId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.State, RunItemState.Done).SetProperty(i => i.CheckId, check.Id), ct);

        await tx.CommitAsync(ct);
        return check;
    }

    /// <returns>false when the finding does not exist.</returns>
    public async Task<bool> ResolveAsync(string id, string note, CancellationToken ct)
    {
        var f = await db.Findings.FindAsync([id], ct);
        if (f is null) return false;
        if (!f.Resolved)
        {
            f.Resolved = true;
            f.ResolvedAt = DateTimeOffset.UtcNow;
        }
        f.Note = note; // repeated resolve only overwrites the note
        await db.SaveChangesAsync(ct);
        return true;
    }
}
