using Blog.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.ViewComponents;

public class FooterViewComponent : ViewComponent
{
    private readonly ICustomThemeSettingRepository _themeSettings;

    public FooterViewComponent(ICustomThemeSettingRepository themeSettings)
    {
        _themeSettings = themeSettings;
    }

    private static readonly HashSet<string> _supportedFooterLayouts =
        new(StringComparer.OrdinalIgnoreCase) { "Neutral", "Magazine", "Grid", "Minimal", "Classic", "Modern" };

    public async Task<IViewComponentResult> InvokeAsync(Guid ownerId)
    {
        var layoutSetting = await _themeSettings.GetByKeyAsync(ownerId, "layout-footer");
        var raw = layoutSetting?.EffectiveValue ?? "Neutral";
        var layout = _supportedFooterLayouts.Contains(raw) ? raw : "Neutral";

        ViewBag.OwnerId = ownerId;
        return View(layout);
    }
}
