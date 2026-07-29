using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Blog.Core.Domain;
using Blog.Core.Interfaces;

namespace Blog.Web.Services;

/// <summary>
/// Builds an <see cref="ErrorLog"/> from the current request (+ optional exception) and records it,
/// grouping similar errors by a stable fingerprint. Best-effort and never throws into the request path.
/// </summary>
public sealed class ErrorLogService
{
    private readonly IErrorLogRepository _repo;
    private readonly ILogger<ErrorLogService> _log;
    private readonly IEmailService _email;
    private readonly ISettingRepository _settings;
    private readonly IUserRepository _users;

    public ErrorLogService(
        IErrorLogRepository repo, ILogger<ErrorLogService> log,
        IEmailService email, ISettingRepository settings, IUserRepository users)
    {
        _repo = repo;
        _log = log;
        _email = email;
        _settings = settings;
        _users = users;
    }

    // Paths we never want to log (noise / infinite-loop risk).
    private static readonly Regex _ignore = new(
        @"^/(error|favicon\.ico|robots\.txt|apple-touch-icon).*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task RecordAsync(HttpContext ctx, int statusCode, Exception? ex = null)
    {
        try
        {
            var path = ctx.Request.Path.Value ?? "/";
            if (_ignore.IsMatch(path)) return;

            var exceptionType = ex?.GetType().FullName;
            var norm = NormalizePath(path);
            var fingerprint = Hash($"{statusCode}|{norm}|{exceptionType}");

            var entry = new ErrorLog
            {
                Fingerprint   = fingerprint,
                StatusCode    = statusCode,
                Method        = ctx.Request.Method,
                Path          = Trim(path, 1024),
                ExceptionType = Trim(exceptionType, 256),
                Message       = Trim(ex?.Message, 2048),
                StackTrace    = Trim(ex?.StackTrace, 8000),
                UserAgent     = Trim(ctx.Request.Headers.UserAgent.ToString(), 512),
                Referer       = Trim(ctx.Request.Headers.Referer.ToString(), 1024),
            };
            var isNew = await _repo.RecordAsync(entry);

            // Notify on a NEW server-error (5xx) signature only — repeats and 404 noise never email.
            if (isNew && statusCode >= 500)
                await NotifyAsync(ctx, entry);
        }
        catch (Exception logEx)
        {
            _log.LogWarning(logEx, "Failed to record error log (non-fatal).");
        }
    }

    private async Task NotifyAsync(HttpContext ctx, ErrorLog entry)
    {
        try
        {
            var admin = await _users.GetFirstAdminAsync();
            if (admin is null) return;
            var s = await _settings.GetSettingsAsync(admin.Id);
            if (!s.ErrorNotificationsEnabled || string.IsNullOrWhiteSpace(s.ErrorNotificationEmails)) return;

            var recipients = s.ErrorNotificationEmails
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(a => a.Contains('@')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (recipients.Count == 0) return;

            var host = ctx.Request.Host.Value;
            var subject = $"[{host}] New server error: {entry.StatusCode} {entry.Path}";
            var body =
                $"<p>A new server error signature was detected on <strong>{System.Net.WebUtility.HtmlEncode(host)}</strong>.</p>" +
                $"<table cellpadding='4' style='font-family:sans-serif;font-size:14px'>" +
                $"<tr><td><b>Status</b></td><td>{entry.StatusCode}</td></tr>" +
                $"<tr><td><b>Method</b></td><td>{System.Net.WebUtility.HtmlEncode(entry.Method)}</td></tr>" +
                $"<tr><td><b>Path</b></td><td>{System.Net.WebUtility.HtmlEncode(entry.Path)}</td></tr>" +
                $"<tr><td><b>Type</b></td><td>{System.Net.WebUtility.HtmlEncode(entry.ExceptionType ?? "-")}</td></tr>" +
                $"<tr><td><b>Message</b></td><td>{System.Net.WebUtility.HtmlEncode(entry.Message ?? "-")}</td></tr>" +
                $"<tr><td valign='top'><b>Time (UTC)</b></td><td>{DateTime.UtcNow:u}</td></tr>" +
                $"</table>" +
                $"<p style='color:#666;font-size:12px'>You receive this once per new error signature. See all errors in Admin &rarr; Error Monitor. This alert covers server errors (5xx) only.</p>";

            foreach (var to in recipients)
            {
                try { await _email.SendAsync(to, to, subject, body); }
                catch (Exception mailEx) { _log.LogWarning(mailEx, "Error-alert email to {To} failed.", to); }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Error-notification step failed (non-fatal).");
        }
    }

    // Collapse volatile path segments so similar URLs group together: GUIDs and numeric ids -> '#'.
    private static string NormalizePath(string path)
    {
        path = path.ToLowerInvariant().Split('?')[0].TrimEnd('/');
        if (path.Length == 0) path = "/";
        path = Regex.Replace(path, @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", "#");
        path = Regex.Replace(path, @"\b\d+\b", "#");
        return path;
    }

    private static string Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..32];
    }

    private static string? Trim(string? s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length > max ? s[..max] : s);
}
