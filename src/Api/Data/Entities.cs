namespace Api.Data;

public static class RunStatus
{
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";
}

public static class RunItemState
{
    public const string Pending = "pending";
    public const string Done = "done";
}

public static class CheckOutcome
{
    public const string Ok = "ok";
    public const string Found = "found";
    public const string Timeout = "timeout";
    public const string Unreachable = "unreachable";
}

public static class FindingKind
{
    public const string Script = "script";
    public const string Stylesheet = "stylesheet";
    public const string Image = "image";
    public const string Iframe = "iframe";
    public const string Link = "link";
    public const string Form = "form";
    public const string Redirect = "redirect";
    public const string Other = "other";
}

public class Merchant
{
    public required string Id { get; set; }
    public required string Url { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Run
{
    public Guid Id { get; set; }
    public string Status { get; set; } = RunStatus.Running;
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

public class RunItem
{
    public Guid RunId { get; set; }
    public required string MerchantId { get; set; }
    public string State { get; set; } = RunItemState.Pending;
    public Guid? CheckId { get; set; }
}

public class Check
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public required string MerchantId { get; set; }
    public required string Outcome { get; set; }
    public string? Error { get; set; }
    public int DurationMs { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public int UrlsSeen { get; set; }
}

public class Finding
{
    public required string Id { get; set; }
    public required string MerchantId { get; set; }
    public required string Url { get; set; }
    public required string Kind { get; set; }
    public required string BlacklistSource { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public Guid FirstCheckId { get; set; }
    public Guid LastCheckId { get; set; }
    public bool Resolved { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? Note { get; set; }
}

public class BlacklistEntry
{
    public required string Url { get; set; }
    public required string Source { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}

/// <summary>Single-row table (Id = 1) describing the last blacklist import.</summary>
public class BlacklistMeta
{
    public int Id { get; set; } = 1;
    public DateTimeOffset LastImportAt { get; set; }
    public int EntryCount { get; set; }
    public string? SourceNote { get; set; }
}
