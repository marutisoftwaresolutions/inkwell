using System.Security.Claims;
using Blog.Core.Domain;
using Blog.Core.Services;
using Blog.Core.Interfaces;
using Blog.Web.Services;
using Blog.Web.Services.Import;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

// Bulk creation of posts/pages/media is admin-tier → gated on the existing AdminOnly policy
// (no new permission claim, so existing installs need no RBAC re-seed or re-login).
[Authorize(Policy = "AdminOnly")]
[Route("admin/import")]
public class ImportController : Controller
{
    private const int BatchSize = 5;

    private readonly IImportJobRepository _jobs;
    private readonly IEnumerable<IContentImporter> _importers;
    private readonly ImportProcessor _processor;
    private readonly AuditService _audit;
    private readonly ILogger<ImportController> _logger;

    public ImportController(IImportJobRepository jobs, IEnumerable<IContentImporter> importers,
        ImportProcessor processor, AuditService audit, ILogger<ImportController> logger)
    {
        _jobs = jobs;
        _importers = importers;
        _processor = processor;
        _audit = audit;
        _logger = logger;
    }

    private Guid CurrentUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static string ImportsTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "inkwell-imports");
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ── Stage 0: job list + start ─────────────────────────────────────────────
    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        var jobs = await _jobs.GetJobsAsync(CurrentUserId());
        ViewData["ListSearchPlaceholder"] = "Search imports by file, source or status";
        return View(Blog.Web.Models.ListPaging.Apply(this, jobs, q, page, j => new[] { j.FileName, j.Source.ToString(), j.Status.ToString() }));
    }

    // ── Stage 1: upload + analyze ─────────────────────────────────────────────
    [HttpPost("upload")]
    [ValidateAntiForgeryToken]
    [RequestFormLimits(MultipartBodyLengthLimit = 524288000)] // 500 MB
    [RequestSizeLimit(524288000)]
    public async Task<IActionResult> Upload([FromForm] IFormFile? file, [FromForm] string source = "WordPress")
    {
        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "Please choose an export file to upload.";
            return RedirectToAction("Index");
        }
        if (!Enum.TryParse<ImportSource>(source, ignoreCase: true, out var src))
        {
            TempData["Error"] = "Unsupported import source.";
            return RedirectToAction("Index");
        }
        var importer = _importers.FirstOrDefault(i => i.Source == src);
        if (importer == null)
        {
            TempData["Error"] = $"{src} import is not available yet.";
            return RedirectToAction("Index");
        }

        // Save to a quarantined temp file.
        var savedPath = Path.Combine(ImportsTempDir(), $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}");
        await using (var fs = new FileStream(savedPath, FileMode.Create))
            await file.CopyToAsync(fs);

        var job = new ImportJob
        {
            OwnerId = CurrentUserId(),
            Source = src,
            FileName = file.FileName,
            FilePath = savedPath,
            Status = ImportJobStatus.Draft
        };
        job.Id = await _jobs.CreateJobAsync(job);

        try
        {
            importer.Analyze(savedPath); // validates the file (throws on an invalid export)

            var items = new List<ImportItem>();
            foreach (var rec in importer.ReadRecords(savedPath))
            {
                items.Add(new ImportItem
                {
                    JobId = job.Id,
                    ItemType = rec.Type,
                    SourceId = Truncate(rec.SourceId, 450),
                    Title = Truncate(rec.Title, 1000),
                    Ordinal = rec.Ordinal,
                    Status = ImportItemStatus.Pending,
                    DataJson = System.Text.Json.JsonSerializer.Serialize(rec.Payload, rec.Payload.GetType())
                });
                if (items.Count >= 500) { await _jobs.AddItemsAsync(items); items.Clear(); }
            }
            if (items.Count > 0) await _jobs.AddItemsAsync(items);

            job.Status = ImportJobStatus.Analyzed;
            await _jobs.UpdateJobAsync(job);
            await _jobs.RecomputeJobCountsAsync(job.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Import analyze failed for job {Job}", job.Id);
            job.Status = ImportJobStatus.Failed;
            await _jobs.UpdateJobAsync(job);
            TryDeleteFile(savedPath);
            TempData["Error"] = $"Could not read the export file: {ex.Message}";
            return RedirectToAction("Index");
        }

        return RedirectToAction("Configure", new { id = job.Id });
    }

    // ── Stage 2/3: preview + select ──────────────────────────────────────────
    [HttpGet("configure/{id}")]
    public async Task<IActionResult> Configure(Guid id)
    {
        var job = await _jobs.GetJobAsync(id, CurrentUserId());
        if (job == null) return NotFound();
        ViewBag.Counts = await _jobs.GetTypeCountsAsync(id);
        ViewBag.DefaultAuthorId = CurrentUserId();
        return View(job);
    }

    [HttpPost("configure/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Configure(Guid id, ImportOptions options, string? action = null)
    {
        var job = await _jobs.GetJobAsync(id, CurrentUserId());
        if (job == null) return NotFound();

        // Attribute imported posts to the current admin (guarantees public visibility under owner scoping).
        options.DefaultAuthorId = CurrentUserId();
        job.OptionsJson = System.Text.Json.JsonSerializer.Serialize(options);

        // Preview saves the options and reports; only the run marks the job ready and audits a start.
        if (string.Equals(action, "preview", StringComparison.OrdinalIgnoreCase))
        {
            await _jobs.UpdateJobAsync(job);
            return RedirectToAction("Preview", new { id });
        }

        job.Status = ImportJobStatus.Analyzed;
        await _jobs.UpdateJobAsync(job);
        await _audit.LogAsync(AuditActions.ImportStarted, "Import", id.ToString(), $"{job.Source} — {job.FileName}");
        return RedirectToAction("Run", new { id });
    }

    /// <summary>
    /// Predicts the outcome of the import without writing anything. Deliberately read-only: it uses
    /// the analyzer rather than the processor, so a preview has no code path that could persist.
    /// </summary>
    [HttpGet("preview/{id}")]
    public async Task<IActionResult> Preview(Guid id)
    {
        var job = await _jobs.GetJobAsync(id, CurrentUserId());
        if (job == null) return NotFound();

        var items = await _jobs.GetItemsAsync(id);
        var staged = new List<StagedItem>();

        foreach (var item in items)
        {
            string? slug = null, html = null;
            var published = true;

            if (item.ItemType is ImportItemType.Post or ImportItemType.Page && !string.IsNullOrWhiteSpace(item.DataJson))
            {
                try
                {
                    var parsed = System.Text.Json.JsonSerializer.Deserialize<ParsedPost>(item.DataJson!,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    slug = parsed?.Slug;
                    html = parsed?.Html;
                    published = string.Equals(parsed?.Status ?? "publish", "publish", StringComparison.OrdinalIgnoreCase);
                }
                catch (System.Text.Json.JsonException)
                {
                    // A staged item we cannot read is reported as-is rather than silently dropped.
                }
            }

            staged.Add(new StagedItem(item.ItemType, item.Title ?? "(untitled)", slug, html, published));
        }

        var existing = await _jobs.GetExistingSlugsAsync();
        ViewBag.Preview = ImportPreviewAnalyzer.Analyze(staged, job.Options, existing);
        return View(job);
    }

    // ── Stage 4: run (batched) ───────────────────────────────────────────────
    [HttpGet("run/{id}")]
    public async Task<IActionResult> Run(Guid id)
    {
        var job = await _jobs.GetJobAsync(id, CurrentUserId());
        if (job == null) return NotFound();
        return View(job);
    }

    /// <summary>
    /// The preview's "Run the import" is a confirmed POST (typed OVERWRITE when anything would be
    /// replaced), so the step past the prediction is deliberate and anti-forgery-protected. Nothing
    /// is written here: the batches start from the Run page.
    /// </summary>
    [HttpPost("run/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunConfirmed(Guid id)
    {
        var job = await _jobs.GetJobAsync(id, CurrentUserId());
        if (job == null) return NotFound();
        return RedirectToAction(nameof(Run), new { id });
    }

    [HttpPost("process-batch/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessBatch(Guid id)
    {
        var job = await _jobs.GetJobAsync(id, CurrentUserId());
        if (job == null) return NotFound();
        if (job.Status == ImportJobStatus.Completed || job.Status == ImportJobStatus.Cancelled)
            return Json(new { done = true, status = job.Status.ToString(), total = job.TotalItems, imported = job.ImportedItems, failed = job.FailedItems, skipped = job.SkippedItems, errors = Array.Empty<string>() });

        if (job.Status != ImportJobStatus.Running)
        {
            job.Status = ImportJobStatus.Running;
            await _jobs.UpdateJobAsync(job);
        }

        var result = await _processor.ProcessBatchAsync(job, BatchSize);

        // Refresh counters from the DB (RecomputeJobCountsAsync ran inside the batch).
        var refreshed = await _jobs.GetJobAsync(id, CurrentUserId()) ?? job;

        if (!result.HasMore)
        {
            refreshed.Status = ImportJobStatus.Completed;
            refreshed.CompletedAt = DateTime.UtcNow;
            await _jobs.UpdateJobAsync(refreshed);
            TryDeleteFile(refreshed.FilePath);
            await _audit.LogAsync(AuditActions.ImportCompleted, "Import", id.ToString(),
                $"{refreshed.Source}: {refreshed.ImportedItems} imported, {refreshed.FailedItems} failed, {refreshed.SkippedItems} skipped");
        }

        return Json(new
        {
            done = !result.HasMore,
            status = refreshed.Status.ToString(),
            total = refreshed.TotalItems,
            imported = refreshed.ImportedItems,
            failed = refreshed.FailedItems,
            skipped = refreshed.SkippedItems,
            processed = result.Processed,
            errors = result.Errors
        });
    }

    // ── Stage 5: summary + report ────────────────────────────────────────────
    [HttpGet("summary/{id}")]
    public async Task<IActionResult> Summary(Guid id)
    {
        var job = await _jobs.GetJobAsync(id, CurrentUserId());
        if (job == null) return NotFound();
        ViewBag.Failed = await _jobs.GetItemsAsync(id, ImportItemStatus.Failed);
        return View(job);
    }

    [HttpGet("report/{id}")]
    public async Task<IActionResult> Report(Guid id)
    {
        var job = await _jobs.GetJobAsync(id, CurrentUserId());
        if (job == null) return NotFound();
        var items = await _jobs.GetItemsAsync(id);

        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("Import Report");
        ws.Cell(1, 1).Value = "Type";
        ws.Cell(1, 2).Value = "Title";
        ws.Cell(1, 3).Value = "Status";
        ws.Cell(1, 4).Value = "Error";
        ws.Row(1).Style.Font.Bold = true;
        var r = 2;
        foreach (var it in items)
        {
            ws.Cell(r, 1).Value = it.ItemType.ToString();
            ws.Cell(r, 2).Value = it.Title ?? "";
            ws.Cell(r, 3).Value = it.Status.ToString();
            ws.Cell(r, 4).Value = it.Error ?? "";
            r++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"import-report-{id:N}.xlsx");
    }

    [HttpPost("cancel/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var job = await _jobs.GetJobAsync(id, CurrentUserId());
        if (job == null) return NotFound();
        job.Status = ImportJobStatus.Cancelled;
        await _jobs.UpdateJobAsync(job);
        TryDeleteFile(job.FilePath);
        await _audit.LogAsync(AuditActions.ImportCancelled, "Import", id.ToString(), job.FileName);
        TempData["Success"] = "Import cancelled.";
        return RedirectToAction("Index");
    }

    // Rollback: remove the posts/pages/images/comments this import created (taxonomy is kept).
    [HttpPost("undo/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Undo(Guid id)
    {
        var job = await _jobs.GetJobAsync(id, CurrentUserId());
        if (job == null) return NotFound();
        if (string.Equals(job.Options.SlugConflict, "Overwrite", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Undo isn't available for imports that overwrote existing content (originals can't be restored).";
            return RedirectToAction("Summary", new { id });
        }
        var removed = await _processor.UndoAsync(job);
        job.Status = ImportJobStatus.Cancelled;
        await _jobs.UpdateJobAsync(job);
        await _audit.LogAsync(AuditActions.ImportCancelled, "Import", id.ToString(), $"Undo — {removed} items removed");
        TempData["Success"] = $"Undo complete — {removed} imported item(s) removed (categories and tags were kept).";
        return RedirectToAction("Index");
    }

    [HttpPost("delete/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var job = await _jobs.GetJobAsync(id, CurrentUserId());
        if (job != null) TryDeleteFile(job.FilePath);
        await _jobs.DeleteJobAsync(id, CurrentUserId());
        TempData["Success"] = "Import record deleted (imported content is kept).";
        return RedirectToAction("Index");
    }

    private void TryDeleteFile(string? path)
    {
        try { if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not delete import temp file {Path}", path); }
    }

    private static string? Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);
}
