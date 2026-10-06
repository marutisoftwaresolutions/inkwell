using System.Security.Claims;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Services;
using Blog.Web.Services.Newsletter;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

/// <summary>
/// Admin → Newsletter (ADR-0028): write an issue (subject, preheader, intro in Markdown, up to six posts), preview
/// it, send a test to yourself, then send it to every confirmed subscriber. Sending is done by the newsletter job
/// in batches; this screen queues, follows progress and can cancel what has not gone out yet. Admin-only; not
/// offered in cloud mode, where subscribers are not separated by site.
/// </summary>
[Authorize(Policy = "AdminOnly")]
[Route("admin/newsletter")]
public class NewsletterAdminController : Controller
{
    public const int MaxSubjectLength = 200;
    public const int MaxPreheaderLength = 200;
    public const int MaxIntroLength = 20000;

    private readonly INewsletterRepository _issues;
    private readonly IMemberRepository _members;
    private readonly IPostRepository _posts;
    private readonly NewsletterService _newsletter;
    private readonly IEmailService _email;
    private readonly AuditService _audit;
    private readonly ITenantContext _tenant;

    public NewsletterAdminController(INewsletterRepository issues, IMemberRepository members, IPostRepository posts,
        NewsletterService newsletter, IEmailService email, AuditService audit, ITenantContext tenant)
    {
        _issues = issues;
        _members = members;
        _posts = posts;
        _newsletter = newsletter;
        _email = email;
        _audit = audit;
        _tenant = tenant;
    }

    // Self-hosted: one site, one subscriber list. (Cloud mode answers 404 below.)
    private static Guid Owner => Guid.Empty;
    private Guid Me => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        if (_tenant.IsCloudMode) return NotFound();
        ViewBag.Subscribers = await _members.GetConfirmedCountAsync();
        ViewBag.EmailConfigured = _email.IsConfigured;
        ViewData["ListSearchPlaceholder"] = "Search newsletters by subject";
        return View(Blog.Web.Models.ListPaging.Apply(this, await _issues.ListAsync(Owner), q, page, i => new[] { i.Subject }));
    }

    [HttpGet("new")]
    public async Task<IActionResult> Create()
    {
        if (_tenant.IsCloudMode) return NotFound();
        await FillEditorAsync(null);
        return View("Edit", new NewsletterIssue());
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Edit(Guid id)
    {
        if (_tenant.IsCloudMode) return NotFound();
        var issue = await _issues.GetAsync(id, Owner);
        if (issue is null) return NotFound();
        if (!issue.IsDraft)
        {
            ViewBag.Deliveries = await _issues.GetDeliveriesAsync(id, Owner, 100);
            ViewBag.Progress = await _issues.GetProgressAsync(id, Owner);
            return View("Detail", issue);
        }
        await FillEditorAsync(issue);
        return View(issue);
    }

    /// <summary>The progress fragment the status page polls while a newsletter is sending.</summary>
    [HttpGet("{id:guid}/progress")]
    public async Task<IActionResult> Progress(Guid id)
    {
        if (_tenant.IsCloudMode) return NotFound();
        var issue = await _issues.GetAsync(id, Owner);
        if (issue is null || issue.IsDraft) return NotFound();
        ViewBag.Progress = await _issues.GetProgressAsync(id, Owner);
        return PartialView("_Progress", issue);
    }

    [HttpPost("save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(Guid? id, string? subject, string? preheader, string? introMarkdown, List<Guid>? postIds)
    {
        if (_tenant.IsCloudMode) return NotFound();
        var issue = id is Guid existing ? await _issues.GetAsync(existing, Owner) : new NewsletterIssue { OwnerId = Owner, CreatedBy = Me };
        if (issue is null) return NotFound();
        if (!issue.IsDraft)
        {
            TempData["Error"] = "This newsletter has been sent or queued, so it can no longer be changed.";
            return RedirectToAction(nameof(Edit), new { id = issue.Id });
        }

        issue.Subject = (subject ?? "").Trim();
        issue.Preheader = string.IsNullOrWhiteSpace(preheader) ? null : preheader.Trim();
        issue.IntroMarkdown = string.IsNullOrWhiteSpace(introMarkdown) ? null : introMarkdown.Trim();
        issue.PostIds = string.Join(",", (postIds ?? new()).Distinct().Take(NewsletterService.MaxPosts));

        if (issue.Subject.Length == 0) ModelState.AddModelError("Subject", "Give the newsletter a subject: it is the first thing readers see in their inbox.");
        else if (issue.Subject.Length > MaxSubjectLength) ModelState.AddModelError("Subject", $"Keep the subject under {MaxSubjectLength} characters.");
        if (issue.Preheader?.Length > MaxPreheaderLength) ModelState.AddModelError("Preheader", $"Keep the preview text under {MaxPreheaderLength} characters.");
        if (issue.IntroMarkdown?.Length > MaxIntroLength) ModelState.AddModelError("IntroMarkdown", $"Keep the introduction under {MaxIntroLength:N0} characters.");
        if (issue.IntroMarkdown is null && issue.PostIdList.Count == 0) ModelState.AddModelError("IntroMarkdown", "Write an introduction or choose at least one post: the newsletter would be empty.");
        if ((postIds?.Distinct().Count() ?? 0) > NewsletterService.MaxPosts) ModelState.AddModelError("PostIds", $"Choose up to {NewsletterService.MaxPosts} posts.");
        if (!ModelState.IsValid)
        {
            await FillEditorAsync(issue.Id == Guid.Empty ? null : issue);
            return View("Edit", issue);
        }

        var isNew = issue.Id == Guid.Empty;
        if (isNew) await _issues.CreateAsync(issue);
        else if (!await _issues.UpdateDraftAsync(issue))
        {
            TempData["Error"] = "This newsletter was queued in another window, so these changes were not saved.";
            return RedirectToAction(nameof(Edit), new { id = issue.Id });
        }
        await _audit.LogAsync(isNew ? AuditActions.NewsletterDrafted : AuditActions.NewsletterUpdated, "Newsletter", issue.Id.ToString(), issue.Subject);
        TempData["Success"] = isNew ? "Draft saved." : "Changes saved.";
        return RedirectToAction(nameof(Edit), new { id = issue.Id });
    }

    /// <summary>The email as subscribers will see it, for the editor's preview frame. Sandboxed: no scripts run.</summary>
    [HttpGet("{id:guid}/preview")]
    public async Task<IActionResult> Preview(Guid id)
    {
        if (_tenant.IsCloudMode) return NotFound();
        var issue = await _issues.GetAsync(id, Owner);
        if (issue is null) return NotFound();
        var siteUrl = issue.SiteBaseUrl ?? _newsletter.SiteUrl(HttpContext);
        var html = issue.IsDraft ? (await _newsletter.RenderAsync(issue, siteUrl)).Html : issue.RenderedHtml ?? "";
        Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'; img-src * data:; style-src 'unsafe-inline'";
        return Content(NewsletterEmail.Personalize(html, siteUrl, html: true), "text/html; charset=utf-8");
    }

    [HttpPost("{id:guid}/test")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendTest(Guid id)
    {
        if (_tenant.IsCloudMode) return NotFound();
        var issue = await _issues.GetAsync(id, Owner);
        if (issue is null) return NotFound();
        var to = User.FindFirstValue(ClaimTypes.Email);
        if (!_email.IsConfigured) TempData["Error"] = "Outgoing email is not set up on this server (the Smtp settings), so nothing can be sent.";
        else if (string.IsNullOrWhiteSpace(to)) TempData["Error"] = "Your account has no email address to send the test to.";
        else
        {
            var r = await _newsletter.SendTestAsync(issue, issue.SiteBaseUrl ?? _newsletter.SiteUrl(HttpContext), to, User.Identity?.Name);
            if (r.Sent)
            {
                await _audit.LogAsync(AuditActions.NewsletterTestSent, "Newsletter", issue.Id.ToString(), issue.Subject, newValues: "Test sent to " + to);
                TempData["Success"] = $"Test sent to {to}.";
            }
            else TempData["Error"] = "The test could not be sent: " + r.Error;
        }
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("{id:guid}/send")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(Guid id)
    {
        if (_tenant.IsCloudMode) return NotFound();
        var issue = await _issues.GetAsync(id, Owner);
        if (issue is null) return NotFound();
        var recipients = await _members.GetConfirmedCountAsync();
        if (!_email.IsConfigured)
        {
            TempData["Error"] = "Outgoing email is not set up on this server (the Smtp settings), so the newsletter cannot be sent.";
            return RedirectToAction(nameof(Edit), new { id });
        }
        if (recipients == 0)
        {
            TempData["Error"] = "There are no confirmed subscribers to send to yet.";
            return RedirectToAction(nameof(Edit), new { id });
        }
        var siteUrl = _newsletter.SiteUrl(HttpContext);
        var (html, text) = await _newsletter.RenderAsync(issue, siteUrl);
        if (!await _issues.QueueAsync(id, Owner, html, text, siteUrl, Me, User.Identity?.Name, DateTime.UtcNow))
        {
            TempData["Error"] = "This newsletter was already queued or sent.";
            return RedirectToAction(nameof(Edit), new { id });
        }
        await _audit.LogAsync(AuditActions.NewsletterQueued, "Newsletter", id.ToString(), issue.Subject,
            newValues: $"Queued for {recipients} confirmed subscriber(s)");
        TempData["Success"] = $"Sending to {recipients} subscriber{(recipients == 1 ? "" : "s")}. It goes out in batches over the next few minutes; this page shows progress.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("{id:guid}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id)
    {
        if (_tenant.IsCloudMode) return NotFound();
        var issue = await _issues.GetAsync(id, Owner);
        if (issue is null) return NotFound();
        if (await _issues.CancelAsync(id, Owner, DateTime.UtcNow))
        {
            var after = await _issues.GetAsync(id, Owner);
            await _audit.LogAsync(AuditActions.NewsletterCancelled, "Newsletter", id.ToString(), issue.Subject,
                newValues: $"Cancelled after {after?.SentCount ?? 0} of {after?.RecipientCount ?? 0} sent");
            TempData["Success"] = "Sending stopped. Copies already sent cannot be recalled.";
        }
        else TempData["Error"] = "This newsletter had already finished sending.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (_tenant.IsCloudMode) return NotFound();
        var issue = await _issues.GetAsync(id, Owner);
        if (issue is null) return NotFound();
        if (await _issues.DeleteDraftAsync(id, Owner))
        {
            await _audit.LogAsync(AuditActions.NewsletterDeleted, "Newsletter", id.ToString(), issue.Subject);
            TempData["Success"] = "Draft deleted.";
        }
        else TempData["Error"] = "Only drafts can be deleted; sent newsletters are kept as a record.";
        return RedirectToAction(nameof(Index));
    }

    private async Task FillEditorAsync(NewsletterIssue? issue)
    {
        var recent = await _posts.GetPostsAsync(new PostFilter { Status = PostStatus.Published, Page = 1, PageSize = 60 });
        var chosen = issue?.PostIdList ?? Array.Empty<Guid>();
        // Chosen posts first, in their newsletter order, then the latest others.
        var picked = new List<Post>();
        foreach (var pid in chosen)
            if (await _posts.GetByIdAsync(pid, null) is { } p && NewsletterService.IsPublic(p)) picked.Add(p);
        ViewBag.Posts = picked.Concat(recent.Items.Where(p => !chosen.Contains(p.Id))).ToList();
        ViewBag.Subscribers = await _members.GetConfirmedCountAsync();
        ViewBag.EmailConfigured = _email.IsConfigured;
    }
}
