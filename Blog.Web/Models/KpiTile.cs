namespace Blog.Web.Models;

/// <summary>A dashboard number with its context, rendered by <c>Views/Shared/_KpiTile.cshtml</c>.</summary>
/// <param name="Label">What the number is ("Views, 7 days").</param>
/// <param name="Value">The formatted number or short text.</param>
/// <param name="Href">The screen that explains it; the whole tile links there.</param>
/// <param name="Delta">Change against the previous period; positive is good, negative bad, null hides the delta.</param>
/// <param name="Note">Short context beside the delta ("vs previous 7 days").</param>
/// <param name="Icon">Inner SVG markup (24x24 stroke), optional.</param>
public sealed record KpiTile(string Label, string Value, string? Href = null, long? Delta = null, string? Note = null, string? Icon = null);
