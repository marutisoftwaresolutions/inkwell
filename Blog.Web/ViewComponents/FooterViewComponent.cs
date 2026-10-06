using Blog.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.ViewComponents;

public class FooterViewComponent : ViewComponent
{
    private readonly ICustomThemeSettingRepository _themeSettings;
    private readonly Blog.Web.Services.MenuService _menus;

    public FooterViewComponent(ICustomThemeSettingRepository themeSettings, Blog.Web.Services.MenuService menus)
    {
        _themeSettings = themeSettings;
        _menus = menus;
    }

    private static readonly HashSet<string> _supportedFooterLayouts =
        new(StringComparer.OrdinalIgnoreCase) { "Neutral", "Magazine", "Grid", "Minimal", "Classic", "Modern" };

    public async Task<IViewComponentResult> InvokeAsync(Guid ownerId)
    {
        var layoutSetting = await _themeSettings.GetByKeyAsync(ownerId, "layout-footer");
        var raw = layoutSetting?.EffectiveValue ?? "Neutral";
        var layout = _supportedFooterLayouts.Contains(raw) ? raw : "Neutral";

        ViewBag.OwnerId = ownerId;
        // Footer menu from Admin → Menus; empty (renders nothing, as before menus existed) when there is none.
        ViewBag.FooterLinks = await _menus.GetLinksAsync(ownerId, Blog.Core.Domain.MenuLocations.Footer);
        return View(layout);
    }
}
