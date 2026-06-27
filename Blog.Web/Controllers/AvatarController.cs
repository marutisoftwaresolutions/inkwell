using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace Blog.Web.Controllers;

[Route("avatar")]
public class AvatarController : Controller
{
    private static readonly string[] Palettes =
    [
        "#1e3a5f", "#5b21b6", "#0f766e", "#0369a1", "#9a3412",
        "#166534", "#7c3aed", "#0e7490", "#b45309", "#be185d"
    ];

    [HttpGet("{seed}")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public IActionResult Get(string seed, int size = 80, string bg = "")
    {
        var initials = GetInitials(seed);
        var bgColor = string.IsNullOrEmpty(bg) ? PickColor(seed) : $"#{bg.TrimStart('#')}";
        var textColor = "#ffffff";
        var half = size / 2;
        var fontSize = size * 0.4;

        var svg = new StringBuilder();
        svg.Append($"<svg xmlns='http://www.w3.org/2000/svg' width='{size}' height='{size}' viewBox='0 0 {size} {size}'>");
        svg.Append($"<rect width='{size}' height='{size}' rx='{half}' fill='{bgColor}'/>");
        svg.Append($"<text x='50%' y='50%' dominant-baseline='central' text-anchor='middle' ");
        svg.Append($"font-family='system-ui,sans-serif' font-size='{fontSize:F0}' font-weight='600' fill='{textColor}'>");
        svg.Append(System.Web.HttpUtility.HtmlEncode(initials));
        svg.Append("</text></svg>");

        return Content(svg.ToString(), "image/svg+xml");
    }

    private static string GetInitials(string seed)
    {
        if (string.IsNullOrWhiteSpace(seed)) return "?";
        var parts = seed.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[^1][0])}";
        return seed.Length > 1
            ? $"{char.ToUpper(seed[0])}{char.ToUpper(seed[1])}"
            : char.ToUpper(seed[0]).ToString();
    }

    private static string PickColor(string seed)
    {
        var hash = seed.Aggregate(0, (acc, c) => acc * 31 + c);
        return Palettes[Math.Abs(hash) % Palettes.Length];
    }
}
