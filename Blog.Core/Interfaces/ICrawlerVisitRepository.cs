using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface ICrawlerVisitRepository
{
    /// <summary>Records one identified crawler request. Best-effort; never throws into the request.</summary>
    Task RecordAsync(CrawlerVisit visit);

    /// <summary>Per-crawler totals over the window, busiest first.</summary>
    Task<IReadOnlyList<CrawlerActivity>> GetActivityAsync(Guid? ownerId, int days);

    /// <summary>Pages drawing the most AI-crawler attention over the window.</summary>
    Task<IReadOnlyList<CrawledPage>> GetTopPagesAsync(Guid? ownerId, int days, bool aiOnly = true, int take = 20);

    /// <summary>Daily AI-crawler visit counts over the window, oldest first, for the trend.</summary>
    Task<IReadOnlyList<CrawlerDay>> GetDailyAiVisitsAsync(Guid? ownerId, int days);
}
