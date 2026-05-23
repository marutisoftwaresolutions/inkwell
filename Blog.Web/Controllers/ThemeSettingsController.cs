using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Blog.Web.Controllers;

[Authorize(Policy = "CanManageThemes")]
[Route("admin/theme")]
public class ThemeSettingsController : Controller
{
    private readonly ICustomThemeSettingRepository _themeSettings;

    public ThemeSettingsController(ICustomThemeSettingRepository themeSettings)
    {
        _themeSettings = themeSettings;
    }

    // ══════════════════════════════════════════════════════════════════
    //  PRESET THEMES
    // ══════════════════════════════════════════════════════════════════

    public record ThemePreset(
        string Id, string Name, string Description,
        string Background, string Foreground, string Primary, string PrimaryForeground,
        string Secondary, string SecondaryForeground, string Muted, string MutedForeground,
        string Accent, string AccentForeground, string Card, string CardForeground,
        string Border, string Destructive,
        string FontHeading, string FontBody, string BorderRadius);

    public static List<ThemePreset> Presets => new()
    {
        // ── Light Presets ────────────────────────────────────────────
        new("nature", "Nature & Earth", "Warm earthy tones with forest green accents",
            "#f8f5f0", "#3e2723", "#2e7d32", "#ffffff",
            "#e8f5e9", "#1b5e20", "#f0e9e0", "#6d4c41",
            "#c8e6c9", "#1b5e20", "#f8f5f0", "#3e2723",
            "#e0d6c9", "#c62828",
            "Montserrat", "Merriweather", "0.5rem"),

        new("ocean", "Ocean Breeze", "Cool blues and teals inspired by the sea",
            "#f0f7fa", "#1a2a3a", "#0277bd", "#ffffff",
            "#e0f2f1", "#00695c", "#e3edf3", "#546e7a",
            "#b2ebf2", "#006064", "#f0f7fa", "#1a2a3a",
            "#cfd8dc", "#d32f2f",
            "Inter", "Source Sans 3", "0.5rem"),

        new("sunset", "Sunset Warm", "Warm oranges and reds like a golden sunset",
            "#fef7ed", "#431407", "#ea580c", "#ffffff",
            "#fff1e6", "#c2410c", "#fde8d8", "#78350f",
            "#fed7aa", "#9a3412", "#fef7ed", "#431407",
            "#e8d5c4", "#dc2626",
            "Outfit", "Lora", "0.5rem"),

        new("royal", "Royal Purple", "Elegant purples and golds for a premium feel",
            "#faf5ff", "#2e1065", "#7c3aed", "#ffffff",
            "#ede9fe", "#5b21b6", "#f3e8ff", "#6b21a8",
            "#ddd6fe", "#4c1d95", "#faf5ff", "#2e1065",
            "#d8d0e8", "#dc2626",
            "Playfair Display", "Nunito", "0.75rem"),

        new("minimal", "Minimal Clean", "Black, white, and grey — pure minimalism",
            "#ffffff", "#171717", "#171717", "#ffffff",
            "#f5f5f5", "#404040", "#f5f5f5", "#737373",
            "#e5e5e5", "#262626", "#ffffff", "#171717",
            "#e5e5e5", "#dc2626",
            "Inter", "Inter", "0.25rem"),

        new("coffee", "Coffee House", "Rich browns and creams for a cozy vibe",
            "#f5f0eb", "#3e2723", "#6d4c41", "#ffffff",
            "#efebe9", "#4e342e", "#e8e0d8", "#5d4037",
            "#d7ccc8", "#3e2723", "#f5f0eb", "#3e2723",
            "#d7ccc8", "#c62828",
            "Lora", "Merriweather", "0.5rem"),

        new("arctic", "Arctic Frost", "Icy blues and whites with a crisp modern look",
            "#f8fafc", "#0f172a", "#0ea5e9", "#ffffff",
            "#e0f2fe", "#0369a1", "#f1f5f9", "#475569",
            "#bae6fd", "#075985", "#f8fafc", "#0f172a",
            "#e2e8f0", "#e11d48",
            "Roboto", "Roboto", "1rem"),

        // ── Dark Presets ─────────────────────────────────────────────
        new("midnight", "Midnight Blue", "Sleek dark mode with electric blue highlights",
            "#0f172a", "#f8fafc", "#3b82f6", "#ffffff",
            "#1e293b", "#e2e8f0", "#334155", "#cbd5e1",
            "#1d4ed8", "#eff6ff", "#1e293b", "#f8fafc",
            "#334155", "#ef4444",
            "Inter", "Inter", "0.5rem"),
            
        new("forest", "Dark Forest", "Deep greens and warm greys for a high-contrast dark theme",
            "#022c22", "#ecfdf5", "#10b981", "#ffffff",
            "#064e3b", "#d1fae5", "#065f46", "#a7f3d0",
            "#059669", "#ecfdf5", "#064e3b", "#ecfdf5",
            "#047857", "#f87171",
            "Nunito", "Lora", "0.5rem"),

        new("obsidian", "Obsidian Rose", "Dark charcoal with warm rose-pink accents",
            "#1a1a2e", "#eaeaea", "#e94990", "#ffffff",
            "#16213e", "#f9a8d4", "#16213e", "#9ca3af",
            "#2d1b3d", "#f472b6", "#16213e", "#eaeaea",
            "#2a2a40", "#ef4444",
            "Outfit", "Nunito", "0.75rem")
    };

    // ══════════════════════════════════════════════════════════════════
    //  DEFAULT SEED SETTINGS (Nature & Earth)
    // ══════════════════════════════════════════════════════════════════

    private static List<CustomThemeSetting> GetDefaultSettings() => new()
    {
        new() { SettingGroup = "colors", SettingKey = "background", SettingType = "color", DefaultValue = "#f8f5f0", Label = "Background", Description = "Main page background color" },
        new() { SettingGroup = "colors", SettingKey = "foreground", SettingType = "color", DefaultValue = "#3e2723", Label = "Foreground", Description = "Main text color" },
        new() { SettingGroup = "colors", SettingKey = "primary", SettingType = "color", DefaultValue = "#2e7d32", Label = "Primary", Description = "Primary accent color for links and buttons" },
        new() { SettingGroup = "colors", SettingKey = "primary-foreground", SettingType = "color", DefaultValue = "#ffffff", Label = "Primary Foreground", Description = "Text on primary-colored backgrounds" },
        new() { SettingGroup = "colors", SettingKey = "secondary", SettingType = "color", DefaultValue = "#e8f5e9", Label = "Secondary", Description = "Secondary accent background color" },
        new() { SettingGroup = "colors", SettingKey = "secondary-foreground", SettingType = "color", DefaultValue = "#1b5e20", Label = "Secondary Foreground", Description = "Text on secondary-colored backgrounds" },
        new() { SettingGroup = "colors", SettingKey = "muted", SettingType = "color", DefaultValue = "#f0e9e0", Label = "Muted", Description = "Muted background for subtle elements" },
        new() { SettingGroup = "colors", SettingKey = "muted-foreground", SettingType = "color", DefaultValue = "#6d4c41", Label = "Muted Foreground", Description = "Text color for muted elements" },
        new() { SettingGroup = "colors", SettingKey = "accent", SettingType = "color", DefaultValue = "#c8e6c9", Label = "Accent", Description = "Accent background for tags, highlights" },
        new() { SettingGroup = "colors", SettingKey = "accent-foreground", SettingType = "color", DefaultValue = "#1b5e20", Label = "Accent Foreground", Description = "Text on accent backgrounds" },
        new() { SettingGroup = "colors", SettingKey = "card", SettingType = "color", DefaultValue = "#f8f5f0", Label = "Card", Description = "Post card background color" },
        new() { SettingGroup = "colors", SettingKey = "card-foreground", SettingType = "color", DefaultValue = "#3e2723", Label = "Card Foreground", Description = "Post card text color" },
        new() { SettingGroup = "colors", SettingKey = "border", SettingType = "color", DefaultValue = "#e0d6c9", Label = "Border", Description = "Border color for cards and dividers" },
        new() { SettingGroup = "colors", SettingKey = "destructive", SettingType = "color", DefaultValue = "#c62828", Label = "Destructive", Description = "Color for error states and delete actions" },
        new() { SettingGroup = "typography", SettingKey = "font-heading", SettingType = "select", DefaultValue = "Montserrat", Label = "Heading Font", Description = "Font family for headings and titles" },
        new() { SettingGroup = "typography", SettingKey = "font-body", SettingType = "select", DefaultValue = "Merriweather", Label = "Body Font", Description = "Font family for body text and paragraphs" },
        new() { SettingGroup = "layout", SettingKey = "border-radius", SettingType = "select", DefaultValue = "0.5rem", Label = "Border Radius", Description = "Corner rounding for cards and buttons" },
        new() { SettingGroup = "layout", SettingKey = "layout-navbar", SettingType = "select", DefaultValue = "Neutral", Label = "Navbar Layout", Description = "Visual style of the top navigation bar" },
        new() { SettingGroup = "layout", SettingKey = "layout-footer", SettingType = "select", DefaultValue = "Neutral", Label = "Footer Layout", Description = "Visual style of the site footer" },
        new() { SettingGroup = "layout", SettingKey = "layout-index", SettingType = "select", DefaultValue = "Neutral", Label = "Homepage Layout", Description = "Layout of the main blog post list section" },
        new() { SettingGroup = "layout", SettingKey = "layout-postcard", SettingType = "select", DefaultValue = "Neutral", Label = "Post Card Style", Description = "Visual style of individual post cards" },
        new() { SettingGroup = "layout", SettingKey = "layout-post", SettingType = "select", DefaultValue = "Neutral", Label = "Single Post Layout", Description = "Layout structure for individual blog posts" },
        new() { SettingGroup = "layout", SettingKey = "layout-page", SettingType = "select", DefaultValue = "Neutral", Label = "Single Page Layout", Description = "Layout structure for static pages" },
        new() { SettingGroup = "inkwell", SettingKey = "inkwell-preset", SettingType = "select", DefaultValue = "cream", Label = "Inkwell Preset", Description = "Color preset for Magazine/Inkwell layout (cream, linen, manuscript, etc.)" },
    };

    // ══════════════════════════════════════════════════════════════════
    //  ACTIONS
    // ══════════════════════════════════════════════════════════════════

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Seed defaults on first visit
        await _themeSettings.SeedDefaultsAsync(userId, GetDefaultSettings());

        var settings = await _themeSettings.GetAllAsync(userId);

        var model = new ThemeSettingsViewModel { Settings = settings };
        ViewData["Title"] = "Theme Settings";
        ViewData["Presets"] = Presets;
        return View(model);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Dictionary<string, string> settings)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var updates = settings
            .Select(kv => new CustomThemeSetting { SettingKey = kv.Key, SettingValue = kv.Value })
            .ToList();

        await _themeSettings.SaveAllAsync(userId, updates);

        TempData["Success"] = "Theme settings updated successfully.";
        return RedirectToAction("Index");
    }

    [HttpPost("preset/{presetId}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyPreset(string presetId)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var preset = Presets.FirstOrDefault(p => p.Id == presetId);
        if (preset == null)
        {
            TempData["Error"] = "Preset not found.";
            return RedirectToAction("Index");
        }

        // Seed if not yet created
        await _themeSettings.SeedDefaultsAsync(userId, GetDefaultSettings());

        // Map preset values to setting keys
        var updates = new List<CustomThemeSetting>
        {
            new() { SettingKey = "background", SettingValue = preset.Background },
            new() { SettingKey = "foreground", SettingValue = preset.Foreground },
            new() { SettingKey = "primary", SettingValue = preset.Primary },
            new() { SettingKey = "primary-foreground", SettingValue = preset.PrimaryForeground },
            new() { SettingKey = "secondary", SettingValue = preset.Secondary },
            new() { SettingKey = "secondary-foreground", SettingValue = preset.SecondaryForeground },
            new() { SettingKey = "muted", SettingValue = preset.Muted },
            new() { SettingKey = "muted-foreground", SettingValue = preset.MutedForeground },
            new() { SettingKey = "accent", SettingValue = preset.Accent },
            new() { SettingKey = "accent-foreground", SettingValue = preset.AccentForeground },
            new() { SettingKey = "card", SettingValue = preset.Card },
            new() { SettingKey = "card-foreground", SettingValue = preset.CardForeground },
            new() { SettingKey = "border", SettingValue = preset.Border },
            new() { SettingKey = "destructive", SettingValue = preset.Destructive },
            new() { SettingKey = "font-heading", SettingValue = preset.FontHeading },
            new() { SettingKey = "font-body", SettingValue = preset.FontBody },
            new() { SettingKey = "border-radius", SettingValue = preset.BorderRadius },
            // Note: blog-layout is NOT overridden by preset — user controls it independently
        };

        await _themeSettings.SaveAllAsync(userId, updates);

        TempData["Success"] = $"Theme '{preset.Name}' applied successfully.";
        return RedirectToAction("Index");
    }

    [HttpPost("layout-preset/{presetId}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyLayoutPreset(string presetId)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var validLayouts = new[] { "Neutral", "Magazine", "Minimal", "Grid", "Classic", "Modern" };

        // LowercaseUrls routing option lowercases route values — normalise back to PascalCase
        var normalised = validLayouts.FirstOrDefault(l =>
            l.Equals(presetId, StringComparison.OrdinalIgnoreCase));

        if (normalised == null)
        {
            TempData["Error"] = "Layout Preset not found.";
            return RedirectToAction("Index");
        }

        presetId = normalised; // use canonical PascalCase value from here on

        // Seed if not yet created
        await _themeSettings.SeedDefaultsAsync(userId, GetDefaultSettings());

        var updates = new List<CustomThemeSetting>
        {
            new() { SettingKey = "layout-navbar", SettingValue = presetId },
            new() { SettingKey = "layout-footer", SettingValue = presetId },
            new() { SettingKey = "layout-index", SettingValue = presetId },
            new() { SettingKey = "layout-postcard", SettingValue = presetId },
            new() { SettingKey = "layout-post", SettingValue = presetId },
            new() { SettingKey = "layout-page", SettingValue = presetId },
            new() { SettingKey = "blog-layout", SettingValue = presetId }
        };

        await _themeSettings.SaveAllAsync(userId, updates);

        TempData["Success"] = $"Layout '{presetId}' applied successfully.";
        return RedirectToAction("Index");
    }

    [HttpPost("inkwell-preset/{presetName}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyInkwellPreset(string presetName)
    {
        var validPresets = new[] { "cream", "linen", "manuscript", "folio", "press", "letterpress", "foxglove", "cobalt", "ink", "onyx", "slate", "sand", "plum", "forest", "mono" };
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var normalised = validPresets.FirstOrDefault(p => p.Equals(presetName, StringComparison.OrdinalIgnoreCase));
        if (normalised == null)
        {
            TempData["Error"] = "Inkwell preset not found.";
            return RedirectToAction("Index");
        }

        await _themeSettings.SeedDefaultsAsync(userId, GetDefaultSettings());
        await _themeSettings.SaveAllAsync(userId, new List<CustomThemeSetting>
        {
            new() { SettingKey = "inkwell-preset", SettingValue = normalised }
        });

        TempData["Success"] = $"Inkwell preset '{normalised}' applied.";
        return RedirectToAction("Index");
    }

    [HttpPost("reset")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reset()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var existing = await _themeSettings.GetAllAsync(userId);
        var resets = existing
            .Select(s => new CustomThemeSetting { SettingKey = s.SettingKey, SettingValue = s.DefaultValue })
            .ToList();

        await _themeSettings.SaveAllAsync(userId, resets);

        TempData["Success"] = "Theme reset to defaults.";
        return RedirectToAction("Index");
    }
}

