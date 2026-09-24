using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Models;

/// <summary>
/// Search and paging for the admin lists that load everything and used to render everything.
/// The list stays the view's model type, so a view's <c>@foreach (var x in Model)</c> is untouched;
/// the query, page and totals travel in ViewData for the shared <c>_ListSearch</c> and
/// <c>_ListPager</c> partials. Filtering is a case-insensitive "contains" across the fields the
/// caller names — plenty for a few hundred rows, and honest about being in memory.
/// </summary>
public static class ListPaging
{
    public const int DefaultPageSize = 25;

    public const string QueryKey = "ListQuery";
    public const string PageKey = "ListPage";
    public const string TotalPagesKey = "ListTotalPages";
    public const string TotalKey = "ListTotal";
    public const string ShownKey = "ListShown";

    /// <summary>Filter, page and stash the paging state on the controller. Returns the rows for this page.</summary>
    public static List<T> Apply<T>(Controller controller, IEnumerable<T> items, string? q, int page,
        Func<T, IEnumerable<string?>> searchFields, int pageSize = DefaultPageSize)
    {
        var (rows, meta) = Page(items, q, page, searchFields, pageSize);
        Stash(controller, meta);
        return rows;
    }

    /// <summary>Pure core, for tests: the page of rows plus what the pager needs.</summary>
    public static (List<T> Rows, PagingMeta Meta) Page<T>(IEnumerable<T> items, string? q, int page,
        Func<T, IEnumerable<string?>> searchFields, int pageSize = DefaultPageSize)
    {
        var query = (q ?? string.Empty).Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? items.ToList()
            : items.Where(i => searchFields(i).Any(f => f is not null && f.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();

        if (pageSize < 1) pageSize = DefaultPageSize;
        var totalPages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var rows = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return (rows, new PagingMeta(query, page, totalPages, filtered.Count, rows.Count));
    }

    /// <summary>For lists that page in SQL (media): record totals the repository already computed.</summary>
    public static void Stash(Controller controller, PagingMeta meta)
    {
        controller.ViewData[QueryKey] = meta.Query;
        controller.ViewData[PageKey] = meta.Page;
        controller.ViewData[TotalPagesKey] = meta.TotalPages;
        controller.ViewData[TotalKey] = meta.Total;
        controller.ViewData[ShownKey] = meta.Shown;
    }
}

public sealed record PagingMeta(string Query, int Page, int TotalPages, int Total, int Shown)
{
    public static PagingMeta FromCounts(string? query, int page, int total, int pageSize, int shown)
    {
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)Math.Max(1, pageSize)));
        return new PagingMeta((query ?? string.Empty).Trim(), Math.Clamp(page, 1, totalPages), totalPages, total, shown);
    }
}
