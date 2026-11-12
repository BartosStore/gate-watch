using Api;
using Api.Blacklist;
using Api.Data;
using Api.Findings;
using Api.Merchants;
using Api.Runs;
using Api.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ListQueryTests : IAsyncLifetime
{
    private const string Evil = "https://evil.example/list.js";
    private readonly ServiceProvider _sp;
    private readonly FakeScanner _scanner = new();
    private readonly string _p = "L" + Guid.NewGuid().ToString("N")[..8] + "-";

    public ListQueryTests(PostgresFixture db)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddGateWatchCore(db.ConnectionString, 4);
        services.AddSingleton<IPageScanner>(_scanner);
        _sp = services.BuildServiceProvider();
        _sp.GetRequiredService<BlacklistStore>().Load([new(Evil, "custom")]);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _sp.DisposeAsync();

    private T Get<T>(IServiceScope s) where T : notnull => s.ServiceProvider.GetRequiredService<T>();

    /// <summary>Creates: open (unresolved finding), resolved (finding resolved), timeout, down, ok, never (no check).</summary>
    private async Task SetupAsync()
    {
        _scanner.Handler = url => url switch
        {
            _ when url.Contains("open") || url.Contains("resolved") => FakeScanner.Ok(Evil),
            _ when url.Contains("timeout") => new ScanResult(ScanStatus.Timeout, "timeout", [], 10000, null),
            _ when url.Contains("down") => new ScanResult(ScanStatus.Unreachable, "HTTP 500", [], 10, null),
            _ => FakeScanner.Ok(),
        };
        using var s = _sp.CreateScope();
        var runs = Get<RunService>(s);
        var names = new[] { "ok", "never", "down", "resolved", "timeout", "open" };
        var id = await runs.CreateRunAsync(names.Select(n => new MerchantInput(_p + n, $"https://{n}.example/")).ToList(), default);
        // "never" must stay unchecked: drop its run item before processing.
        await Get<AppDbContext>(s).Database.ExecuteSqlInterpolatedAsync($"DELETE FROM run_items WHERE merchant_id = {_p + "never"}");
        await runs.ProcessRunAsync(id, default);
        await Get<FindingService>(s).ResolveAsync(Domain.FindingId.Compute(_p + "resolved", Evil), "done", default);
    }

    [PostgresFact]
    public async Task Merchants_are_ordered_open_alerts_then_errors_then_ok_then_unchecked()
    {
        await SetupAsync();
        using var s = _sp.CreateScope();
        var page = await Get<MerchantQueries>(s).MerchantsAsync(_p, null, 1, 100, default);

        Assert.Equal(6, page.Total);
        Assert.Equal(["open", "down", "timeout", "ok", "resolved", "never"], page.Items.Select(i => i.Id[_p.Length..]));
        var open = page.Items[0];
        Assert.Equal(1, open.OpenAlerts);
        Assert.Equal("found", open.LastOutcome);
        Assert.NotNull(open.LastCheckAt);
        Assert.Equal("HTTP 500", page.Items[1].LastError);
        Assert.Null(page.Items[5].LastOutcome);
        Assert.Null(page.Items[5].LastCheckAt);
    }

    [PostgresFact]
    public async Task Merchants_filter_by_status_and_search_and_page()
    {
        await SetupAsync();
        using var s = _sp.CreateScope();
        var q = Get<MerchantQueries>(s);

        Assert.Equal([_p + "timeout"], (await q.MerchantsAsync(_p, "timeout", 1, 10, default)).Items.Select(i => i.Id));
        Assert.Equal([_p + "never"], (await q.MerchantsAsync(_p, "unchecked", 1, 10, default)).Items.Select(i => i.Id));
        Assert.Equal(2, (await q.MerchantsAsync(_p, "found", 1, 10, default)).Total);
        Assert.Equal([_p + "down"], (await q.MerchantsAsync("down.example", null, 1, 10, default)).Items.Select(i => i.Id)
            .Where(i => i.StartsWith(_p)));

        var p2 = await q.MerchantsAsync(_p, null, 2, 4, default);
        Assert.Equal(6, p2.Total);
        Assert.Equal(2, p2.Items.Count);
        Assert.Equal(2, p2.Page);
    }

    [PostgresFact]
    public async Task Search_treats_wildcards_literally()
    {
        await SetupAsync();
        using var s = _sp.CreateScope();
        Assert.Equal(0, (await Get<MerchantQueries>(s).MerchantsAsync(_p + "%", null, 1, 10, default)).Total);
    }

    [PostgresFact]
    public async Task Alerts_filter_by_resolved_and_merchant()
    {
        await SetupAsync();
        using var s = _sp.CreateScope();
        var q = Get<MerchantQueries>(s);

        var open = await q.AlertsAsync(false, _p + "open", 1, 10, default);
        var a = Assert.Single(open.Items);
        Assert.Equal(Evil, a.Url);
        Assert.Equal("https://open.example/", a.MerchantUrl);
        Assert.Equal("script", a.Kind);
        Assert.Equal("custom", a.BlacklistSource);
        Assert.False(a.Resolved);
        Assert.Equal("", a.Note);

        var resolved = await q.AlertsAsync(true, _p + "resolved", 1, 10, default);
        var r = Assert.Single(resolved.Items);
        Assert.True(r.Resolved);
        Assert.NotNull(r.ResolvedAt);
        Assert.Equal("done", r.Note);

        Assert.Empty((await q.AlertsAsync(true, _p + "open", 1, 10, default)).Items);
        Assert.Equal(1, (await q.AlertsAsync(null, _p + "resolved", 1, 10, default)).Total);
    }

    [PostgresFact]
    public async Task Seed_inserts_only_into_empty_table()
    {
        using var s = _sp.CreateScope();
        var path = Path.Combine(Path.GetTempPath(), $"seed-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, """[{ "id": "x", "url": "https://x.example/" }]""");
        // The shared test DB already contains merchants from other tests, so nothing is inserted.
        await SetupAsync();
        Assert.Equal(0, await Get<MerchantQueries>(s).SeedIfEmptyAsync(path, default));
    }
}
