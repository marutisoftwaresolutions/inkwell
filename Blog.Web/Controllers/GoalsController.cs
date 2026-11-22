using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

/// <summary>
/// Admin → Analytics → Goals: what the site wants readers to do, counted per day. Two conversions are
/// counted for every site (newsletter sign-ups, form submissions); an Admin adds page-visit and
/// link-click goals. Adding and removing a goal are audited. Not offered in cloud mode's shared Desk
/// beyond the tenant's own goals: the list and the counts are per owner.
/// </summary>
[Authorize(Policy = "AdminOnly")]
[Route("admin/analytics/goals")]
public class GoalsController : Controller
{
    private readonly ISettingRepository _settings;
    private readonly IUserRepository _users;
    private readonly IGoalCountRepository _counts;
    private readonly ITenantContext _tenant;
    private readonly GoalRecorder _recorder;
    private readonly AuditService _audit;

    public GoalsController(ISettingRepository settings, IUserRepository users, IGoalCountRepository counts,
        ITenantContext tenant, GoalRecorder recorder, AuditService audit)
    {
        _settings = settings;
        _users = users;
        _counts = counts;
        _tenant = tenant;
        _recorder = recorder;
        _audit = audit;
    }

    public sealed record Row(string Key, string Name, string What, bool BuiltIn, int Last7, int Last30, int? InRange = null);

    /// <summary>The key counts are stored under: the tenant in cloud mode, otherwise the whole site.</summary>
    private Guid CountOwner => _tenant is { IsCloudMode: true, IsResolved: true } ? _tenant.UserId : Guid.Empty;

    /// <summary>Whose settings hold the goals: the tenant in cloud mode, otherwise the first Admin (the site's settings).</summary>
    private async Task<Guid> SettingsOwnerAsync() =>
        _tenant is { IsCloudMode: true, IsResolved: true } ? _tenant.UserId : (await _users.GetFirstAdminAsync())?.Id ?? Guid.Empty;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? from, string? to)
    {
        var site = await _settings.GetSettingsAsync(await SettingsOwnerAsync());
        var goals = Goals.Normalize(site.Goals);

        var today = DateTime.UtcNow.Date;
        Dictionary<string, int> week = new(), month = new();
        Dictionary<string, int>? chosen = null;
        var range = Goals.Range(from, to, today);
        if (range is null && (!string.IsNullOrWhiteSpace(from) || !string.IsNullOrWhiteSpace(to)))
            TempData["Error"] = "Choose both dates to see a range of your own.";
        try
        {
            await _recorder.FlushAsync();   // so a click made a moment ago is in the numbers
            week = await _counts.GetTotalsAsync(CountOwner, today.AddDays(-6), today);
            month = await _counts.GetTotalsAsync(CountOwner, today.AddDays(-29), today);
            if (range is { } r) chosen = await _counts.GetTotalsAsync(CountOwner, r.From, r.To);
        }
        catch { TempData["Error"] = "The counts could not be read just now. The goals themselves are listed below."; }

        var rows = Goals.BuiltIn.Select(b => new Row(b.Key, b.Name, b.Meaning, true, week.GetValueOrDefault(b.Key), month.GetValueOrDefault(b.Key), chosen?.GetValueOrDefault(b.Key)))
            .Concat(goals.Select(g => new Row(g.Key, g.Name,
                g.Kind == Goals.Visit ? $"A reader opened {g.Target}" : $"A reader followed a link starting with {g.Target}",
                false, week.GetValueOrDefault(g.Key), month.GetValueOrDefault(g.Key), chosen?.GetValueOrDefault(g.Key))))
            .ToList();

        // The range as it was applied (dates may have been swapped or shortened), for the inputs and the column heading.
        ViewBag.RangeFrom = range?.From.ToString("yyyy-MM-dd");
        ViewBag.RangeTo = range?.To.ToString("yyyy-MM-dd");
        ViewBag.Today = today.ToString("yyyy-MM-dd");

        ViewBag.CanAdd = goals.Count < Goals.Max;
        ViewData["Title"] = "Goals";
        return View(rows);
    }

    /// <summary>Daily counts as CSV: the chosen range, or the last 30 days. Totals only, so there is nothing personal in the file.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(string? from, string? to)
    {
        var today = DateTime.UtcNow.Date;
        var (start, end) = Goals.Range(from, to, today) ?? (today.AddDays(-29), today);
        var site = await _settings.GetSettingsAsync(await SettingsOwnerAsync());
        var names = Goals.BuiltIn.ToDictionary(b => b.Key, b => b.Name, StringComparer.Ordinal);
        foreach (var g in Goals.Normalize(site.Goals)) names[g.Key] = g.Name;

        List<GoalHit> daily;
        try
        {
            await _recorder.FlushAsync();
            daily = await _counts.GetDailyAsync(CountOwner, start, end);
        }
        catch
        {
            TempData["Error"] = "The counts could not be read just now, so there is nothing to download. Try again in a moment.";
            return RedirectToAction(nameof(Index), new { from, to });
        }
        var bytes = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(Goals.Csv(daily, names))).ToArray();
        return File(bytes, "text/csv", $"goals-{start:yyyy-MM-dd}-to-{end:yyyy-MM-dd}.csv");
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(string? name, string? kind, string? target)
    {
        var owner = await SettingsOwnerAsync();
        var site = await _settings.GetSettingsAsync(owner);
        var goals = Goals.Normalize(site.Goals);

        var problem = Goals.Problem(name, kind, target, goals.Count);
        if (problem is not null)
        {
            TempData["Error"] = problem;
            // Which field the reason belongs to, so the form can mark it and point at it.
            var nameAtFault = string.IsNullOrWhiteSpace(name) || (name?.Trim().Length ?? 0) > Goals.MaxNameLength;
            TempData["GoalErrorField"] = goals.Count >= Goals.Max ? "" : nameAtFault ? "goalName" : kind != Goals.Visit && kind != Goals.Click ? "goalKind" : "goalTarget";
            TempData["GoalErrorText"] = problem;
            TempData["GoalName"] = name; TempData["GoalKind"] = kind; TempData["GoalTarget"] = target;
            return RedirectToAction(nameof(Index));
        }

        var goal = new Goal { Key = Goals.KeyFor(name, goals.Select(g => g.Key)), Name = name!.Trim(), Kind = kind!, Target = Goals.CleanTarget(kind, target)! };
        goals.Add(goal);
        site.Goals = Goals.Normalize(goals);
        await _settings.SaveSettingsAsync(owner, site);
        _recorder.Forget(CountOwner);

        await _audit.LogAsync(AuditActions.GoalAdded, "Goal", goal.Key, goal.Name, newValues: $"{goal.Kind}: {goal.Target}");
        TempData["Success"] = "Goal added. Counting starts now; it can take a minute to reach every page.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("delete/{key}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string key)
    {
        var owner = await SettingsOwnerAsync();
        var site = await _settings.GetSettingsAsync(owner);
        var goals = Goals.Normalize(site.Goals);
        var goal = goals.FirstOrDefault(g => g.Key == key);
        if (goal is null) return NotFound();

        goals.Remove(goal);
        site.Goals = goals;
        await _settings.SaveSettingsAsync(owner, site);
        _recorder.Forget(CountOwner);
        await _recorder.FlushAsync();   // write what is still in memory first, so the delete below takes it too
        var removed = await _counts.DeleteGoalAsync(CountOwner, goal.Key);

        await _audit.LogAsync(AuditActions.GoalDeleted, "Goal", goal.Key, goal.Name, oldValues: $"{goal.Kind}: {goal.Target}", newValues: $"{removed} day(s) of counts removed");
        TempData["Success"] = "Goal removed.";
        return RedirectToAction(nameof(Index));
    }
}
