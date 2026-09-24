using Blog.Core.Services;
using Blog.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Blog.Tests;

public class HttpCacheValidationTests
{
    private static readonly DateTime T = new(2026, 9, 15, 10, 30, 45, 500, DateTimeKind.Utc);

    [Fact]
    public void ETag_is_stable_for_the_same_parts_and_differs_when_any_part_changes()
    {
        var a = HttpCacheValidation.WeakETag(Guid.Empty, T, 3, "layout=Neutral");
        var b = HttpCacheValidation.WeakETag(Guid.Empty, T, 3, "layout=Neutral");
        var c = HttpCacheValidation.WeakETag(Guid.Empty, T, 4, "layout=Neutral");
        var d = HttpCacheValidation.WeakETag(Guid.Empty, T.AddSeconds(1), 3, "layout=Neutral");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.NotEqual(a, d);
        Assert.StartsWith("W/\"", a);
    }

    [Fact]
    public void Http_dates_drop_sub_second_precision_so_the_round_trip_compares_equal()
    {
        var lm = HttpCacheValidation.ToHttpDate(T);
        Assert.Equal(0, lm.Millisecond);
        var header = lm.ToString("R");
        Assert.True(HttpCacheValidation.IsFresh(null, "W/\"x\"", header, T)); // the original, with millis, still counts as unchanged
    }

    [Fact]
    public void If_none_match_wins_and_compares_weakly()
    {
        var etag = HttpCacheValidation.WeakETag("a");
        Assert.True(HttpCacheValidation.IsFresh(etag, etag, null, T));
        Assert.True(HttpCacheValidation.IsFresh(etag.Substring(2), etag, null, T));           // strong form of the same tag
        Assert.True(HttpCacheValidation.IsFresh("\"other\", " + etag, etag, null, T));         // list
        Assert.True(HttpCacheValidation.IsFresh("*", etag, null, T));
        Assert.False(HttpCacheValidation.IsFresh("W/\"stale\"", etag, T.ToString("R"), T));   // header present but wrong → not fresh, even though IMS would match
    }

    [Fact]
    public void If_modified_since_is_used_only_without_if_none_match()
    {
        var etag = HttpCacheValidation.WeakETag("a");
        Assert.True(HttpCacheValidation.IsFresh(null, etag, T.AddMinutes(5).ToString("R"), T));
        Assert.False(HttpCacheValidation.IsFresh(null, etag, T.AddMinutes(-5).ToString("R"), T));
        Assert.False(HttpCacheValidation.IsFresh(null, etag, "not a date", T));
        Assert.False(HttpCacheValidation.IsFresh(null, etag, null, T));
    }
}

public class PreviewLinkServiceTests
{
    private static PreviewLinkService NewService() => new(new EphemeralDataProtectionProvider());

    [Fact]
    public void A_link_names_its_post_until_it_expires()
    {
        var svc = NewService();
        var id = Guid.NewGuid();
        var issued = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

        var token = svc.Issue(id, issued);
        var claim = svc.Validate(token, issued.AddDays(6));
        Assert.NotNull(claim);
        Assert.Equal(id, claim!.Value.PostId);
        Assert.Equal(issued + PreviewLinkService.Lifetime, claim.Value.ExpiresUtc);

        Assert.Null(svc.Validate(token, issued.AddDays(8)));
    }

    [Fact]
    public void Tampered_missing_or_foreign_tokens_are_rejected()
    {
        var svc = NewService();
        var token = svc.Issue(Guid.NewGuid());

        Assert.Null(svc.Validate(null));
        Assert.Null(svc.Validate(""));
        Assert.Null(svc.Validate(token + "x"));
        Assert.Null(NewService().Validate(token)); // a different key ring cannot read it
    }
}
