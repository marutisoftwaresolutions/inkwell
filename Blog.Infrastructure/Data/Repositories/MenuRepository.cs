using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class MenuRepository : IMenuRepository
{
    private readonly DapperContext _ctx;
    public MenuRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<IReadOnlyList<MenuItem>> GetItemsAsync(Guid ownerId, string location)
    {
        using var conn = _ctx.CreateConnection();
        var rows = await conn.QueryAsync<MenuItem>(@"
            SELECT * FROM MenuItems
             WHERE OwnerId = @OwnerId AND Location = @Location
             ORDER BY SortOrder, CreatedAt;", new { OwnerId = ownerId, Location = location });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<MenuItem>> GetResolvedAsync(Guid ownerId, string location)
    {
        using var conn = _ctx.CreateConnection();
        var rows = await conn.QueryAsync<MenuItem>(@"
            SELECT m.*,
                   CASE m.LinkType
                        WHEN 'page'     THEN p.Slug
                        WHEN 'category' THEN c.Slug
                        WHEN 'tag'      THEN t.Slug
                        WHEN 'series'   THEN s.Slug
                   END AS TargetSlug,
                   CASE m.LinkType
                        WHEN 'page'     THEN p.Title
                        WHEN 'category' THEN c.Name
                        WHEN 'tag'      THEN t.Name
                        WHEN 'series'   THEN s.Title
                   END AS TargetTitle,
                   CAST(CASE
                        WHEN m.LinkType = 'page'     THEN CASE WHEN p.Id IS NOT NULL AND p.IsPublished = 1 THEN 1 ELSE 0 END
                        WHEN m.LinkType = 'category' THEN CASE WHEN c.Id IS NOT NULL THEN 1 ELSE 0 END
                        WHEN m.LinkType = 'tag'      THEN CASE WHEN t.Id IS NOT NULL THEN 1 ELSE 0 END
                        WHEN m.LinkType = 'series'   THEN CASE WHEN s.Id IS NOT NULL THEN 1 ELSE 0 END
                        ELSE 1 END AS BIT) AS TargetAvailable
              FROM MenuItems m
              LEFT JOIN Pages      p ON m.LinkType = 'page'     AND p.Id = m.TargetId
              LEFT JOIN Categories c ON m.LinkType = 'category' AND c.Id = m.TargetId
              LEFT JOIN Tags       t ON m.LinkType = 'tag'      AND t.Id = m.TargetId
              LEFT JOIN Series     s ON m.LinkType = 'series'   AND s.Id = m.TargetId
             WHERE m.OwnerId = @OwnerId AND m.Location = @Location
             ORDER BY m.SortOrder, m.CreatedAt;", new { OwnerId = ownerId, Location = location });
        return rows.ToList();
    }

    public async Task ReplaceAsync(Guid ownerId, string location, IReadOnlyList<MenuItem> items)
    {
        using var conn = _ctx.CreateConnection();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync("DELETE FROM MenuItems WHERE OwnerId = @OwnerId AND Location = @Location;",
            new { OwnerId = ownerId, Location = location }, tx);
        if (items.Count > 0)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO MenuItems (Id, OwnerId, Location, ParentId, SortOrder, Label, LinkType, TargetId, Url, OpenInNewTab, CreatedAt, UpdatedAt)
                VALUES (@Id, @OwnerId, @Location, @ParentId, @SortOrder, @Label, @LinkType, @TargetId, @Url, @OpenInNewTab, @CreatedAt, @UpdatedAt);",
                items, tx);
        }
        tx.Commit();
    }
}
