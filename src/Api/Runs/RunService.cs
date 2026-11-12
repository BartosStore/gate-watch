using System.Threading.Channels;
using Api.Data;
using Api.Findings;
using Api.Scanning;
using Microsoft.EntityFrameworkCore;

namespace Api.Runs;

public sealed record MerchantInput(string Id, string Url);

public sealed record AlertDto(string Id, string Url, bool Resolved, string Note);

public sealed record MerchantResultDto(string MerchantId, string Status, List<string> Matches, List<AlertDto> Alerts, string? Error);

public sealed record RunDto(string RunId, string Status, List<MerchantResultDto> Results, string? Error);

/// <summary>Queue of run ids waiting for the worker.</summary>
public sealed class RunQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();
    public ValueTask EnqueueAsync(Guid runId) => _channel.Writer.WriteAsync(runId);
    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

/// <summary>Global limit of concurrently scanned pages (SCAN_CONCURRENCY, default 8).</summary>
public sealed class ScanLimiter(int concurrency)
{
    public SemaphoreSlim Semaphore { get; } = new(concurrency, concurrency);
    public int Concurrency { get; } = concurrency;
}

public sealed class RunService(
    AppDbContext db,
    IServiceScopeFactory scopes,
    IPageScanner scanner,
    ScanLimiter limiter,
    ILogger<RunService> log)
{
    public const string InterruptedError = "Přerušeno restartem aplikace";

    public async Task<Guid> CreateRunAsync(IReadOnlyList<MerchantInput> merchants, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var run = new Run { Id = Guid.NewGuid(), Status = RunStatus.Running, CreatedAt = now };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO merchants (id, url, created_at)
            SELECT id, url, {now} FROM unnest({merchants.Select(m => m.Id).ToArray()}, {merchants.Select(m => m.Url).ToArray()}) AS m(id, url)
            ON CONFLICT (id) DO UPDATE SET url = EXCLUDED.url WHERE merchants.url <> EXCLUDED.url
            """, ct);
        db.Runs.Add(run);
        await db.SaveChangesAsync(ct);
        // One statement for all run items: 20 000 merchants must still answer 202 within 5 s.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO run_items (run_id, merchant_id, state)
            SELECT {run.Id}, id, {RunItemState.Pending} FROM unnest({merchants.Select(m => m.Id).ToArray()}) AS m(id)
            """, ct);
        await tx.CommitAsync(ct);
        return run.Id;
    }

    /// <summary>Runs that were "running" when the previous process stopped are closed as failed (not re-run).</summary>
    public Task<int> FailInterruptedRunsAsync(CancellationToken ct) =>
        db.Runs.Where(r => r.Status == RunStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, RunStatus.Failed)
                .SetProperty(r => r.Error, InterruptedError)
                .SetProperty(r => r.FinishedAt, DateTimeOffset.UtcNow), ct);

    public async Task ProcessRunAsync(Guid runId, CancellationToken ct)
    {
        try
        {
            var items = await db.RunItems.AsNoTracking()
                .Where(i => i.RunId == runId && i.State == RunItemState.Pending)
                .Join(db.Merchants, i => i.MerchantId, m => m.Id, (i, m) => new MerchantInput(m.Id, m.Url))
                .ToListAsync(ct);

            await Task.WhenAll(items.Select(m => CheckMerchantAsync(runId, m, ct)));

            await db.Runs.Where(r => r.Id == runId).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, RunStatus.Done)
                .SetProperty(r => r.FinishedAt, DateTimeOffset.UtcNow), ct);
            log.LogInformation("Run {RunId} done ({Count} merchants)", runId, items.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown: the run stays "running" and is marked as interrupted on the next start.
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Run {RunId} failed", runId);
            await db.Runs.Where(r => r.Id == runId).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, RunStatus.Failed)
                .SetProperty(r => r.Error, "Interní chyba běhu: " + ex.Message)
                .SetProperty(r => r.FinishedAt, DateTimeOffset.UtcNow), CancellationToken.None);
        }
    }

    private async Task CheckMerchantAsync(Guid runId, MerchantInput m, CancellationToken ct)
    {
        await limiter.Semaphore.WaitAsync(ct);
        try
        {
            var startedAt = DateTimeOffset.UtcNow;
            ScanResult result;
            try
            {
                result = await scanner.ScanAsync(m.Url, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // A failure of one merchant is an error of this merchant only, never of the whole run.
                log.LogWarning(ex, "Scan of {MerchantId} crashed", m.Id);
                result = new ScanResult(ScanStatus.Unreachable, "Interní chyba kontroly: " + ex.Message, [],
                    (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds, null);
            }

            using var scope = scopes.CreateScope();
            var findings = scope.ServiceProvider.GetRequiredService<FindingService>();
            await findings.RecordCheckAsync(runId, m.Id, startedAt, result, ct);
        }
        finally
        {
            limiter.Semaphore.Release();
        }
    }

    /// <summary>GET /runs/{id}: always read from the DB. Only finished merchants are returned.</summary>
    public async Task<RunDto?> GetRunAsync(Guid runId, CancellationToken ct)
    {
        var run = await db.Runs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null) return null;

        var checks = await db.RunItems.AsNoTracking()
            .Where(i => i.RunId == runId && i.State == RunItemState.Done)
            .Join(db.Checks, i => i.CheckId, c => c.Id, (i, c) => c)
            .OrderBy(c => c.MerchantId)
            .ToListAsync(ct);

        var merchantIds = checks.Where(c => c.MatchedUrls.Count > 0).Select(c => c.MerchantId).ToList();
        var findings = (await db.Findings.AsNoTracking()
                .Where(f => merchantIds.Contains(f.MerchantId))
                .ToListAsync(ct))
            .ToDictionary(f => (f.MerchantId, f.Url));

        var results = checks.Select(c =>
        {
            var ok = c.Outcome is CheckOutcome.Ok or CheckOutcome.Found;
            var alerts = c.MatchedUrls
                .Select(u => findings.TryGetValue((c.MerchantId, u), out var f) ? new AlertDto(f.Id, f.Url, f.Resolved, f.Note ?? "") : null)
                .OfType<AlertDto>()
                .ToList();
            return new MerchantResultDto(c.MerchantId, ok ? "ok" : "error", c.MatchedUrls, alerts,
                ok ? null : c.Error ?? c.Outcome);
        }).ToList();

        return new RunDto(run.Id.ToString(), run.Status, results, run.Status == RunStatus.Failed ? run.Error : null);
    }
}

public sealed class RunWorker(RunQueue queue, IServiceScopeFactory scopes, ILogger<RunWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var running = new List<Task>();
        await foreach (var runId in queue.ReadAllAsync(ct))
        {
            running.RemoveAll(t => t.IsCompleted);
            running.Add(Task.Run(async () =>
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<RunService>().ProcessRunAsync(runId, ct);
            }, CancellationToken.None));
            log.LogInformation("Run {RunId} started", runId);
        }
        await Task.WhenAll(running);
    }
}
