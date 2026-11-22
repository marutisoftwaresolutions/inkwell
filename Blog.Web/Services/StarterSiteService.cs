using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Controllers;

namespace Blog.Web.Services;

/// <summary>
/// Applies a starter site (<see cref="StarterSites"/>) for a new owner: its layout and colour preset,
/// and its pages. It only ever adds. A page whose slug already exists is left exactly as it is, and
/// the theme is set only when the owner has not chosen a layout yet, so running it on a site that
/// already has content changes nothing the owner made.
/// </summary>
public sealed class StarterSiteService
{
    /// <summary>The same navbar and footer fallbacks the Theme screen uses for layouts without their own.</summary>
    private static readonly Dictionary<string, string> ChromeFor = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Newsletter"] = "Minimal", ["Showcase"] = "Modern",
    };

    private readonly IPageRepository _pages;
    private readonly ICustomThemeSettingRepository _theme;

    public StarterSiteService(IPageRepository pages, ICustomThemeSettingRepository theme)
    {
        _pages = pages;
        _theme = theme;
    }

    public sealed record Outcome(int PagesCreated, int PagesSkipped, bool ThemeApplied);

    public async Task<Outcome> ApplyAsync(Guid ownerId, string? key)
    {
        var starter = StarterSites.Find(key);
        if (starter is null) return new Outcome(0, 0, false);

        var created = 0;
        var skipped = 0;
        foreach (var page in starter.Pages)
        {
            if (await _pages.GetBySlugAsync(page.Slug) is not null || await _pages.SlugExistsAsync(page.Slug, ownerId))
            {
                skipped++;   // never overwrite what is already there
                continue;
            }
            await _pages.CreateAsync(new Page
            {
                Title = page.Title,
                Slug = page.Slug,
                Content = page.Html,
                MetaDescription = page.MetaDescription,
                AuthorId = ownerId,
                IsPublished = true,
                IsInNav = true,
                SortOrder = page.SortOrder,
                PublishedAt = DateTime.UtcNow,
            });
            created++;
        }

        return new Outcome(created, skipped, await ApplyThemeAsync(ownerId, starter));
    }

    private async Task<bool> ApplyThemeAsync(Guid ownerId, StarterSites.Starter starter)
    {
        var existing = await _theme.GetAllAsync(ownerId);
        // An owner who already has theme rows has been to the Theme screen: their choice stands.
        if (existing.Count > 0) return false;
        if (!StarterSites.KnownLayouts.Contains(starter.Layout) || !StarterSites.KnownPresets.Contains(starter.Preset)) return false;

        await _theme.SeedDefaultsAsync(ownerId, ThemeSettingsController.GetDefaultSettings());
        var chrome = ChromeFor.TryGetValue(starter.Layout, out var mapped) ? mapped : starter.Layout;
        await _theme.SaveAllAsync(ownerId,
        [
            new() { SettingKey = "layout-navbar", SettingValue = chrome },
            new() { SettingKey = "layout-footer", SettingValue = chrome },
            new() { SettingKey = "layout-index", SettingValue = starter.Layout },
            new() { SettingKey = "layout-postcard", SettingValue = starter.Layout },
            new() { SettingKey = "layout-post", SettingValue = starter.Layout },
            new() { SettingKey = "layout-page", SettingValue = starter.Layout },
            new() { SettingKey = "blog-layout", SettingValue = starter.Layout },
            new() { SettingKey = "inkwell-preset", SettingValue = starter.Preset },
        ]);
        return true;
    }
}
