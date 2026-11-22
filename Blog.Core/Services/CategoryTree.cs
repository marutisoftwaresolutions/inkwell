using Blog.Core.Domain;

namespace Blog.Core.Services;

/// <summary>
/// Categories can sit under another category (<see cref="Category.ParentId"/>). This is every rule
/// about that shape in one place: the order to list them in, what lies beneath a category, the way
/// up from one, and whether a move would make a loop. It tolerates bad data (a parent that no longer
/// exists, a loop written straight into the database): such a category is simply listed at the top
/// level, never lost and never followed forever. Pure.
/// </summary>
public static class CategoryTree
{
    /// <summary>How deep a category may sit: a top-level category is depth 0.</summary>
    public const int MaxDepth = 3;

    public sealed record Node(Category Category, int Depth);

    /// <summary>Every category once, parents before their children, each level by name.</summary>
    public static List<Node> Order(IEnumerable<Category> categories)
    {
        var all = categories.ToList();
        var ids = all.Select(c => c.Id).ToHashSet();
        var children = all.Where(c => c.ParentId is Guid p && p != c.Id && ids.Contains(p))
            .GroupBy(c => c.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList());

        var result = new List<Node>(all.Count);
        var seen = new HashSet<Guid>();

        void Visit(Category c, int depth)
        {
            if (!seen.Add(c.Id)) return;
            result.Add(new Node(c, depth));
            if (children.TryGetValue(c.Id, out var kids))
                foreach (var kid in kids) Visit(kid, depth + 1);
        }

        foreach (var root in all.Where(c => c.ParentId is not Guid p || p == c.Id || !ids.Contains(p)).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            Visit(root, 0);
        // Whatever is left belongs to a loop: show each at the top level instead of dropping it.
        foreach (var stray in all.Where(c => !seen.Contains(c.Id)).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            Visit(stray, 0);
        return result;
    }

    /// <summary>Everything beneath a category, at any depth, without the category itself.</summary>
    public static HashSet<Guid> DescendantIds(IEnumerable<Category> categories, Guid id)
    {
        var byParent = categories.Where(c => c.ParentId is not null).ToLookup(c => c.ParentId!.Value);
        var found = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(id);
        while (queue.Count > 0)
            foreach (var child in byParent[queue.Dequeue()])
                if (child.Id != id && found.Add(child.Id)) queue.Enqueue(child.Id);
        return found;
    }

    /// <summary>The way down to a category: its top-level ancestor first, the category itself last.</summary>
    public static List<Category> Path(IEnumerable<Category> categories, Guid id)
    {
        var byId = categories.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        var path = new List<Category>();
        var seen = new HashSet<Guid>();
        for (Guid? at = id; at is Guid current && byId.TryGetValue(current, out var c) && seen.Add(current); at = c.ParentId)
            path.Add(c);
        path.Reverse();
        return path;
    }

    /// <summary>
    /// Whether <paramref name="id"/> may be placed under <paramref name="parentId"/> (null: the top
    /// level). Refused when the parent is the category itself or lies beneath it (a loop), when the
    /// parent does not exist, or when the category and what hangs under it would end up deeper than
    /// <see cref="MaxDepth"/>. Pass <see cref="Guid.Empty"/> as the id for a category not created yet.
    /// </summary>
    public static bool CanPlaceUnder(IEnumerable<Category> categories, Guid id, Guid? parentId)
    {
        if (parentId is not Guid parent) return true;
        var all = categories.ToList();
        if (parent == id || all.All(c => c.Id != parent)) return false;

        var beneath = id == Guid.Empty ? new HashSet<Guid>() : DescendantIds(all, id);
        if (beneath.Contains(parent)) return false;

        var parentDepth = Path(all, parent).Count - 1;
        return parentDepth + 1 + HeightBelow(all, id, beneath) <= MaxDepth;
    }

    /// <summary>How many levels hang under a category: 0 when it has no children.</summary>
    private static int HeightBelow(List<Category> all, Guid id, HashSet<Guid> beneath)
    {
        if (beneath.Count == 0) return 0;
        var own = Path(all, id).Count;
        return beneath.Max(d => Path(all, d).Count) - own;
    }
}
