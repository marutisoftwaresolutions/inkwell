using Blog.Core.Interfaces;
using Blog.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.ViewComponents;

public class NavbarViewComponent : ViewComponent
{
    private readonly ISettingRepository _settings;
    private readonly IPageRepository _pages;
    private readonly ICategoryRepository _categories;
    private readonly ICustomThemeSettingRepository _themeSettings;

    public NavbarViewComponent(
        ISettingRepository settings,
        IPageRepository pages,
        ICategoryRepository categories,
        ICustomThemeSettingRepository themeSettings)
    {
        _settings = settings;
        _pages = pages;
        _categories = categories;
        _themeSettings = themeSettings;
    }

    public async Task<IViewComponentResult> InvokeAsync(Guid ownerId)
    {
        var navPages = await _pages.GetNavPagesAsync();
        var userSettings = await _settings.GetSettingsAsync(ownerId);
        var categories = await _categories.GetAllAsync(ownerId);

        var model = new NavbarViewModel
        {
            SiteName = userSettings.SiteName,
            Tagline = userSettings.SiteDescription,
            SiteLogoUrl = userSettings.SiteLogoUrl,
            Pages = navPages,
            Categories = categories
        };

        // Select layout variant — only render views that actually exist
        var supportedNavbarLayouts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Neutral", "Magazine", "Grid", "Minimal", "Classic", "Modern", "Feed" };
        var layoutSetting = await _themeSettings.GetByKeyAsync(ownerId, "layout-navbar");
        var raw = layoutSetting?.EffectiveValue ?? "Neutral";
        var layout = supportedNavbarLayouts.Contains(raw) ? raw : "Neutral";

        return View(layout, model);
    }
}
