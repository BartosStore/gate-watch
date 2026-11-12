using Microsoft.Playwright;

namespace Api.Scanning;

/// <summary>One shared headless Chromium for the whole process; relaunched if it crashes.</summary>
public sealed class BrowserHost : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public string UserAgent { get; private set; } = "";

    public async Task<IBrowser> GetAsync()
    {
        if (_browser is { IsConnected: true }) return _browser;
        await _lock.WaitAsync();
        try
        {
            if (_browser is { IsConnected: true }) return _browser;
            _playwright ??= await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new()
            {
                Headless = true,
                // Chromium's own sandbox stays off (Playwright default); isolation is the container. See docs/PREDPOKLADY.md.
                Args = ["--disable-gpu", "--disable-dev-shm-usage", "--no-first-run", "--disable-extensions"],
            });
            var major = _browser.Version.Split('.')[0];
            UserAgent = $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{major}.0.0.0 Safari/537.36";
            return _browser;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.CloseAsync();
        _playwright?.Dispose();
    }
}
