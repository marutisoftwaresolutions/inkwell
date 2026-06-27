using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

public class PostRepository : IPostRepository
{
    private readonly DapperContext _ctx;

    private class PostCategoryDto
    {
        public Guid PostId { get; set; }
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
    }

    private class PostTagDto
    {
        public Guid PostId { get; set; }
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
    }

    public PostRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<PagedResult<Post>> GetPostsAsync(PostFilter filter)
    {
        using var conn = _ctx.CreateConnection();

        var where = new List<string> { "1=1" };
        var p = new DynamicParameters();

        if (filter.Status.HasValue)
        {
            if (filter.Status.Value == PostStatus.Published)
            {
                where.Add("(p.Status = 'Published' AND p.PublishedAt <= GETDATE() OR (p.Status = 'Scheduled' AND p.ScheduledAt <= GETDATE()))");
            }
            else
            {
                where.Add("p.Status = @Status");
                p.Add("Status", filter.Status.Value.ToString());
            }
        }
        if (filter.AuthorId.HasValue)
        {
            where.Add("p.AuthorId = @AuthorId");
            p.Add("AuthorId", filter.AuthorId.Value);
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            where.Add("(p.Title LIKE @Search OR p.Plaintext LIKE @Search)");
            p.Add("Search", $"%{filter.Search}%");
        }
        if (filter.CategoryId.HasValue)
        {
            where.Add("EXISTS (SELECT 1 FROM PostCategories pc WHERE pc.PostId = p.Id AND pc.CategoryId = @CategoryId)");
            p.Add("CategoryId", filter.CategoryId.Value);
        }
        if (filter.Categories != null && filter.Categories.Any())
        {
            where.Add("EXISTS (SELECT 1 FROM PostCategories pc JOIN Categories c ON pc.CategoryId = c.Id WHERE pc.PostId = p.Id AND c.Slug IN @Categories)");
            p.Add("Categories", filter.Categories);
        }
        
        if (filter.TagId.HasValue)
        {
            where.Add("EXISTS (SELECT 1 FROM PostTags pt WHERE pt.PostId = p.Id AND pt.TagId = @TagId)");
            p.Add("TagId", filter.TagId.Value);
        }
        if (filter.Tags != null && filter.Tags.Any())
        {
            where.Add("EXISTS (SELECT 1 FROM PostTags pt JOIN Tags t ON pt.TagId = t.Id WHERE pt.PostId = p.Id AND t.Slug IN @Tags)");
            p.Add("Tags", filter.Tags);
        }

        var whereClause = string.Join(" AND ", where);
        var offset = (filter.Page - 1) * filter.PageSize;
        p.Add("Offset", offset);
        p.Add("PageSize", filter.PageSize);

        var countSql = $"SELECT COUNT(*) FROM Posts p WHERE {whereClause}";
        var totalItems = await conn.ExecuteScalarAsync<int>(countSql, p);

        // SQL Server paging: OFFSET / FETCH NEXT
        var sql = $@"
            SELECT p.*, u.DisplayName as AuthorName, COALESCE(u.ProfileImage, u.AvatarUrl) as AvatarUrl, u.Credentials as AuthorCredentials, u.Specialty as AuthorSpecialty,
                   (SELECT COUNT(*) FROM Comments c WHERE c.PostId = p.Id AND c.Status = 'Approved') as CommentCount
            FROM Posts p
            LEFT JOIN Users u ON u.Id = p.AuthorId
            WHERE {whereClause}
            ORDER BY COALESCE(p.PublishedAt, p.UpdatedAt, p.CreatedAt) DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        var posts = (await conn.QueryAsync<Post>(sql, p)).ToList();

        if (posts.Any())
        {
            var postIds = posts.Select(post => post.Id).ToList();

            var postCategories = (await conn.QueryAsync<PostCategoryDto>(@"
                SELECT pc.PostId, c.Id, c.Name, c.Slug
                FROM Categories c
                INNER JOIN PostCategories pc ON pc.CategoryId = c.Id
                WHERE pc.PostId IN @PostIds", new { PostIds = postIds })).ToList();

            var postTags = (await conn.QueryAsync<PostTagDto>(@"
                SELECT pt.PostId, t.Id, t.Name, t.Slug
                FROM Tags t
                INNER JOIN PostTags pt ON pt.TagId = t.Id
                WHERE pt.PostId IN @PostIds", new { PostIds = postIds })).ToList();

            foreach (var post in posts)
            {
                post.Categories = postCategories
                    .Where(c => c.PostId == post.Id)
                    .Select(c => new Category { Id = c.Id, Name = c.Name, Slug = c.Slug })
                    .ToList();
                post.Tags = postTags
                    .Where(t => t.PostId == post.Id)
                    .Select(t => new Tag { Id = t.Id, Name = t.Name, Slug = t.Slug })
                    .ToList();
            }
        }

        return new PagedResult<Post>
        {
            Items = posts,
            TotalItems = totalItems,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<Post?> GetByIdAsync(Guid id, Guid? authorId = null)
    {
        using var conn = _ctx.CreateConnection();
        
        var sql = @"
            SELECT p.*, u.DisplayName as AuthorName, COALESCE(u.ProfileImage, u.AvatarUrl) as AvatarUrl, u.Credentials as AuthorCredentials, u.Specialty as AuthorSpecialty
            FROM Posts p
            LEFT JOIN Users u ON u.Id = p.AuthorId
            WHERE p.Id = @Id" + (authorId.HasValue ? " AND p.AuthorId = @AuthorId" : "");

        var post = await conn.QueryFirstOrDefaultAsync<Post>(sql, new { Id = id, AuthorId = authorId });

        if (post != null)
        {
            post.Categories = (await conn.QueryAsync<Category>(@"
                SELECT c.* FROM Categories c
                INNER JOIN PostCategories pc ON pc.CategoryId = c.Id
                WHERE pc.PostId = @PostId", new { PostId = id })).ToList();

            post.Tags = (await conn.QueryAsync<Tag>(@"
                SELECT t.* FROM Tags t
                INNER JOIN PostTags pt ON pt.TagId = t.Id
                WHERE pt.PostId = @PostId", new { PostId = id })).ToList();
        }
        return post;
    }

    public async Task<Post?> GetBySlugAsync(string slug, Guid? authorId = null)
    {
        using var conn = _ctx.CreateConnection();
        var sql = @"
            SELECT p.*, u.DisplayName as AuthorName, COALESCE(u.ProfileImage, u.AvatarUrl) as AvatarUrl, u.Credentials as AuthorCredentials, u.Specialty as AuthorSpecialty,
                   (SELECT COUNT(*) FROM Comments c WHERE c.PostId = p.Id AND c.Status = 'Approved') as CommentCount
            FROM Posts p
            LEFT JOIN Users u ON u.Id = p.AuthorId
            WHERE p.Slug = @Slug" + (authorId.HasValue ? " AND p.AuthorId = @AuthorId" : "");

        var post = await conn.QueryFirstOrDefaultAsync<Post>(sql, new { Slug = slug, AuthorId = authorId });

        if (post != null)
        {
            post.Categories = (await conn.QueryAsync<Category>(@"
                SELECT c.* FROM Categories c
                INNER JOIN PostCategories pc ON pc.CategoryId = c.Id
                WHERE pc.PostId = @PostId", new { PostId = post.Id })).ToList();

            post.Tags = (await conn.QueryAsync<Tag>(@"
                SELECT t.* FROM Tags t
                INNER JOIN PostTags pt ON pt.TagId = t.Id
                WHERE pt.PostId = @PostId", new { PostId = post.Id })).ToList();
        }
        return post;
    }

    public async Task<Guid> CreateAsync(Post post)
    {
        using var conn = _ctx.CreateConnection();
        if (post.Id == Guid.Empty) post.Id = Guid.NewGuid();
        if (post.Uuid == Guid.Empty) post.Uuid = Guid.NewGuid();

        return await conn.ExecuteScalarAsync<Guid>(@"
            INSERT INTO Posts (Id, Uuid, Title, Slug, Html, Plaintext, Type, Visibility, FeatureImage,
                               MetaTitle, MetaDescription, CanonicalUrl, OgImage, OgTitle, OgDescription,
                               TwitterImage, TwitterTitle, TwitterDescription,
                               AuthorId, Status, PublishedAt, LastVerifiedAt, NextReviewAt, ScheduledAt,
                               AllowComments, FaqJson, CreatedAt, UpdatedAt)
            OUTPUT INSERTED.Id
            VALUES (@Id, @Uuid, @Title, @Slug, @Html, @Plaintext, @Type, @Visibility, @FeatureImage,
                    @MetaTitle, @MetaDescription, @CanonicalUrl, @OgImage, @OgTitle, @OgDescription,
                    @TwitterImage, @TwitterTitle, @TwitterDescription,
                    @AuthorId, @Status, @PublishedAt, @LastVerifiedAt, @NextReviewAt, @ScheduledAt,
                    @AllowComments, @FaqJson, @CreatedAt, @UpdatedAt)",
            new
            {
                post.Id, post.Uuid, post.Title, post.Slug, post.Html, post.Plaintext, post.Type, post.Visibility, post.FeatureImage,
                post.MetaTitle, post.MetaDescription, post.CanonicalUrl, post.OgImage, post.OgTitle, post.OgDescription,
                post.TwitterImage, post.TwitterTitle, post.TwitterDescription,
                post.AuthorId, Status = post.Status.ToString(),
                post.PublishedAt, post.LastVerifiedAt, post.NextReviewAt, post.ScheduledAt,
                post.AllowComments, post.FaqJson,
                CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now
            });
    }

    public async Task UpdateAsync(Post post)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE Posts SET
                Title = @Title, Slug = @Slug, Html = @Html, Plaintext = @Plaintext,
                Type = @Type, Visibility = @Visibility, FeatureImage = @FeatureImage,
                MetaTitle = @MetaTitle, MetaDescription = @MetaDescription, CanonicalUrl = @CanonicalUrl,
                OgImage = @OgImage, OgTitle = @OgTitle, OgDescription = @OgDescription,
                TwitterImage = @TwitterImage, TwitterTitle = @TwitterTitle, TwitterDescription = @TwitterDescription,
                Status = @Status, PublishedAt = @PublishedAt, LastVerifiedAt = @LastVerifiedAt,
                NextReviewAt = @NextReviewAt, ScheduledAt = @ScheduledAt,
                AllowComments = @AllowComments, FaqJson = @FaqJson, UpdatedAt = @UpdatedAt
            WHERE Id = @Id AND AuthorId = @AuthorId",
            new
            {
                post.Title, post.Slug, post.Html, post.Plaintext, post.Type, post.Visibility, post.FeatureImage,
                post.MetaTitle, post.MetaDescription, post.CanonicalUrl, post.OgImage, post.OgTitle, post.OgDescription,
                post.TwitterImage, post.TwitterTitle, post.TwitterDescription,
                Status = post.Status.ToString(),
                post.PublishedAt, post.LastVerifiedAt, post.NextReviewAt, post.ScheduledAt,
                post.AllowComments, post.FaqJson,
                UpdatedAt = DateTime.Now, post.Id, post.AuthorId
            });
    }

    public async Task DeleteAsync(Guid id, Guid? authorId = null)
    {
        using var conn = _ctx.CreateConnection();
        if (authorId.HasValue)
        {
            var ownsPost = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM Posts WHERE Id = @Id AND AuthorId = @AuthorId", new { Id = id, AuthorId = authorId.Value });
            if (ownsPost == 0) return;
        }
        
        await conn.ExecuteAsync("DELETE FROM PostCategories WHERE PostId = @Id", new { Id = id });
        await conn.ExecuteAsync("DELETE FROM PostTags WHERE PostId = @Id", new { Id = id });
        await conn.ExecuteAsync("DELETE FROM Comments WHERE PostId = @Id", new { Id = id });
        await conn.ExecuteAsync("DELETE FROM Posts WHERE Id = @Id", new { Id = id });
    }

    public async Task IncrementViewCountAsync(Guid id)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync("UPDATE Posts SET ViewCount = ViewCount + 1 WHERE Id = @Id", new { Id = id });
    }

    public async Task<bool> SlugExistsAsync(string slug, Guid? excludeId = null)
    {
        using var conn = _ctx.CreateConnection();
        var sql = excludeId.HasValue
            ? "SELECT COUNT(1) FROM Posts WHERE Slug = @Slug AND Id != @ExcludeId"
            : "SELECT COUNT(1) FROM Posts WHERE Slug = @Slug";
        return await conn.ExecuteScalarAsync<int>(sql, new { Slug = slug, ExcludeId = excludeId }) > 0;
    }

    public async Task<int> GetTotalCountAsync(PostStatus? status = null, Guid? authorId = null)
    {
        using var conn = _ctx.CreateConnection();
        var sql = "SELECT COUNT(*) FROM Posts WHERE 1=1";
        if (status.HasValue) 
        {
            if (status.Value == PostStatus.Published)
                sql += " AND (Status = 'Published' OR (Status = 'Scheduled' AND ScheduledAt <= GETDATE()))";
            else
                sql += " AND Status = @Status";
        }
        else
        {
            sql += " AND Status != 'Trash'";
        }
        
        if (authorId.HasValue) sql += " AND AuthorId = @AuthorId";
        
        return await conn.ExecuteScalarAsync<int>(sql, new { Status = status?.ToString(), AuthorId = authorId });
    }

    public async Task<int> GetViewsTodayAsync(Guid? authorId = null)
    {
        using var conn = _ctx.CreateConnection();
        var sql = "SELECT ISNULL(SUM(ViewCount), 0) FROM Posts WHERE CAST(UpdatedAt AS DATE) = CAST(GETDATE() AS DATE)";
        if (authorId.HasValue) sql += " AND AuthorId = @AuthorId";
        
        return await conn.ExecuteScalarAsync<int>(sql, new { AuthorId = authorId });
    }

    public async Task<List<Post>> GetRecentPostsAsync(int count = 5, Guid? authorId = null)
    {
        using var conn = _ctx.CreateConnection();
        var sql = @"
            SELECT TOP(@Count) p.*, u.DisplayName as AuthorName
            FROM Posts p
            LEFT JOIN Users u ON u.Id = p.AuthorId
            WHERE p.Status != 'Trash'";
            
        if (authorId.HasValue)
            sql += " AND p.AuthorId = @AuthorId";
            
        sql += " ORDER BY p.CreatedAt DESC";

        var posts = (await conn.QueryAsync<Post>(sql, new { Count = count, AuthorId = authorId })).ToList();

        if (posts.Any())
        {
            var postIds = posts.Select(post => post.Id).ToList();

            var postCategories = (await conn.QueryAsync<PostCategoryDto>(@"
                SELECT pc.PostId, c.Id, c.Name, c.Slug
                FROM Categories c
                INNER JOIN PostCategories pc ON pc.CategoryId = c.Id
                WHERE pc.PostId IN @PostIds", new { PostIds = postIds })).ToList();

            var postTags = (await conn.QueryAsync<PostTagDto>(@"
                SELECT pt.PostId, t.Id, t.Name, t.Slug
                FROM Tags t
                INNER JOIN PostTags pt ON pt.TagId = t.Id
                WHERE pt.PostId IN @PostIds", new { PostIds = postIds })).ToList();

            foreach (var post in posts)
            {
                post.Categories = postCategories
                    .Where(c => c.PostId == post.Id)
                    .Select(c => new Category { Id = c.Id, Name = c.Name, Slug = c.Slug })
                    .ToList();
                post.Tags = postTags
                    .Where(t => t.PostId == post.Id)
                    .Select(t => new Tag { Id = t.Id, Name = t.Name, Slug = t.Slug })
                    .ToList();
            }
        }

        return posts;
    }

    public async Task<List<Post>> GetRelatedPostsAsync(Guid postId, List<Guid> categoryIds, List<Guid> tagIds, int count = 3)
    {
        using var conn = _ctx.CreateConnection();
        var p = new DynamicParameters();
        p.Add("PostId", postId);
        p.Add("Count", count);

        var conditions = new List<string>();
        if (categoryIds.Any())
        {
            conditions.Add("EXISTS (SELECT 1 FROM PostCategories pc WHERE pc.PostId = p.Id AND pc.CategoryId IN @CategoryIds)");
            p.Add("CategoryIds", categoryIds);
        }
        if (tagIds.Any())
        {
            conditions.Add("EXISTS (SELECT 1 FROM PostTags pt WHERE pt.PostId = p.Id AND pt.TagId IN @TagIds)");
            p.Add("TagIds", tagIds);
        }

        var whereMatch = conditions.Any()
            ? $"AND ({string.Join(" OR ", conditions)})"
            : string.Empty;

        var sql = $@"
            SELECT TOP(@Count) p.*, u.DisplayName as AuthorName, COALESCE(u.ProfileImage, u.AvatarUrl) as AvatarUrl,
                u.Credentials as AuthorCredentials, u.Specialty as AuthorSpecialty
            FROM Posts p
            LEFT JOIN Users u ON u.Id = p.AuthorId
            WHERE p.Id != @PostId
              AND (p.Status = 'Published' OR (p.Status = 'Scheduled' AND p.ScheduledAt <= GETDATE()))
              {whereMatch}
            ORDER BY p.PublishedAt DESC";

        return (await conn.QueryAsync<Post>(sql, p)).ToList();
    }

    public async Task AssignCategoriesAsync(Guid postId, List<Guid> categoryIds)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync("DELETE FROM PostCategories WHERE PostId = @PostId", new { PostId = postId });
        foreach (var catId in categoryIds)
            await conn.ExecuteAsync(
                "IF NOT EXISTS (SELECT 1 FROM PostCategories WHERE PostId=@PostId AND CategoryId=@CategoryId) INSERT INTO PostCategories (PostId, CategoryId) VALUES (@PostId, @CategoryId)",
                new { PostId = postId, CategoryId = catId });
    }

    public async Task AssignTagsAsync(Guid postId, List<Guid> tagIds)
    {
        using var conn = _ctx.CreateConnection();
        await conn.ExecuteAsync("DELETE FROM PostTags WHERE PostId = @PostId", new { PostId = postId });
        foreach (var tagId in tagIds)
            await conn.ExecuteAsync(
                "IF NOT EXISTS (SELECT 1 FROM PostTags WHERE PostId=@PostId AND TagId=@TagId) INSERT INTO PostTags (PostId, TagId) VALUES (@PostId, @TagId)",
                new { PostId = postId, TagId = tagId });
    }
}
