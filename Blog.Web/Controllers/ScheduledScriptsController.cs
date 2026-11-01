using System.Text.Json;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Services;
using Blog.Web.Services.Jobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

/// <summary>
/// Admin → Scheduled scripts. An Admin schedules SQL script files that already sit in the server's
/// scripts folder (hourly, daily or weekly, UTC), enables or disables them, runs one now, and reads the
/// last output. The Desk never accepts SQL text: what can run is decided by whoever can write to the
/// server's file system, which is the operator, not a Desk account. Schedules fixed in configuration
/// are listed read-only so an operator can pin housekeeping that no Desk user may remove.
/// </summary>
[Authorize(Policy = "AdminOnly")]
[Route("admin/scheduled-scripts")]
public class ScheduledScriptsController : Controller
{
    private readonly IScheduledScriptRepository _scripts;
    private readonly ScheduledScriptRunner _runner;
    private readonly AuditService _audit;

    public ScheduledScriptsController(IScheduledScriptRepository scripts, ScheduledScriptRunner runner, AuditService audit)
    {
        _scripts = scripts; _runner = runner; _audit = audit;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, int page = 1, Guid? edit = null)
    {
        var all = await _scripts.GetAllAsync();
        ViewBag.Scripts = Blog.Web.Models.ListPaging.Apply(this, all, q, page, s => new[] { s.Name, s.ScriptFile, s.Frequency });
        ViewData["ListSearchPlaceholder"] = "Search schedules by name or file";
        ViewBag.Files = _runner.AvailableFiles();
        ViewBag.Folder = _runner.FolderPath;
        ViewBag.Editing = edit is null ? null : all.FirstOrDefault(s => s.Id == edit && !s.IsFromConfig);
        ViewBag.Total = all.Count;
        ViewData["Title"] = "Scheduled Scripts";
        return View();
    }

    [HttpPost("save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(Guid? id, string name, string scriptFile, string frequency, int? dayOfWeek, string? timeUtc, bool enabled = true)
    {
        name = (name ?? "").Trim(); scriptFile = (scriptFile ?? "").Trim(); frequency = (frequency ?? "").Trim().ToLowerInvariant();
        if (!TimeSpan.TryParse(timeUtc ?? "", out var time)) time = TimeSpan.Zero;

        var error = ScriptSchedule.Validate(name, scriptFile, frequency, dayOfWeek, time);
        if (error is null && !_runner.FileExists(scriptFile)) error = $"{scriptFile} is not in the scripts folder ({_runner.FolderPath}). Copy the file there first.";
        var clash = await _scripts.GetByNameAsync(name);
        if (error is null && clash is not null && clash.Id != id) error = $"A schedule named \"{name}\" already exists.";
        if (error is not null) { TempData["Error"] = error; return RedirectToAction(nameof(Index), new { edit = id }); }

        var existing = id is null ? null : await _scripts.GetByIdAsync(id.Value);
        if (existing is not null && existing.IsFromConfig)
        {
            TempData["Error"] = "That schedule is fixed in the server configuration and cannot be edited here.";
            return RedirectToAction(nameof(Index));
        }

        var script = existing ?? new ScheduledScript();
        var before = existing is null ? null : JsonSerializer.Serialize(new { existing.Name, existing.ScriptFile, existing.Frequency, existing.DayOfWeek, existing.TimeOfDayUtc, existing.Enabled });
        script.Name = name; script.ScriptFile = scriptFile; script.Frequency = frequency;
        script.DayOfWeek = frequency == ScriptFrequencies.Weekly ? dayOfWeek : null;
        script.TimeOfDayUtc = time; script.Enabled = enabled; script.Source = ScriptSources.Desk;
        script.NextRunAt = ScriptSchedule.NextRun(script, DateTime.UtcNow);
        await _scripts.UpsertAsync(script);

        await _audit.LogAsync(existing is null ? AuditActions.ScheduledScriptCreated : AuditActions.ScheduledScriptUpdated,
            "ScheduledScript", script.Id.ToString(), script.Name, oldValues: before,
            newValues: JsonSerializer.Serialize(new { script.Name, script.ScriptFile, script.Frequency, script.DayOfWeek, script.TimeOfDayUtc, script.Enabled }));

        TempData["Success"] = $"{script.Name} saved. Next run {script.NextRunAt:yyyy-MM-dd HH:mm} UTC.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(Guid id)
    {
        var script = await _scripts.GetByIdAsync(id);
        if (script is null) { TempData["Error"] = "Schedule not found."; return RedirectToAction(nameof(Index)); }
        if (script.IsFromConfig) { TempData["Error"] = "That schedule is fixed in the server configuration."; return RedirectToAction(nameof(Index)); }

        script.Enabled = !script.Enabled;
        script.NextRunAt = script.Enabled ? ScriptSchedule.NextRun(script, DateTime.UtcNow) : null;
        await _scripts.UpsertAsync(script);
        await _audit.LogAsync(script.Enabled ? AuditActions.ScheduledScriptEnabled : AuditActions.ScheduledScriptDisabled,
            "ScheduledScript", script.Id.ToString(), script.Name);
        TempData["Success"] = script.Enabled ? $"{script.Name} enabled. Next run {script.NextRunAt:yyyy-MM-dd HH:mm} UTC." : $"{script.Name} disabled.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("run")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunNow(Guid id)
    {
        var script = await _scripts.GetByIdAsync(id);
        if (script is null) { TempData["Error"] = "Schedule not found."; return RedirectToAction(nameof(Index)); }

        var result = await _runner.RunAsync(script, HttpContext.RequestAborted);
        var next = script.Enabled ? ScriptSchedule.NextRun(script, DateTime.UtcNow) : script.NextRunAt ?? DateTime.MaxValue;
        await _scripts.RecordRunAsync(script.Id, DateTime.UtcNow, result.Succeeded, result.Output, result.DurationMs, next);
        await _audit.LogAsync(result.Succeeded ? AuditActions.ScheduledScriptRan : AuditActions.ScheduledScriptFailed,
            "ScheduledScript", script.Id.ToString(), script.Name,
            newValues: JsonSerializer.Serialize(new { script.ScriptFile, result.DurationMs, Trigger = "manual" }));

        if (result.Succeeded) TempData["Success"] = $"{script.Name} ran in {result.DurationMs / 1000.0:n1} s. The output is in its row.";
        else TempData["Error"] = $"{script.Name} failed. The output in its row says where.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var script = await _scripts.GetByIdAsync(id);
        if (script is null) { TempData["Error"] = "Schedule not found."; return RedirectToAction(nameof(Index)); }
        if (script.IsFromConfig) { TempData["Error"] = "That schedule is fixed in the server configuration and cannot be deleted here."; return RedirectToAction(nameof(Index)); }

        await _scripts.DeleteAsync(id);
        await _audit.LogAsync(AuditActions.ScheduledScriptDeleted, "ScheduledScript", script.Id.ToString(), script.Name,
            oldValues: JsonSerializer.Serialize(new { script.ScriptFile, script.Frequency, script.DayOfWeek, script.TimeOfDayUtc }));
        TempData["Success"] = $"{script.Name} removed. The file stays in the scripts folder.";
        return RedirectToAction(nameof(Index));
    }
}
