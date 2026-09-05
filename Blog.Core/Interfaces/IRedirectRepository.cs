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

    /// <summary>Every rule, newest first. Rules accrue silently from slug changes and imports, so the
    /// operator needs to see the whole set, not just the one they are editing.</summary>
    Task<IReadOnlyList<RedirectRule>> GetAllAsync();

    /// <summary>Removes a rule. The URL reverts to whatever it would otherwise serve — usually a 404.</summary>
    Task<bool> DeleteAsync(string from);

    /// <summary>
    /// Logged 404 paths that no rule already covers, most-recent first. Raw — triage is
    /// <see cref="Blog.Core.Services.NotFoundTriage"/>'s job, not the database's.
    /// </summary>
    Task<IReadOnlyList<NotFoundGroup>> GetUnhandledNotFoundsAsync(int take = 200);

    /// <summary>Slugs of published posts, for matching a 404 against what actually exists.</summary>
    Task<IReadOnlyList<string>> GetPublishedSlugsAsync();
}
