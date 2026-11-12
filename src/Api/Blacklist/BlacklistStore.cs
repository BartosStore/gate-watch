using System.Collections.Frozen;

namespace Api.Blacklist;

/// <summary>In-memory blacklist (normalized URL -> source). Null until the first load finishes.</summary>
public sealed class BlacklistStore
{
    private volatile FrozenDictionary<string, string>? _entries;

    public bool Ready => _entries is not null;
    public int Count => _entries?.Count ?? 0;

    public void Load(IEnumerable<KeyValuePair<string, string>> entries) =>
        _entries = entries.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Exact match on an already normalized URL.</summary>
    public bool TryMatch(string normalizedUrl, out string source)
    {
        source = "";
        var e = _entries;
        if (e is null || !e.TryGetValue(normalizedUrl, out var s)) return false;
        source = s;
        return true;
    }
}
