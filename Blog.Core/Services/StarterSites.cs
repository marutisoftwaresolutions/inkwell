using System.Net;

namespace Blog.Core.Services;

/// <summary>
/// The starter sites the setup wizard offers, so a new install does not open on an empty site. A
/// starter is a layout, a colour preset and a few pages built from the existing blocks. Every word
/// of it is a placeholder and says so: a starter gives a shape to fill in, never claims, prices,
/// testimonials or company facts. Applying one never overwrites a page that already exists.
/// Pure: this class only describes; <c>StarterSiteService</c> in Blog.Web writes.
/// </summary>
public static class StarterSites
{
    public const string None = "none";

    public sealed record StarterPage(string Title, string Slug, string Html, string MetaDescription, int SortOrder);

    public sealed record Starter(string Key, string Name, string Description, string Layout, string Preset, IReadOnlyList<StarterPage> Pages);

    /// <summary>Layouts and presets a starter may name; each exists as a view and a preset today (Theme rule).</summary>
    public static readonly IReadOnlyList<string> KnownLayouts = ["Neutral", "Magazine", "Grid", "Minimal", "Classic", "Modern", "Newsletter", "Showcase"];
    public static readonly IReadOnlyList<string> KnownPresets = ["cream", "linen", "manuscript", "folio", "press", "letterpress", "foxglove", "cobalt", "ink", "onyx"];

    public static readonly IReadOnlyList<Starter> All =
    [
        new("blog", "Blog", "Posts on the home page, plus an About and a Contact page.", "Magazine", "cream",
        [
            About(1),
            Contact(2),
        ]),
        new("business", "Business site with a blog", "Services, Pricing, About and Contact pages built from section blocks, with the blog on the home page.", "Neutral", "linen",
        [
            new("Services", "services", Features("What we do", "Service name") + Cta("Talk to us about your project", "/contact", "Get in touch"),
                "Placeholder description of the services page. Replace it with one sentence about what you offer.", 1),
            new("Pricing", "pricing", Pricing() + Faq(),
                "Placeholder description of the pricing page. Replace it with one sentence about your plans.", 2),
            About(3),
            Contact(4),
        ]),
        new("portfolio", "Portfolio", "A Work page with a grid of projects, plus About and Contact.", "Grid", "press",
        [
            new("Work", "work", Features("Selected work", "Project name") + Cta("Have a project in mind?", "/contact", "Start a conversation"),
                "Placeholder description of the work page. Replace it with one sentence about the work you show.", 1),
            About(2),
            Contact(3),
        ]),
        new("newsletter", "Newsletter", "A reading layout for a publication that lives in the inbox, with an About page that says what subscribers get.", "Newsletter", "manuscript",
        [
            new("About", "about",
                H2("What this newsletter is") + P("Placeholder. Say what you write about, how often an issue arrives and who it is for.") +
                Columns(("What you get", "Placeholder. Describe a typical issue."), ("When it arrives", "Placeholder. Say how often you send.")) +
                Cta("Read the latest issues", "/", "Go to the home page"),
                "Placeholder description of this newsletter. Replace it with one sentence about what subscribers get.", 1),
            Contact(2),
        ]),
    ];

    public static Starter? Find(string? key) =>
        All.FirstOrDefault(s => string.Equals(s.Key, (key ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The key to store for a choice from the wizard: a known starter's key, or <see cref="None"/>.</summary>
    public static string Normalize(string? key) => Find(key)?.Key ?? None;

    // ── Page parts (the same markup the builder's blocks save) ───────────────────

    private static StarterPage About(int order) => new("About", "about",
        H2("About this site") + P("Placeholder. Say who you are, what this site covers and why a reader can trust it.") +
        Columns(("Who we are", "Placeholder. A sentence or two about you or your organisation."), ("What you will find here", "Placeholder. The topics you write about.")),
        "Placeholder description of the about page. Replace it with one sentence about who is behind this site.", order);

    private static StarterPage Contact(int order) => new("Contact", "contact",
        "<section class=\"sec-section sec-contact\">" + H2("Get in touch") +
        "<p class=\"sec-lede\">Placeholder. Say how to reach you and when people can expect a reply. To add a form, create one under Forms, then add the Form block to this page.</p></section>",
        "Placeholder description of the contact page. Replace it with one sentence about how to reach you.", order);

    private static string H2(string text) => $"<h2>{E(text)}</h2>";
    private static string P(string text) => $"<p>{E(text)}</p>";

    private static string Columns(params (string Heading, string Text)[] columns) =>
        $"<div class=\"sec-columns\" data-columns=\"{Math.Clamp(columns.Length, 2, 4)}\">" +
        string.Concat(columns.Select(c => $"<div class=\"sec-col\"><h3>{E(c.Heading)}</h3><p>{E(c.Text)}</p></div>")) + "</div>";

    private static string Features(string heading, string itemName)
    {
        var item = $"<div class=\"sec-feature\"><h3>{E(itemName)}</h3><p>Placeholder. Say what this is and who it helps, in one or two sentences.</p></div>";
        return $"<section class=\"sec-section sec-features\" data-columns=\"3\"><h2>{E(heading)}</h2><p class=\"sec-lede\">Placeholder introduction. Replace or delete it.</p>" +
               $"<div class=\"sec-grid\">{item}{item}{item}</div></section>";
    }

    private static string Cta(string heading, string href, string label) =>
        $"<section class=\"sec-section sec-cta\"><h2>{E(heading)}</h2><p>Placeholder. One sentence on why to take this step.</p>" +
        $"<p class=\"sec-actions\"><a class=\"sec-btn\" href=\"{E(href)}\">{E(label)}</a></p></section>";

    private static string Pricing()
    {
        var plan = "<div class=\"sec-plan\"><h3>Plan name</h3><p class=\"sec-price\">Price</p><p>Placeholder. Who this plan is for.</p>" +
                   "<ul><li>What is included</li><li>What is included</li><li>What is included</li></ul>" +
                   "<p class=\"sec-actions\"><a class=\"sec-btn\" href=\"/contact\">Choose plan</a></p></div>";
        return $"<section class=\"sec-section sec-pricing\" data-columns=\"3\"><h2>Pricing</h2><div class=\"sec-grid\">{plan}{plan}{plan}</div></section>";
    }

    private static string Faq()
    {
        var item = "<div class=\"sec-faq-item\"><h3>A question buyers ask?</h3><p>Placeholder answer. Keep it to a few sentences.</p></div>";
        return $"<section class=\"sec-section sec-faq\"><h2>Questions and answers</h2>{item}{item}{item}</section>";
    }

    private static string E(string text) => WebUtility.HtmlEncode(text);
}
