namespace Blog.Core.Services.Export;

public enum FidelityOutcome { Kept, Changed, Lost }

public sealed record FidelityRow(string Item, FidelityOutcome Outcome, string Detail);

/// <summary>
/// The fidelity matrix shown before an export runs (Data Portability rule: every loss is declared up
/// front). Rows describe what the writers in this folder actually do, with the selection's own counts;
/// a row whose count is zero is left out, so the screen talks about this site, not a generic list.
/// Keep it in step with <c>docs/plans/EXPORTER-PLAN.md</c> §3.
/// </summary>
public static class ExportFidelity
{
    public static IReadOnlyList<FidelityRow> For(ExportTarget target, ExportCounts c, ExportOptions o)
    {
        var rows = new List<FidelityRow>();
        void Add(int count, string item, FidelityOutcome outcome, string detail)
        {
            if (count > 0) rows.Add(new FidelityRow(item, outcome, detail));
        }
        string N(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

        var wp = target == ExportTarget.WordPress;

        Add(c.Posts, N(c.Posts, "post", "posts"), FidelityOutcome.Kept,
            "Title, address (slug), body, dates and status." + (c.Drafts + c.InReview > 0
                ? wp ? " Posts in review import as Pending; drafts and returned posts as Drafts." : " Unpublished posts import as drafts."
                : ""));
        Add(o.IncludePages ? c.Pages : 0, N(c.Pages, "page", "pages"), FidelityOutcome.Kept,
            wp ? "Including parent pages and menu order." : "Ghost pages have no parent, so the page hierarchy is flattened.");

        if (wp)
        {
            Add(c.Categories, N(c.Categories, "category", "categories"), FidelityOutcome.Kept, "As WordPress categories.");
            Add(c.Tags, N(c.Tags, "tag", "tags"), FidelityOutcome.Kept, "As WordPress tags.");
        }
        else
        {
            Add(c.Categories, N(c.Categories, "category", "categories"), FidelityOutcome.Changed,
                "Ghost has no categories, so each becomes a tag. A category and a tag with the same address become one tag.");
            Add(c.Tags, N(c.Tags, "tag", "tags"), FidelityOutcome.Kept, "As Ghost tags.");
        }

        if (o.IncludeEmails)
            Add(c.Authors, N(c.Authors, "author", "authors"), FidelityOutcome.Kept,
                wp ? "With display name and email; WordPress asks you to map or create each one during import."
                   : "With name, email, bio and role; Ghost creates a staff account for each.");
        else
            Add(c.Authors, N(c.Authors, "author", "authors"), wp ? FidelityOutcome.Changed : FidelityOutcome.Lost,
                wp ? "Without email addresses (not included). WordPress asks you to map each author to an account during import."
                   : "Not included: Ghost needs an email for every author. Every post is credited to the account that imports it. Tick “Email addresses” above to keep authors.");

        if (wp)
            Add(o.IncludeComments ? c.Comments : 0, N(c.Comments, "comment", "comments"), FidelityOutcome.Kept,
                "With replies and approval state" + (o.IncludeEmails ? "." : "; commenter emails left out.") + " IP addresses are never exported.");
        else
            Add(c.Comments, N(c.Comments, "comment", "comments"), FidelityOutcome.Lost,
                "Ghost cannot import comments. Export to WordPress to keep them.");

        Add(c.Posts + c.Pages, "Images", FidelityOutcome.Kept,
            wp ? "Feature and in-body images become media items that WordPress downloads from this site during import (tick “Download and import file attachments”). This site must be reachable from the WordPress server."
               : "Feature and in-body images are packed into the ZIP under content/images and the links rewritten. Alt text and captions of feature images are kept.");

        Add(c.WithSeo, N(c.WithSeo, "item with search or social fields", "items with search or social fields"),
            wp ? FidelityOutcome.Changed : FidelityOutcome.Kept,
            wp ? "Meta title, description, canonical, Open Graph and Twitter fields are written for both Yoast SEO and Rank Math. WordPress itself shows them only with one of those plugins."
               : "Into Ghost's own meta title, description, canonical, Open Graph and Twitter fields. Values longer than Ghost allows are cut at a word.");

        string Block(string name) => wp
            ? $"Rendered as ordinary HTML in the body; the original {name} data is kept in post meta."
            : $"Rendered as ordinary HTML in the body. Ghost has nowhere to keep the structured {name} data.";
        Add(c.WithRoundup, N(c.WithRoundup, "post with a roundup", "posts with a roundup"), FidelityOutcome.Changed,
            "A comparison table, one section per product (strengths, limits, score, website) and the scoring method. " + Block("roundup"));
        Add(c.WithKeyFacts, N(c.WithKeyFacts, "post with key facts", "posts with key facts"), FidelityOutcome.Changed, "A “Key facts” list. " + Block("key facts"));
        Add(c.WithFaq, N(c.WithFaq, "post with an FAQ", "posts with an FAQ"), FidelityOutcome.Changed, "A “Frequently asked questions” section. " + Block("FAQ"));
        Add(c.WithHowTo, N(c.WithHowTo, "post with how-to steps", "posts with how-to steps"), FidelityOutcome.Changed, "A numbered “Step by step” list. " + Block("how-to"));
        Add(c.WithAnswerCapsule, N(c.WithAnswerCapsule, "post with an answer capsule", "posts with an answer capsule"), FidelityOutcome.Changed,
            "A bold opening paragraph. " + Block("answer capsule"));
        Add(c.InSeries, N(c.InSeries, "post in a series", "posts in a series"), FidelityOutcome.Changed,
            (wp ? "A tag “Series: …” on each post" : "An internal #series-… tag on each post") +
            ", plus a “Part N of the series” line at the end. Reading order and series pages are not kept.");

        Add(c.WithCustomFields, N(c.WithCustomFields, "item with custom fields", "items with custom fields"),
            wp ? FidelityOutcome.Kept : FidelityOutcome.Lost,
            wp ? "Each value becomes a WordPress custom field under its key, listed in the editor's Custom Fields box. A theme shows them only if it is built to."
               : "Ghost has no custom fields, so these values, and the “Details” list readers see, are not exported. Export to WordPress to keep them.");

        rows.Add(new FidelityRow("Search-engine markup (structured data)", FidelityOutcome.Lost,
            "Not exported; the destination's theme and plugins generate their own."));
        rows.Add(new FidelityRow("Redirect rules, menus, settings, theme, subscribers", FidelityOutcome.Lost,
            "Not part of a content export. Use Backups to move a whole Inkwell site."));
        return rows;
    }
}
