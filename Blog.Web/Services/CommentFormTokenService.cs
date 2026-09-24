using System.Globalization;
using Blog.Core.Services;
using Microsoft.AspNetCore.DataProtection;

namespace Blog.Web.Services;

/// <summary>
/// Issues and verifies the hidden timing token carried by every public comment form.
///
/// The token is the form's render time, protected by ASP.NET Core Data Protection so it cannot be
/// forged or back-dated. Its presence proves the form was actually fetched; its age tells the spam
/// filter whether a human could have written the comment in the time elapsed. A token issued under a
/// key ring that has since rotated verifies as <see cref="CommentFormTokenState.Invalid"/>, which the
/// filter treats as "ask the reader to reload" rather than as spam.
/// </summary>
public sealed class CommentFormTokenService
{
    public const string FieldName = "commentToken";
    /// <summary>The honeypot input's name. Deliberately unlike any autofill-recognised field.</summary>
    public const string HoneypotFieldName = "contact_ref";

    private readonly IDataProtector _protector;

    public CommentFormTokenService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Inkwell.CommentForm.v1");
    }

    public string Issue(DateTime? utcNow = null)
    {
        var ticks = (utcNow ?? DateTime.UtcNow).Ticks.ToString(CultureInfo.InvariantCulture);
        return _protector.Protect(ticks);
    }

    /// <summary>
    /// A token bound to one post: it verifies only for a submission to that post, so a token fetched
    /// from one page cannot be replayed against every other post for its 24-hour lifetime.
    /// </summary>
    public string Issue(Guid postId, DateTime? utcNow = null)
    {
        var ticks = (utcNow ?? DateTime.UtcNow).Ticks.ToString(CultureInfo.InvariantCulture);
        return _protector.Protect(ticks + "|" + postId.ToString("N"));
    }

    /// <summary>Validates a post-bound token. An unbound token or one bound to another post is Invalid.</summary>
    public CommentFormTokenState Validate(string? token, Guid postId, out TimeSpan? age, DateTime? utcNow = null)
    {
        age = null;
        if (string.IsNullOrWhiteSpace(token)) return CommentFormTokenState.Missing;
        string raw;
        try { raw = _protector.Unprotect(token); }
        catch { return CommentFormTokenState.Invalid; }
        var bar = raw.IndexOf('|');
        if (bar < 0 || !string.Equals(raw[(bar + 1)..], postId.ToString("N"), StringComparison.Ordinal))
            return CommentFormTokenState.Invalid;
        return ValidateTicks(raw[..bar], out age, utcNow);
    }

    /// <summary>Validates a token without checking which post it was issued for (legacy callers and tests).</summary>
    public CommentFormTokenState Validate(string? token, out TimeSpan? age, DateTime? utcNow = null)
    {
        age = null;
        if (string.IsNullOrWhiteSpace(token))
            return CommentFormTokenState.Missing;
        string raw;
        try { raw = _protector.Unprotect(token); }
        catch { return CommentFormTokenState.Invalid; }
        var bar = raw.IndexOf('|');
        return ValidateTicks(bar < 0 ? raw : raw[..bar], out age, utcNow);
    }

    private static CommentFormTokenState ValidateTicks(string raw, out TimeSpan? age, DateTime? utcNow)
    {
        age = null;
        try
        {
            if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
                return CommentFormTokenState.Invalid;

            var issued = new DateTime(ticks, DateTimeKind.Utc);
            var elapsed = (utcNow ?? DateTime.UtcNow) - issued;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            age = elapsed;
            return CommentFormTokenState.Valid;
        }
        catch
        {
            // Tampered payload, or a key that no longer exists on this host.
            return CommentFormTokenState.Invalid;
        }
    }
}
