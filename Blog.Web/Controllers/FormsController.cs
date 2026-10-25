using System.Text;
using System.Text.Json;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services.Forms;
using Blog.Web.Models;
using Blog.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

/// <summary>
/// Admin → Forms (ADR-0030): define contact forms and read what visitors sent. Submissions are personal data, so the
/// screens are Admin-only and every change is audited; the one exception, the list of forms the page builder
/// offers in its Form block, is FormOptionsController: an [Authorize] here would also apply to it.
/// </summary>
[Authorize(Policy = "AdminOnly")]
[Route("admin/forms")]
public class FormsController : Controller
{
    public const int SubmissionPageSize = 25;
    private readonly IFormRepository _forms;
    private readonly AuditService _audit;

    public FormsController(IFormRepository forms, AuditService audit)
    {
        _forms = forms;
        _audit = audit;
    }

    private Guid Owner => TrafficRecorder.OwnerOf(HttpContext);

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        var forms = await _forms.ListAsync(Owner);
        ViewData["Title"] = "Forms";
        ViewData["AllForms"] = forms.Count;
        return View(ListPaging.Apply(this, forms, q, page, f => new[] { f.Name, f.FormKey }));
    }

    [HttpGet("create")]
    public IActionResult Create()
    {
        ViewData["Title"] = "New Form";
        return View("Edit", FormEditModel.Starter());
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(FormEditModel model)
    {
        var (form, problems) = await ValidateAsync(model, null);
        if (problems.Count > 0) return Invalid(model, problems, "New Form");
        await _forms.CreateAsync(form);
        await _audit.LogAsync(AuditActions.FormCreated, "Form", form.Id.ToString(), form.Name, newValues: Summary(form));
        TempData["Success"] = $"Form saved. Add it to a page with the Form block.";
        return RedirectToAction(nameof(Edit), new { id = form.Id });
    }

    [HttpGet("edit/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id)
    {
        var form = await _forms.GetAsync(id, Owner);
        if (form is null) return NotFound();
        ViewData["Title"] = "Edit " + form.Name;
        return View("Edit", FormEditModel.From(form));
    }

    [HttpPost("edit/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, FormEditModel model)
    {
        var existing = await _forms.GetAsync(id, Owner);
        if (existing is null) return NotFound();
        model.Id = id;
        model.SubmissionCount = existing.SubmissionCount;
        // The key is how pages find the form; once anyone has sent it, changing it would break those pages silently.
        if (existing.SubmissionCount > 0) model.FormKey = existing.FormKey;
        var (form, problems) = await ValidateAsync(model, id);
        if (problems.Count > 0) return Invalid(model, problems, "Edit " + existing.Name);
        await _forms.UpdateAsync(form);
        await _audit.LogAsync(AuditActions.FormUpdated, "Form", id.ToString(), form.Name, oldValues: Summary(existing), newValues: Summary(form));
        TempData["Success"] = "Form saved.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, string? q, int page = 1)
    {
        var form = await _forms.GetAsync(id, Owner);
        if (form is null) return NotFound();
        await _forms.DeleteAsync(id, Owner);
        await _audit.LogAsync(AuditActions.FormDeleted, "Form", id.ToString(), form.Name,
            oldValues: Summary(form) + $"; {form.SubmissionCount} submissions deleted with it");
        TempData["Success"] = "Form deleted.";
        // Back to the same search and page (checklist 10); the pager clamps a page that no longer exists.
        return RedirectToAction(nameof(Index), new { q = string.IsNullOrWhiteSpace(q) ? null : q, page = page > 1 ? page : (int?)null });
    }

    // ── Submissions ───────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/submissions")]
    public async Task<IActionResult> Submissions(Guid id, string? q, int page = 1)
    {
        var form = await _forms.GetAsync(id, Owner);
        if (form is null) return NotFound();
        var total = await _forms.CountSubmissionsAsync(id, Owner, q);
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)SubmissionPageSize));
        page = Math.Clamp(page, 1, pages);
        var rows = await _forms.ListSubmissionsAsync(id, Owner, q, (page - 1) * SubmissionPageSize, SubmissionPageSize);
        ListPaging.Stash(this, PagingMeta.FromCounts(q, page, total, SubmissionPageSize, rows.Count));
        ViewData["Title"] = form.Name + " — Submissions";
        return View(new FormSubmissionsViewModel(form, FormRules.ParseFields(form.FieldsJson), rows));
    }

    [HttpGet("{id:guid}/submissions/{submissionId:long}")]
    public async Task<IActionResult> Submission(Guid id, long submissionId)
    {
        var form = await _forms.GetAsync(id, Owner);
        var submission = await _forms.GetSubmissionAsync(submissionId, Owner);
        if (form is null || submission is null || submission.FormId != id) return NotFound();
        await _forms.MarkReadAsync(submissionId, Owner, DateTime.UtcNow);
        ViewData["Title"] = "Submission";
        return View(new FormSubmissionViewModel(form, FormRules.ParseFields(form.FieldsJson), submission));
    }

    [HttpPost("{id:guid}/submissions/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSubmissions(Guid id, long[] ids, string? q, int page = 1)
    {
        var form = await _forms.GetAsync(id, Owner);
        if (form is null) return NotFound();
        var deleted = await _forms.DeleteSubmissionsAsync(id, Owner, ids ?? Array.Empty<long>());
        if (deleted > 0)
        {
            await _audit.LogAsync(AuditActions.FormSubmissionDeleted, "Form", id.ToString(), form.Name, oldValues: $"{deleted} submissions deleted");
            TempData["Success"] = deleted == 1 ? "Submission deleted." : $"{deleted} submissions deleted.";
        }
        else TempData["Error"] = "Select the submissions to delete first.";
        return RedirectToAction(nameof(Submissions), new { id, q, page });
    }

    /// <summary>Every submission of a form as CSV (UTF-8 with a byte-order mark, so spreadsheet apps read accents). Audited.</summary>
    [HttpPost("{id:guid}/submissions/export")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Export(Guid id)
    {
        var form = await _forms.GetAsync(id, Owner);
        if (form is null) return NotFound();
        var fields = FormRules.ParseFields(form.FieldsJson);
        var rows = await _forms.AllSubmissionsAsync(id, Owner);
        var csv = FormCsv.Write(fields, rows);
        await _audit.LogAsync(AuditActions.FormSubmissionExported, "Form", id.ToString(), form.Name, newValues: $"{rows.Count} submissions exported as CSV");
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", $"{form.FormKey}-submissions-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(ContactForm Form, IReadOnlyList<FormProblem> Problems)> ValidateAsync(FormEditModel m, Guid? id)
    {
        var key = string.IsNullOrWhiteSpace(m.FormKey) ? FormRules.MakeKey(m.Name) : m.FormKey.Trim().ToLowerInvariant();
        m.FormKey = key;
        List<FormField> fields;
        try { fields = JsonSerializer.Deserialize<List<FormField>>(m.FieldsJson ?? "[]", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(); }
        catch (JsonException) { fields = new(); }
        var (clean, problems) = FormRules.CleanDefinition(m.Name, key, fields, m.NotifyEmail, m.RetentionDays);
        var list = problems.ToList();
        if (!list.Any(p => p.Field == "FormKey") && await _forms.KeyExistsAsync(key, Owner, id))
            list.Add(new FormProblem("FormKey", "Another form already uses this address. Choose a different one."));
        var form = new ContactForm
        {
            Id = id ?? Guid.NewGuid(), OwnerId = Owner, FormKey = key, Name = (m.Name ?? "").Trim(),
            FieldsJson = FormRules.SerializeFields(clean),
            SubmitLabel = string.IsNullOrWhiteSpace(m.SubmitLabel) ? null : m.SubmitLabel.Trim(),
            SuccessMessage = string.IsNullOrWhiteSpace(m.SuccessMessage) ? null : m.SuccessMessage.Trim(),
            NotifyEmail = string.IsNullOrWhiteSpace(m.NotifyEmail) ? null : m.NotifyEmail.Trim(),
            RetentionDays = m.RetentionDays, IsActive = m.IsActive,
        };
        if (list.Count == 0) m.FieldsJson = form.FieldsJson;
        return (form, list);
    }

    private IActionResult Invalid(FormEditModel model, IReadOnlyList<FormProblem> problems, string title)
    {
        foreach (var p in problems) ModelState.AddModelError(p.Field ?? "", p.Message);
        ViewData["Title"] = title;
        return View("Edit", model);
    }

    private static string Summary(ContactForm f) =>
        $"key {f.FormKey}; {FormRules.ParseFields(f.FieldsJson).Count} fields; {(f.IsActive ? "active" : "off")}; " +
        $"notify {(string.IsNullOrEmpty(f.NotifyEmail) ? "nobody" : f.NotifyEmail)}; keep {(f.RetentionDays == 0 ? "until deleted" : f.RetentionDays + " days")}";
}

/// <summary>The form editor's fields. <see cref="FieldsJson"/> is written by the editor's field list.</summary>
public sealed class FormEditModel
{
    public Guid? Id { get; set; }
    public string? Name { get; set; }
    public string? FormKey { get; set; }
    public string? FieldsJson { get; set; }
    public string? SubmitLabel { get; set; }
    public string? SuccessMessage { get; set; }
    public string? NotifyEmail { get; set; }
    public int RetentionDays { get; set; } = 365;
    public bool IsActive { get; set; } = true;
    public int SubmissionCount { get; set; }

    /// <summary>A new form starts as a typical contact form, so the owner edits rather than builds from nothing.</summary>
    public static FormEditModel Starter() => new()
    {
        Name = "Contact us",
        FieldsJson = FormRules.SerializeFields(new[]
        {
            new FormField { Key = "name", Label = "Your name", Type = FormFieldTypes.Name, Required = true },
            new FormField { Key = "email", Label = "Email address", Type = FormFieldTypes.Email, Required = true },
            new FormField { Key = "message", Label = "Message", Type = FormFieldTypes.LongText, Required = true },
        }),
        SubmitLabel = "Send message",
        SuccessMessage = "Thank you. Your message has been sent, and we will reply by email.",
    };

    public static FormEditModel From(ContactForm f) => new()
    {
        Id = f.Id, Name = f.Name, FormKey = f.FormKey, FieldsJson = f.FieldsJson, SubmitLabel = f.SubmitLabel,
        SuccessMessage = f.SuccessMessage, NotifyEmail = f.NotifyEmail, RetentionDays = f.RetentionDays, IsActive = f.IsActive,
        SubmissionCount = f.SubmissionCount,
    };
}

public sealed record FormSubmissionsViewModel(ContactForm Form, IReadOnlyList<FormField> Fields, IReadOnlyList<FormSubmission> Rows);
public sealed record FormSubmissionViewModel(ContactForm Form, IReadOnlyList<FormField> Fields, FormSubmission Submission);
