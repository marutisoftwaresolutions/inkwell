using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

public interface IRedirectRepository
{
    /// <param name="statusCode">301 (default), 302, or 410 — see <see cref="RedirectStatus"/>.</param>
    Task UpsertAsync(string from, string to, int statusCode = RedirectStatus.MovedPermanently);

    /// <summary>Destination for a move. Returns null when there is no rule, or when the rule is a 410.</summary>
    Task<string?> GetDestinationAsync(string from);

    /// <summary>The full rule for a path — use this when the status code matters (410 vs 301).</summary>
    Task<RedirectRule?> GetRuleAsync(string from);
}
