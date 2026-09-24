using Blog.Core.Services;
using Blog.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// The comment-form token is bound to the post it was rendered for: a token fetched from one post
/// must not verify for another, otherwise one page load yields a 24-hour pass for the whole site.
/// </summary>
public class CommentFormTokenTests
{
    private static CommentFormTokenService NewService() => new(new EphemeralDataProtectionProvider());

    [Fact]
    public void Token_verifies_for_the_post_it_was_issued_for_and_reports_its_age()
    {
        var svc = NewService();
        var postId = Guid.NewGuid();
        var issued = new DateTime(2026, 9, 17, 10, 0, 0, DateTimeKind.Utc);
        var token = svc.Issue(postId, issued);

        var state = svc.Validate(token, postId, out var age, issued.AddSeconds(42));
        Assert.Equal(CommentFormTokenState.Valid, state);
        Assert.Equal(TimeSpan.FromSeconds(42), age);
    }

    [Fact]
    public void Token_from_another_post_is_invalid()
    {
        var svc = NewService();
        var token = svc.Issue(Guid.NewGuid());
        Assert.Equal(CommentFormTokenState.Invalid, svc.Validate(token, Guid.NewGuid(), out var age));
        Assert.Null(age);
    }

    [Fact]
    public void Unbound_legacy_token_is_invalid_when_a_post_is_required_but_still_validates_unbound()
    {
        var svc = NewService();
        var legacy = svc.Issue();
        Assert.Equal(CommentFormTokenState.Invalid, svc.Validate(legacy, Guid.NewGuid(), out _));
        Assert.Equal(CommentFormTokenState.Valid, svc.Validate(legacy, out var age));
        Assert.NotNull(age);
    }

    [Fact]
    public void Missing_and_tampered_tokens_are_distinguished()
    {
        var svc = NewService();
        var postId = Guid.NewGuid();
        Assert.Equal(CommentFormTokenState.Missing, svc.Validate("", postId, out _));
        Assert.Equal(CommentFormTokenState.Invalid, svc.Validate("not-a-token", postId, out _));
        Assert.Equal(CommentFormTokenState.Invalid, NewService().Validate(svc.Issue(postId), postId, out _)); // other key ring
    }
}
