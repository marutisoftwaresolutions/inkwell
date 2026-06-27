using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface IPageViewRepository
{
    Task RecordAsync(PageView view);
    Task<int> GetTotalAsync(Guid ownerId, DateTime from, DateTime to);
    Task<IReadOnlyList<(DateTime Date, int Count)>> GetDailyAsync(Guid ownerId, DateTime from, DateTime to);
    Task<IReadOnlyList<(string Source, string Medium, int Count)>> GetSourceBreakdownAsync(Guid ownerId, DateTime from, DateTime to);
    Task<IReadOnlyList<(string Path, int Views)>> GetTopPagesAsync(Guid ownerId, DateTime from, DateTime to, int limit = 20);
    Task<IReadOnlyList<(string Referrer, int Count)>> GetTopReferrersAsync(Guid ownerId, DateTime from, DateTime to, int limit = 20);
    Task<IReadOnlyList<(string Campaign, int Count)>> GetCampaignsAsync(Guid ownerId, DateTime from, DateTime to);
    Task<IReadOnlyList<(string Country, int Count)>> GetCountryBreakdownAsync(Guid ownerId, DateTime from, DateTime to, int limit = 20);
    Task<IReadOnlyList<(string Region, int Count)>> GetRegionBreakdownAsync(Guid ownerId, DateTime from, DateTime to, int limit = 20);
    Task<IReadOnlyList<PageView>> GetRecentAsync(Guid ownerId, int limit = 200);
}
