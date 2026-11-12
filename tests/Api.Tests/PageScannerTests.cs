using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Api.Blacklist;
using Api.Data;
using Api.Scanning;

namespace Api.Tests;

/// <summary>Runs only where Playwright browsers are installed (e.g. inside the Playwright Docker image).</summary>
public sealed class PlaywrightFactAttribute : FactAttribute
{
    public PlaywrightFactAttribute()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ms-playwright"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "ms-playwright"),
        };
        if (!candidates.Any(p => !string.IsNullOrEmpty(p) && Directory.Exists(p) && Directory.EnumerateDirectories(p, "chromium*").Any()))
            Skip = "Playwright browsers are not installed (run tests in Docker, see README).";
    }
}

public sealed class PageScannerTests : IAsyncLifetime
{
    private readonly TestSite _site = new();
    private readonly BrowserHost _browser = new();
    private readonly BlacklistStore _blacklist = new();

    public Task InitializeAsync()
    {
        _blacklist.Load(new[] { "/evil.js", "/evil-dyn.js", "/evil-link", "/evil-form", "/evil-redirect-target" }
            .Select(p => new KeyValuePair<string, string>(_site.Url(p), "custom")));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _site.Dispose();
    }

    private PageScanner Scanner(TimeSpan? timeout = null, bool allowPrivate = true) =>
        new(_browser, _blacklist, new ScannerOptions { AllowPrivateNetworks = allowPrivate, Timeout = timeout ?? TimeSpan.FromSeconds(10) });

    [PlaywrightFact]
    public async Task Captures_requests_redirects_dom_and_never_fetches_blacklisted_urls()
    {
        var r = await Scanner().ScanAsync(_site.Url("/"), CancellationToken.None);
        var byUrl = r.Urls.ToDictionary(u => u.Url);

        Assert.Equal(ScanStatus.Completed, r.Status);
        Assert.Equal(_site.Url("/home"), r.FinalUrl);

        Assert.Equal(FindingKind.Redirect, byUrl[_site.Url("/home")].Kind);
        Assert.Equal(FindingKind.Stylesheet, byUrl[_site.Url("/style.css")].Kind);
        Assert.Equal(FindingKind.Image, byUrl[_site.Url("/img.png")].Kind);
        Assert.Equal(FindingKind.Iframe, byUrl[_site.Url("/frame")].Kind);
        Assert.True(byUrl.ContainsKey(_site.Url("/dynamic")), "fetch() created by JS must be captured");

        // Blacklisted: recorded, blocked, never reached the server.
        Assert.Equal(FindingKind.Script, byUrl[_site.Url("/evil.js")].Kind);
        Assert.True(byUrl[_site.Url("/evil.js")].Blocked);
        Assert.True(byUrl[_site.Url("/evil-dyn.js")].Blocked);
        Assert.Equal(FindingKind.Redirect, byUrl[_site.Url("/evil-redirect-target")].Kind);
        Assert.True(byUrl[_site.Url("/evil-redirect-target")].Blocked);
        Assert.Equal(FindingKind.Link, byUrl[_site.Url("/evil-link")].Kind);
        Assert.Equal(FindingKind.Form, byUrl[_site.Url("/evil-form")].Kind);
        Assert.Equal(FindingKind.Link, byUrl[_site.Url("/svg-link")].Kind);       // SVG <a>.href is an object
        Assert.Equal(FindingKind.Form, byUrl[_site.Url("/clobbered-form")].Kind); // <input name="action"> clobbers form.action
        Assert.Equal(1, _site.Hits("/go-evil"));
        foreach (var p in new[] { "/evil.js", "/evil-dyn.js", "/evil-link", "/evil-form", "/evil-redirect-target", "/secret-link" })
            Assert.Equal(0, _site.Hits(p));

        // Addresses only written in text or comments are not findings.
        Assert.DoesNotContain(_site.Url("/in-comment"), byUrl.Keys);
        Assert.DoesNotContain(_site.Url("/in-text"), byUrl.Keys);
    }

    [PlaywrightFact]
    public async Task Slow_page_ends_as_timeout_within_limit()
    {
        var r = await Scanner(TimeSpan.FromSeconds(2)).ScanAsync(_site.Url("/slow"), CancellationToken.None);
        Assert.Equal(ScanStatus.Timeout, r.Status);
        Assert.NotNull(r.Error);
        Assert.InRange(r.DurationMs, 1500, 4000);
    }

    [PlaywrightFact]
    public async Task Http_error_is_unreachable()
    {
        var r = await Scanner().ScanAsync(_site.Url("/missing"), CancellationToken.None);
        Assert.Equal(ScanStatus.Unreachable, r.Status);
        Assert.StartsWith("HTTP 404", r.Error);
    }

    [PlaywrightFact]
    public async Task Private_network_is_blocked_by_default()
    {
        var r = await Scanner(allowPrivate: false).ScanAsync(_site.Url("/"), CancellationToken.None);
        Assert.Equal(ScanStatus.Unreachable, r.Status);
        Assert.Equal(0, _site.Hits("/"));
    }

    [PlaywrightFact]
    public async Task Recovers_after_browser_is_closed()
    {
        Assert.Equal(ScanStatus.Completed, (await Scanner().ScanAsync(_site.Url("/home"), CancellationToken.None)).Status);
        await (await _browser.GetAsync()).CloseAsync();
        Assert.Equal(ScanStatus.Completed, (await Scanner().ScanAsync(_site.Url("/home"), CancellationToken.None)).Status);
    }

    [PlaywrightFact]
    public async Task Recovers_after_driver_process_is_killed()
    {
        Assert.Equal(ScanStatus.Completed, (await Scanner().ScanAsync(_site.Url("/home"), CancellationToken.None)).Status);
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("node")) p.Kill(entireProcessTree: true);

        // The check running into the dead driver may fail (never as a clean OK), the next one must work again.
        var first = await Scanner().ScanAsync(_site.Url("/home"), CancellationToken.None);
        if (first.Status != ScanStatus.Completed) Assert.Equal(PageScanner.BrowserCrashed, first.Error);
        Assert.Equal(ScanStatus.Completed, (await Scanner().ScanAsync(_site.Url("/home"), CancellationToken.None)).Status);
    }

    [PlaywrightFact]
    public async Task Dns_failure_is_unreachable()
    {
        var r = await Scanner().ScanAsync("https://does-not-exist.invalid/", CancellationToken.None);
        Assert.Equal(ScanStatus.Unreachable, r.Status);
        Assert.Contains("ERR_NAME_NOT_RESOLVED", r.Error);
    }
}

/// <summary>Tiny local web site with a redirect, resources, JS-created requests and blacklisted references.</summary>
internal sealed class TestSite : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<string, int> _hits = new();
    private readonly int _port;

    public TestSite()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        _port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        _listener.Start();
        _ = Task.Run(LoopAsync);
    }

    public string Url(string path) => $"http://127.0.0.1:{_port}{path}";
    public int Hits(string path) => _hits.GetValueOrDefault(path);

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); } catch { return; }
            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url!.AbsolutePath;
        _hits.AddOrUpdate(path, 1, (_, n) => n + 1);
        var res = ctx.Response;
        try
        {
            switch (path)
            {
                case "/":
                    res.Redirect(Url("/home"));
                    break;
                case "/go-evil":
                    res.Redirect(Url("/evil-redirect-target"));
                    break;
                case "/home":
                    await WriteAsync(res, "text/html", $$"""
                        <!doctype html><html><head>
                        <link rel="stylesheet" href="/style.css">
                        <script src="/evil.js"></script>
                        </head><body>
                        <!-- {{Url("/in-comment")}} -->
                        <p>Visit {{Url("/in-text")}} for more.</p>
                        <img src="/img.png">
                        <a href="/evil-link">bad link</a>
                        <svg><a href="/svg-link"><text>svg link</text></a></svg>
                        <form action="/clobbered-form"><input name="action"></form>
                        <a href="/secret-link">never clicked</a>
                        <form action="/evil-form" method="post"><input name="q"></form>
                        <iframe src="/frame"></iframe>
                        <script>
                          fetch('/dynamic');
                          fetch('/go-evil').catch(() => {});
                          var s = document.createElement('script'); s.src = '/evil-dyn.js'; document.head.appendChild(s);
                          alert('dialog must be dismissed');
                        </script>
                        </body></html>
                        """);
                    break;
                case "/slow":
                    await Task.Delay(TimeSpan.FromSeconds(15));
                    await WriteAsync(res, "text/html", "<html>late</html>");
                    break;
                case "/style.css": await WriteAsync(res, "text/css", "body{}"); break;
                case "/frame": await WriteAsync(res, "text/html", "<html>frame</html>"); break;
                case "/img.png" or "/dynamic": await WriteAsync(res, "text/plain", "ok"); break;
                default:
                    res.StatusCode = 404;
                    await WriteAsync(res, "text/plain", "not found");
                    break;
            }
        }
        catch { /* client went away */ }
        finally
        {
            try { res.Close(); } catch { }
        }
    }

    private static async Task WriteAsync(HttpListenerResponse res, string type, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        res.ContentType = type + "; charset=utf-8";
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
    }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
    }
}
