using Microsoft.Playwright;

namespace Api.Scanning;

/// <summary>
/// One shared headless Chromium for the whole process. If the browser or the Playwright driver
/// process dies, the next caller starts both again (otherwise every later scan would fail).
/// </summary>
public sealed class BrowserHost(ILogger<BrowserHost>? log = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private volatile bool _broken;

    public string UserAgent { get; private set; } = "";

    public async Task<IBrowser> GetAsync()
    {
        if (_browser is { IsConnected: true } b && !_broken) return b;
        await _lock.WaitAsync();
        try
        {
            if (_browser is { IsConnected: true } b2 && !_broken) return b2;
            if (_browser is not null || _playwright is not null)
            {
                log?.LogWarning("Browser or Playwright driver is gone, restarting it");
                await ShutdownAsync();
            }

            _broken = false;
            _playwright = await Playwright.CreateAsync();
            var browser = await _playwright.Chromium.LaunchAsync(new()
            {
                Headless = true,
                // Chromium's own sandbox stays off (Playwright default); isolation is the container. See docs/PREDPOKLADY.md.
                Args = ["--disable-gpu", "--disable-dev-shm-usage", "--no-first-run", "--disable-extensions"],
            });
            browser.Disconnected += (_, _) => _broken = true;
            var major = browser.Version.Split('.')[0];
            UserAgent = $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{major}.0.0.0 Safari/537.36";
            _browser = browser;
            return browser;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Called when an operation failed because the driver/browser died; the next GetAsync restarts it.</summary>
    public void MarkBroken() => _broken = true;

    public static bool IsDriverFailure(Exception ex) =>
        ex is ObjectDisposedException or IOException
        || (ex is PlaywrightException && IsDriverFailureMessage(ex.Message));

    public static bool IsDriverFailureMessage(string message) =>
        message.Contains("Process exited", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Browser has been closed", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Connection closed", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Target crashed", StringComparison.OrdinalIgnoreCase);

    private async Task ShutdownAsync()
    {
        try { if (_browser is not null) await _browser.CloseAsync(); } catch { /* already dead */ }
        try { _playwright?.Dispose(); } catch { /* already dead */ }
        _browser = null;
        _playwright = null;
    }

    public async ValueTask DisposeAsync() => await ShutdownAsync();
}
