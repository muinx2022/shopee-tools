namespace Shopee.Workspace.Modules.Scrape;

public enum ScrapeJobStatus { NotStarted, Completed, Stopped, Failed }

/// <summary>Kết quả THẬT của 1 job scrape (1 tk BigSeller) do <c>RunSingleAsync</c> trả về — thay cho việc
/// suy từ text <c>Status</c>. <see cref="Reason"/> rỗng với Completed/Stopped.</summary>
public readonly record struct ScrapeJobOutcome(ScrapeJobStatus Status, string Reason = "")
{
    public static ScrapeJobOutcome NotStarted(string reason) => new(ScrapeJobStatus.NotStarted, reason);
    public static ScrapeJobOutcome Completed() => new(ScrapeJobStatus.Completed);
    public static ScrapeJobOutcome Stopped() => new(ScrapeJobStatus.Stopped);
    public static ScrapeJobOutcome Failed(string reason) => new(ScrapeJobStatus.Failed, reason);
}
