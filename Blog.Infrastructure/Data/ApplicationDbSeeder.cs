using Blog.Core.Domain;
using Blog.Core.Interfaces;

namespace Blog.Infrastructure.Data;

public class ApplicationDbSeeder
{
    private readonly ICategoryRepository _categoryRepo;
    private readonly ITagRepository _tagRepo;
    private readonly IPageRepository _pageRepo;
    private readonly IPostRepository _postRepo;
    private readonly ISettingRepository _settingRepo;
    private readonly ICommentRepository _commentRepo;
    private readonly IRoleRepository _roleRepo;

    public ApplicationDbSeeder(
        ICategoryRepository categoryRepo,
        ITagRepository tagRepo,
        IPageRepository pageRepo,
        IPostRepository postRepo,
        ISettingRepository settingRepo,
        ICommentRepository commentRepo,
        IRoleRepository roleRepo)
    {
        _categoryRepo = categoryRepo;
        _tagRepo = tagRepo;
        _pageRepo = pageRepo;
        _postRepo = postRepo;
        _settingRepo = settingRepo;
        _commentRepo = commentRepo;
        _roleRepo = roleRepo;
    }

    public async Task SeedAsync()
    {
        // Skip if content already seeded (check posts, not settings — settings exist from setup)
        var existingPosts = await _postRepo.GetTotalCountAsync(null, Guid.Empty);
        if (existingPosts > 0) return;

        await SeedRolesAndPermissionsAsync();
        await SeedCategoriesAsync();
        await SeedTagsAsync();
        await SeedPagesAsync();
        await SeedSettingsAsync();
        await SeedPostsAndCommentsAsync();
    }

    // ── RBAC Seeding ──────────────────────────────────────────────────────────

    public async Task SeedRolesAndPermissionsAsync()
    {
        // Check if roles already exist
        var existing = await _roleRepo.GetAllRolesAsync();
        if (existing.Count > 0) return;

        // Create roles
        var adminRole = new Role { Name = "Admin", Description = "Full access to all features" };
        var editorRole = new Role { Name = "Editor", Description = "Manage all content but not site settings" };
        var authorRole = new Role { Name = "Author", Description = "Manage own posts and media" };
        var contributorRole = new Role { Name = "Contributor", Description = "Create draft posts only" };

        adminRole.Id = await _roleRepo.CreateRoleAsync(adminRole);
        editorRole.Id = await _roleRepo.CreateRoleAsync(editorRole);
        authorRole.Id = await _roleRepo.CreateRoleAsync(authorRole);
        contributorRole.Id = await _roleRepo.CreateRoleAsync(contributorRole);

        // Create permissions
        var permissions = new[]
        {
            new Permission { Name = "posts.edit", ActionType = "edit", ObjectType = "post" },
            new Permission { Name = "posts.publish", ActionType = "publish", ObjectType = "post" },
            new Permission { Name = "posts.delete", ActionType = "delete", ObjectType = "post" },
            new Permission { Name = "pages.manage", ActionType = "manage", ObjectType = "page" },
            new Permission { Name = "comments.manage", ActionType = "manage", ObjectType = "comment" },
            new Permission { Name = "categories.manage", ActionType = "manage", ObjectType = "category" },
            new Permission { Name = "tags.manage", ActionType = "manage", ObjectType = "tag" },
            new Permission { Name = "media.manage", ActionType = "manage", ObjectType = "media" },
            new Permission { Name = "settings.manage", ActionType = "manage", ObjectType = "setting" },
            new Permission { Name = "themes.manage", ActionType = "manage", ObjectType = "theme" },
        };

        foreach (var perm in permissions)
        {
            perm.Id = await _roleRepo.CreatePermissionAsync(perm);
        }

        // Admin gets ALL permissions
        foreach (var perm in permissions)
        {
            await _roleRepo.AssignPermissionToRoleAsync(perm.Id, adminRole.Id);
        }

        // Editor gets everything except settings & themes
        foreach (var perm in permissions.Where(p => p.Name != "settings.manage" && p.Name != "themes.manage"))
        {
            await _roleRepo.AssignPermissionToRoleAsync(perm.Id, editorRole.Id);
        }

        // Author gets: posts.edit, posts.publish, posts.delete, media.manage, comments.manage, categories.manage, tags.manage
        var authorPerms = new[] { "posts.edit", "posts.publish", "posts.delete", "media.manage", "comments.manage", "categories.manage", "tags.manage" };
        foreach (var perm in permissions.Where(p => authorPerms.Contains(p.Name)))
        {
            await _roleRepo.AssignPermissionToRoleAsync(perm.Id, authorRole.Id);
        }

        // Contributor gets: posts.edit only (can create drafts)
        var editPerm = permissions.First(p => p.Name == "posts.edit");
        await _roleRepo.AssignPermissionToRoleAsync(editPerm.Id, contributorRole.Id);
    }

    // ── Content Seeding ───────────────────────────────────────────────────────

    private async Task SeedCategoriesAsync()
    {
        var categories = new[]
        {
            new Category { Name = "Practice Management Software", Slug = "practice-management-software" },
            new Category { Name = "EHR & Electronic Records",     Slug = "ehr-electronic-records" },
            new Category { Name = "Digital Imaging & Diagnostics",Slug = "digital-imaging-diagnostics" },
            new Category { Name = "AI & Machine Learning in Optometry", Slug = "ai-machine-learning-optometry" },
            new Category { Name = "Teleoptometry & Remote Care",  Slug = "teleoptometry-remote-care" },
        };

        foreach (var category in categories)
        {
            await _categoryRepo.CreateAsync(category);
        }
    }

    private async Task SeedTagsAsync()
    {
        var tags = new[]
        {
            new Tag { Name = "Eyefinity",           Slug = "eyefinity" },
            new Tag { Name = "RevolutionEHR",       Slug = "revolution-ehr" },
            new Tag { Name = "AI Diagnostics",      Slug = "ai-diagnostics" },
            new Tag { Name = "OCT Software",        Slug = "oct-software" },
            new Tag { Name = "Telemedicine",        Slug = "telemedicine" },
            new Tag { Name = "HIPAA Compliance",    Slug = "hipaa-compliance" },
            new Tag { Name = "Virtual Try-On",      Slug = "virtual-try-on" },
        };

        foreach (var tag in tags)
        {
            await _tagRepo.CreateAsync(tag);
        }
    }

    private async Task SeedPagesAsync()
    {
        var pages = new[]
        {
            new Page
            {
                Title = "About Us",
                Slug = "about-us",
                Content = "<p>Welcome to our blog. We are passionate about sharing knowledge and inspiring others.</p>",
                AuthorId = Guid.Empty,
                AuthorName = "Admin",
                IsPublished = true,
                PublishedAt = DateTime.UtcNow
            },
            new Page
            {
                Title = "Contact",
                Slug = "contact",
                Content = "<p>Get in touch with us at hello@example.com.</p>",
                AuthorId = Guid.Empty,
                AuthorName = "Admin",
                IsPublished = true,
                PublishedAt = DateTime.UtcNow
            },
            new Page
            {
                Title = "Privacy Policy",
                Slug = "privacy-policy",
                Content = "<p>Your privacy is important to us. This is our privacy policy.</p>",
                AuthorId = Guid.Empty,
                AuthorName = "Admin",
                IsPublished = true,
                PublishedAt = DateTime.UtcNow
            },
        };

        foreach (var page in pages)
        {
            await _pageRepo.CreateAsync(page);
        }
    }

    private async Task SeedSettingsAsync()
    {
        var settings = new UserSettings
        {
            SiteName = "Optical Software",
            SiteDescription = "In-depth reviews, comparisons, and guides for optical software, EHR systems, and technology used in optometry practice.",
            PostsPerPage = 10,
            CommentsEnabled = true,
            CommentsModeration = true
        };

        await _settingRepo.SaveSettingsAsync(Guid.Empty, settings);
    }

    private async Task SeedPostsAndCommentsAsync()
    {
        var rng = new Random(42); // fixed seed for reproducible results

        var allCategories = await _categoryRepo.GetAllAsync(Guid.Empty);
        var allTags = await _tagRepo.GetAllAsync(Guid.Empty);

        var commenters = new[]
        {
            new { Name = "Eve Hacker", Email = "eve@example.com" },
            new { Name = "Frank Castle", Email = "frank@example.com" },
            new { Name = "Grace Hopper", Email = "grace@example.com" },
            new { Name = "Henry Ford", Email = "henry@example.com" },
        };

        var titles = new[]
        {
            "Getting Started with ASP.NET Core",
            "Building a RESTful API in C#",
            "Modern CSS Techniques You Should Know",
            "Introduction to Machine Learning",
            "Docker for .NET Developers",
            "JavaScript Best Practices in 2026",
            "Cloud Architecture Patterns",
            "Healthy Eating on a Budget",
            "Top Travel Destinations This Year",
            "Fitness Tips for Desk Workers",
            "Understanding Azure Functions",
            "React vs Vue: A Comparison",
            "The Future of Web Development",
            "Database Design Fundamentals",
            "A Guide to CI/CD Pipelines",
        };

        var words = new[] { "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit", "sed", "do", "eiusmod", "tempor", "incididunt", "ut", "labore", "et", "dolore", "magna", "aliqua" };

        for (int i = 0; i < titles.Length; i++)
        {
            var status = i < 13 ? PostStatus.Published : PostStatus.Draft;
            var publishDate = DateTime.UtcNow.AddDays(-rng.Next(5, 180));

            var contentLines = new List<string>();
            for (int p = 0; p < rng.Next(3, 6); p++)
            {
                var sentenceWords = Enumerable.Range(0, rng.Next(20, 40)).Select(_ => words[rng.Next(words.Length)]).ToArray();
                contentLines.Add($"<p>{string.Join(" ", sentenceWords)}.</p>");
            }

            var post = new Post
            {
                Title = titles[i],
                Slug = titles[i].ToLower().Replace(" ", "-").Replace("#", "sharp").Replace(":", "").Replace(".", ""),
                MetaDescription = $"A brief summary about {titles[i]}.",
                Html = string.Join("\n", contentLines),
                Plaintext = string.Join("\n", contentLines).Replace("<p>", "").Replace("</p>", ""),
                AuthorId = Guid.Empty,
                AuthorName = "Admin",
                Status = status,
                CreatedAt = publishDate.AddDays(-rng.Next(1, 5)),
                UpdatedAt = publishDate,
                PublishedAt = status == PostStatus.Published ? publishDate : null,
                ViewCount = rng.Next(10, 2000),
                AllowComments = true,
                Categories = allCategories.OrderBy(_ => rng.Next()).Take(rng.Next(1, 3)).ToList(),
                Tags = allTags.OrderBy(_ => rng.Next()).Take(rng.Next(1, 4)).ToList()
            };

            var postId = await _postRepo.CreateAsync(post);
            post.Id = postId;

            await _postRepo.AssignCategoriesAsync(postId, post.Categories.Select(c => c.Id).ToList());
            await _postRepo.AssignTagsAsync(postId, post.Tags.Select(t => t.Id).ToList());

            // Seed 0-3 comments with occasional replies for published posts
            if (status == PostStatus.Published)
            {
                int numComments = rng.Next(0, 4);
                var topLevelCommentIds = new List<Guid>();

                for (int c = 0; c < numComments; c++)
                {
                    var commenter = commenters[rng.Next(commenters.Length)];
                    var commentDate = publishDate.AddDays(rng.Next(1, 20));
                    if (commentDate > DateTime.UtcNow) commentDate = DateTime.UtcNow;

                    var commentWords = Enumerable.Range(0, rng.Next(8, 25)).Select(_ => words[rng.Next(words.Length)]).ToArray();

                    var comment = new Comment
                    {
                        PostId = postId,
                        PostTitle = post.Title,
                        AuthorName = commenter.Name,
                        AuthorEmail = commenter.Email,
                        Content = string.Join(" ", commentWords) + ".",
                        Status = CommentStatus.Approved,
                        CreatedAt = commentDate
                    };

                    var commentId = await _commentRepo.CreateAsync(comment);
                    topLevelCommentIds.Add(commentId);
                }

                // Add 1 reply to first comment if there are at least 2 comments
                if (topLevelCommentIds.Count >= 2)
                {
                    var replyCommenter = commenters[rng.Next(commenters.Length)];
                    var replyWords = Enumerable.Range(0, rng.Next(6, 15)).Select(_ => words[rng.Next(words.Length)]).ToArray();

                    var reply = new Comment
                    {
                        PostId = postId,
                        PostTitle = post.Title,
                        ParentId = topLevelCommentIds[0],
                        AuthorName = replyCommenter.Name,
                        AuthorEmail = replyCommenter.Email,
                        Content = string.Join(" ", replyWords) + ".",
                        Status = CommentStatus.Approved,
                        CreatedAt = DateTime.UtcNow.AddDays(-rng.Next(1, 10))
                    };

                    await _commentRepo.CreateAsync(reply);
                }
            }
        }
    }
}
