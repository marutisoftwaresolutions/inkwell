using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

[Authorize(Policy = "AdminOnly")]
[Route("admin/subscribers")]
public class SubscribersController : Controller
{
    private readonly IMemberRepository _members;
    private readonly AuditService _audit;

    public SubscribersController(IMemberRepository members, AuditService audit)
    {
        _members = members;
        _audit = audit;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? status, int page = 1)
    {
        const int pageSize = 50;
        var (items, total) = await _members.GetPagedAsync(page, pageSize, status);

        ViewBag.Status = status;
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        ViewBag.Total = total;
        ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);

        var confirmed = await _members.GetConfirmedCountAsync();
        ViewBag.ConfirmedCount = confirmed;

        // Shared _ListPager partial; ?status= survives paging through ListSearchKeep (no search: the repository pages by status only).
        Blog.Web.Models.ListPaging.Stash(this, Blog.Web.Models.PagingMeta.FromCounts(null, page, total, pageSize, items.Count));
        ViewData["ListSearchKeep"] = new Dictionary<string, string?> { ["status"] = status };

        ViewData["Title"] = "Subscribers";
        try { ViewBag.TopSignupPages = await _members.GetTopSignupPagesAsync(8); }
        catch { ViewBag.TopSignupPages = null; }   // a report beside the list must never stop the list
        return View(items);
    }

    /// <summary>The list as CSV. Confirmed subscribers unless a status is asked for. The file holds personal data; the download is audited.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(string? status = "confirmed")
    {
        var wanted = status is "pending" or "unsubscribed" or "bounced" or "confirmed" ? status : null;
        var all = new List<Member>();
        for (var page = 1; ; page++)
        {
            var (items, _) = await _members.GetPagedAsync(page, 500, wanted);
            all.AddRange(items);
            if (items.Count < 500) break;
        }
        await _audit.LogAsync(AuditActions.SubscriberExported, "Subscriber", null, $"{all.Count} {wanted ?? "of every status"}");
        var csv = Blog.Core.Services.SubscriberCsv.Write(all.Select(m => (m.Email, m.Name, m.Status, m.ConfirmedAt)));
        Response.Headers.CacheControl = "no-store";
        return File(new System.Text.UTF8Encoding(true).GetBytes(csv), "text/csv", $"subscribers-{wanted ?? "all"}-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    /// <summary>
    /// Adds people from a CSV file as confirmed subscribers. They get no confirmation email, so the admin must
    /// state that each of them asked to receive email; that statement is recorded in the audit trail. Anyone
    /// already on the list in any state is left exactly as they are: someone who unsubscribed stays unsubscribed.
    /// </summary>
    [HttpPost("import")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(Blog.Core.Services.SubscriberCsv.MaxFileBytes + 64 * 1024)]
    public async Task<IActionResult> Import(IFormFile? file, bool consent)
    {
        if (!consent)
        {
            TempData["Error"] = "Tick the box to confirm these people asked to receive email from you. Nothing was imported.";
            return RedirectToAction(nameof(Index));
        }
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Choose a CSV file to import.";
            return RedirectToAction(nameof(Index));
        }
        if (file.Length > Blog.Core.Services.SubscriberCsv.MaxFileBytes)
        {
            TempData["Error"] = "The file is larger than 1 MB. Split it into smaller files.";
            return RedirectToAction(nameof(Index));
        }

        string text;
        using (var reader = new StreamReader(file.OpenReadStream(), System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            text = await reader.ReadToEndAsync();
        var parsed = Blog.Core.Services.SubscriberCsv.Parse(text);
        if (parsed.Rows.Count == 0)
        {
            TempData["Error"] = "No email addresses were found in that file. It needs one address per row, in a column named email or as the first column.";
            return RedirectToAction(nameof(Index));
        }

        int added = 0, existing = 0;
        foreach (var row in parsed.Rows)
        {
            if (await _members.GetByEmailAsync(row.Email) is not null) { existing++; continue; }
            var imported = new Member { Email = row.Email, Name = row.Name, Status = "confirmed", Subscribed = true, ConfirmedAt = DateTime.UtcNow, Note = "Imported" };
            await _members.CreateAsync(imported);
            // People moved from another tool were welcomed there: they do not get the welcome sequence.
            await _members.SetWelcomeStepsSentAsync(imported.Id, Blog.Core.Services.WelcomeSequence.MaxSteps);
            added++;
        }

        // Counts only: addresses are personal data and the audit trail is permanent.
        await _audit.LogAsync(AuditActions.SubscriberImported, "Subscriber", null, $"{added} imported",
            newValues: $"The admin stated these people asked to receive email. Added {added}; already on the list {existing}; invalid {parsed.Invalid}; repeated in the file {parsed.Duplicates}.");
        TempData["Success"] = $"Imported {added} subscriber{(added == 1 ? "" : "s")}."
            + (existing > 0 ? $" {existing} were already on the list and were left as they are." : "")
            + (parsed.Invalid > 0 ? $" {parsed.Invalid} rows had no valid address." : "")
            + (parsed.Truncated ? $" Only the first {Blog.Core.Services.SubscriberCsv.MaxRows} were read; import the rest from a second file." : "");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, string? status, int page = 1)
    {
        await _members.DeleteAsync(id);
        // By id only: the address is personal data and the audit trail is permanent.
        await _audit.LogAsync(AuditActions.SubscriberDeleted, "Subscriber", id.ToString(), "Subscriber");
        TempData["Success"] = "Subscriber removed.";
        return RedirectToAction(nameof(Index), new { status, page });
    }
}
