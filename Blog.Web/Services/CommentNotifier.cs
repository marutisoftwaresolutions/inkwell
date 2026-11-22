using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;

namespace Blog.Web.Services;

/// <summary>Counts comment emails per site so a burst of comments cannot become a burst of mail. In memory; a restart starts the hour again.</summary>
public sealed class CommentNoticeThrottle
{
    private readonly Dictionary<Guid, List<DateTime>> _sent = new();

    public bool TryTake(Guid ownerId, DateTime nowUtc)
    {
        lock (_sent)
        {
            if (!_sent.TryGetValue(ownerId, out var list)) _sent[ownerId] = list = new List<DateTime>();
            return CommentNotice.TryTake(list, nowUtc);
        }
    }
}

/// <summary>
/// Emails the people who moderate a site when a reader comments (Settings → Comments). Called after
/// the comment is stored. Optional work, so it fails open: with no mail server, no recipient, the
/// hourly limit reached or a send that fails, the comment stands and the reader's reply is unchanged.
/// A reply written by signed-in staff is not announced.
/// </summary>
public sealed class CommentNotifier
{
    private static readonly TimeSpan SendLimit = TimeSpan.FromSeconds(10);

    private readonly ISettingRepository _settings;
    private readonly IUserRepository _users;
    private readonly IEmailService _email;
    private readonly CommentNoticeThrottle _throttle;
    private readonly IConfiguration _config;
    private readonly ILogger<CommentNotifier> _log;

    public CommentNotifier(ISettingRepository settings, IUserRepository users, IEmailService email,
        CommentNoticeThrottle throttle, IConfiguration config, ILogger<CommentNotifier> log)
    {
        _settings = settings;
        _users = users;
        _email = email;
        _throttle = throttle;
        _config = config;
        _log = log;
    }

    public async Task NotifyAsync(HttpContext http, Guid ownerId, Comment comment, Post post)
    {
        try
        {
            if (http.User.Identity?.IsAuthenticated == true || !_email.IsConfigured) return;
            var s = await _settings.GetSettingsAsync(ownerId);
            if (!s.CommentNotifyEnabled) return;

            var authorEmail = string.IsNullOrWhiteSpace(s.CommentNotifyEmails) ? (await _users.GetByIdAsync(post.AuthorId))?.Email : null;
            var recipients = CommentNotice.Recipients(s.CommentNotifyEmails, authorEmail);
            if (recipients.Count == 0 || !_throttle.TryTake(ownerId, DateTime.UtcNow)) return;

            // Links use the configured site address when there is one, so a forged Host header cannot aim them elsewhere.
            var canonical = _config["CanonicalHost"];
            var baseUrl = !string.IsNullOrWhiteSpace(canonical) ? $"https://{canonical.Trim()}" : $"{http.Request.Scheme}://{http.Request.Host}";
            var message = CommentNotice.Build(s.SiteName, post.Title, $"{baseUrl}/{post.Slug}", $"{baseUrl}/admin/comments?status=pending",
                comment.AuthorName, comment.Content, held: comment.Status != CommentStatus.Approved, isReply: comment.ParentId is not null);

            using var limit = new CancellationTokenSource(SendLimit);
            foreach (var to in recipients)
            {
                var result = await _email.SendMessageAsync(new EmailMessage(to, null, message.Subject, message.Html, message.Text), limit.Token);
                if (!result.Sent) _log.LogWarning("Comment email to a moderator was not sent: {Error}", result.Error);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Comment email step failed (non-fatal).");
        }
    }
}
