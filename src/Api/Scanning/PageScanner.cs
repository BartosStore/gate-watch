using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Api.Blacklist;
using Api.Data;
using Api.Domain;
using Microsoft.Playwright;

namespace Api.Scanning;

public enum ScanStatus { Completed, Timeout, Unreachable }

public sealed record ScannedUrl(string Url, string Kind, bool Blocked);

public sealed record ScanResult(ScanStatus Status, string? Error, IReadOnlyList<ScannedUrl> Urls, int DurationMs, string? FinalUrl);

public interface IPageScanner
{
    Task<ScanResult> ScanAsync(string url, CancellationToken ct);
}

public sealed class ScannerOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
    /// <summary>Only for tests against a local web server.</summary>
    public bool AllowPrivateNetworks { get; set; }
}

/// <summary>
/// Opens only the home page in a fresh browser context and records every network request
/// (including redirects and JS-created ones) plus a[href], form[action], iframe[src] from the DOM.
/// Blacklisted and private/local URLs are recorded but aborted before they leave the browser.
/// </summary>
public sealed class PageScanner(BrowserHost browserHost, BlacklistStore blacklist, ScannerOptions options) : IPageScanner
{
    private const string DomScript = """
        () => ({
          links: [...document.querySelectorAll('a[href]')].map(e => e.href),
          forms: [...document.querySelectorAll('form[action]')].map(e => e.action),
          iframes: [...document.querySelectorAll('iframe[src]')].map(e => e.src),
        })
        """;

    public async Task<ScanResult> ScanAsync(string url, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var found = new ConcurrentDictionary<string, ScannedUrl>();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(options.Timeout);

        IBrowserContext? context = null;
        Task<(ScanStatus, string?, string?)>? work = null;
        try
        {
            var browser = await browserHost.GetAsync();
            context = await browser.NewContextAsync(new()
            {
                UserAgent = browserHost.UserAgent,
                Locale = "cs-CZ",
                ViewportSize = new() { Width = 1366, Height = 768 },
                AcceptDownloads = false,
                Permissions = [],
                ServiceWorkers = ServiceWorkerPolicy.Block, // otherwise SW requests would bypass routing
            });

            work = RunAsync(context, url, found, sw, deadline.Token);
            var (status, error, finalUrl) = await work.WaitAsync(deadline.Token);
            return new ScanResult(status, error, found.Values.ToList(), (int)sw.ElapsedMilliseconds, finalUrl);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ScanResult(ScanStatus.Timeout, $"Kontrola nedokončena do {options.Timeout.TotalSeconds:0} s",
                found.Values.ToList(), (int)sw.ElapsedMilliseconds, null);
        }
        finally
        {
            if (context is not null)
            {
                try { await context.CloseAsync(); } catch { /* browser may be gone */ }
            }
            // Closing the context makes a still running `work` fail; observe it so it is not unobserved.
            _ = work?.ContinueWith(t => t.Exception, TaskScheduler.Default);
        }
    }

    private async Task<(ScanStatus, string?, string?)> RunAsync(
        IBrowserContext context, string url, ConcurrentDictionary<string, ScannedUrl> found, Stopwatch sw, CancellationToken ct)
    {
        int Remaining() => Math.Max(1, (int)(options.Timeout.TotalMilliseconds - sw.ElapsedMilliseconds));

        bool IsBlocked(string? normalized, string raw) =>
            normalized is null
            || blacklist.TryMatch(normalized, out _)
            || (!options.AllowPrivateNetworks && UrlGuard.IsForbidden(raw));

        // Every request (also JS-created ones) passes here before it leaves the browser.
        // Chromium does not call route handlers for redirect hops, so each request is fetched with
        // MaxRedirects = 0: a redirect target is checked first and the browser only follows allowed ones
        // (the follow-up request comes through this handler again).
        await context.RouteAsync("**/*", async route =>
        {
            var req = route.Request;
            var normalized = UrlNormalizer.Normalize(req.Url);
            var blocked = IsBlocked(normalized, req.Url);
            if (normalized is not null)
                found.TryAdd(normalized, new ScannedUrl(normalized, KindOf(req), blocked));
            try
            {
                if (blocked) { await route.AbortAsync("blockedbyclient"); return; }
                if (req.ResourceType == "media") { await route.AbortAsync("aborted"); return; } // no audio/video downloads

                IAPIResponse resp;
                try
                {
                    resp = await route.FetchAsync(new() { MaxRedirects = 0, Timeout = Remaining() });
                }
                catch (PlaywrightException ex)
                {
                    await route.AbortAsync(AbortCode(ex.Message));
                    return;
                }

                if (resp.Status is >= 300 and < 400 && resp.Headers.TryGetValue("location", out var location))
                {
                    var target = UrlNormalizer.Normalize(location, new Uri(req.Url));
                    var targetBlocked = IsBlocked(target, target ?? location);
                    if (target is not null)
                        found.TryAdd(target, new ScannedUrl(target, FindingKind.Redirect, targetBlocked));
                    if (targetBlocked) { await route.AbortAsync("blockedbyclient"); return; }
                }

                await route.FulfillAsync(new() { Response = resp });
            }
            catch (PlaywrightException) { /* page/context already closed */ }
        });

        var page = await context.NewPageAsync();
        page.Dialog += (_, d) => _ = d.DismissAsync();
        page.Popup += (_, p) => _ = p.CloseAsync();

        IResponse? response;
        try
        {
            response = await page.GotoAsync(url, new() { Timeout = Remaining(), WaitUntil = WaitUntilState.Load });
        }
        catch (TimeoutException)
        {
            return (ScanStatus.Timeout, $"Stránka se nenačetla do {options.Timeout.TotalSeconds:0} s", null);
        }
        catch (PlaywrightException ex)
        {
            return (ScanStatus.Unreachable, FirstLine(ex.Message), null);
        }

        // Give late JS a moment to fire its requests, within the overall limit.
        try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = Math.Min(3000, Remaining()) }); }
        catch (TimeoutException) { /* busy pages never go idle; that is fine */ }

        ct.ThrowIfCancellationRequested();
        var baseUri = new Uri(page.Url);
        var dom = await page.EvaluateAsync<JsonElement>(DomScript);
        AddDom(found, dom, "links", FindingKind.Link, baseUri);
        AddDom(found, dom, "forms", FindingKind.Form, baseUri);
        AddDom(found, dom, "iframes", FindingKind.Iframe, baseUri);

        if (response is { Status: >= 400 })
            return (ScanStatus.Unreachable, $"HTTP {response.Status} {response.StatusText}".TrimEnd(), page.Url);

        return (ScanStatus.Completed, null, page.Url);
    }

    private static void AddDom(ConcurrentDictionary<string, ScannedUrl> found, JsonElement dom, string prop, string kind, Uri baseUri)
    {
        foreach (var el in dom.GetProperty(prop).EnumerateArray())
        {
            var normalized = UrlNormalizer.Normalize(el.GetString(), baseUri);
            if (normalized is not null) found.TryAdd(normalized, new ScannedUrl(normalized, kind, false));
        }
    }

    private static string KindOf(IRequest req)
    {
        if (req.RedirectedFrom is not null) return FindingKind.Redirect;
        return req.ResourceType switch
        {
            "script" => FindingKind.Script,
            "stylesheet" => FindingKind.Stylesheet,
            "image" => FindingKind.Image,
            "document" when req.Frame.ParentFrame is not null => FindingKind.Iframe,
            _ => FindingKind.Other,
        };
    }

    private static string FirstLine(string s) => s.Split('\n', 2)[0].Trim();

    /// <summary>Maps a fetch failure to a Chromium abort reason, so navigation errors stay descriptive.</summary>
    private static string AbortCode(string message) => message switch
    {
        _ when message.Contains("ENOTFOUND") || message.Contains("EAI_AGAIN") => "namenotresolved",
        _ when message.Contains("ECONNREFUSED") => "connectionrefused",
        _ when message.Contains("ECONNRESET") => "connectionreset",
        _ when message.Contains("Timeout", StringComparison.OrdinalIgnoreCase) || message.Contains("ETIMEDOUT") => "timedout",
        _ when message.Contains("certificate", StringComparison.OrdinalIgnoreCase) || message.Contains("CERT_") => "connectionfailed",
        _ => "failed",
    };
}
