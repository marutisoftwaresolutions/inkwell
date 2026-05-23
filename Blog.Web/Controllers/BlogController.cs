using Blog.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text;

namespace Blog.Web.Controllers;

public class BlogController : Controller
{
    private readonly IPostRepository _posts;
    private readonly IPageRepository _pages;
    private readonly ICategoryRepository _categories;
    private readonly ITagRepository _tags;
    private readonly ICommentRepository _comments;
    private readonly ISettingRepository _settings;
    private readonly ICustomThemeSettingRepository _themeSettings;
    private readonly ITenantContext _tenantContext;
    private readonly IUserRepository _users;
    private readonly IRedirectRepository _redirects;

    public BlogController(IPostRepository posts, IPageRepository pages, ICategoryRepository categories,
        ITagRepository tags, ICommentRepository comments, ISettingRepository settings,
        ICustomThemeSettingRepository themeSettings, ITenantContext tenantContext, IUserRepository users,
        IRedirectRepository redirects)
    {
        _posts = posts;
        _pages = pages;
        _categories = categories;
        _tags = tags;
        _comments = comments;
        _settings = settings;
        _themeSettings = themeSettings;
        _tenantContext = tenantContext;
        _users = users;
        _redirects = redirects;
    }

    /// <summary>Gets the owner user ID for data scoping — uses tenant context if resolved, else falls back to primary site owner (first admin).</summary>
    private async Task<Guid> GetOwnerUserIdAsync(IUserRepository userRepo)
    {
        if (_tenantContext.IsCloudMode)
        {
            if (_tenantContext.IsResolved)
                return _tenantContext.UserId;

            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(raw, out var id))
                return id;
        }
            
        var admin = await userRepo.GetFirstAdminAsync();
        return admin?.Id ?? Guid.Empty;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] string? search, [FromQuery] string? category, [FromQuery] string? tags, int page = 1)
    {
        var ownerId = await GetOwnerUserIdAsync(_users);
        // Site-wide settings come from Guid.Empty in Self-Hosted mode; per-user in Cloud
        var settingsId = _tenantContext.IsCloudMode ? ownerId : ownerId;
        var userSettings = await _settings.GetSettingsAsync(settingsId);
        var pageSize = userSettings.PostsPerPage;
        
        var filter = new Blog.Core.Interfaces.PostFilter
        {
            Search = search,
            Status = Blog.Core.Domain.PostStatus.Published,
            AuthorId = ownerId == Guid.Empty ? null : ownerId,
            Page = page, 
            PageSize = pageSize
        };

        ViewBag.OwnerId = settingsId;

        if (!string.IsNullOrWhiteSpace(category))
        {
            filter.Categories = category.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
        }

        if (!string.IsNullOrWhiteSpace(tags))
        {
            filter.Tags = tags.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
        }

        var result = await _posts.GetPostsAsync(filter);
        
        ViewBag.SiteName = userSettings.SiteName;
        ViewBag.Tagline = userSettings.SiteDescription;
        ViewBag.Categories = await _categories.GetAllAsync(ownerId);
        ViewBag.Tags = await _tags.GetAllAsync(ownerId);
        ViewBag.SelectedCategories = filter.Categories;
        ViewBag.SelectedTags = filter.Tags;
        ViewBag.SearchTerm = search;

        // Popular posts for sidebar (top 4 by view count)
        var popularFilter = new Blog.Core.Interfaces.PostFilter
        {
            Status = Blog.Core.Domain.PostStatus.Published,
            AuthorId = ownerId == Guid.Empty ? null : ownerId,
            Page = 1,
            PageSize = 4
        };
        var popularResult = await _posts.GetPostsAsync(popularFilter);
        ViewBag.PopularPosts = popularResult.Items.OrderByDescending(p => p.ViewCount).Take(4).ToList();

        // Layout variant
        var settingsList = await _themeSettings.GetAllAsync(ownerId);
        ViewBag.LayoutIndex = settingsList.FirstOrDefault(s => s.SettingKey == "layout-index")?.EffectiveValue ?? "Neutral";
        ViewBag.LayoutPostCard = settingsList.FirstOrDefault(s => s.SettingKey == "layout-postcard")?.EffectiveValue ?? "Neutral";

        return View(result);
    }

    [HttpGet("category/{slug}")]
    public async Task<IActionResult> Category(string slug, int page = 1)
    {
        var ownerId = await GetOwnerUserIdAsync(_users);
        var settings = await _settings.GetSettingsAsync(ownerId);
        ViewBag.OwnerId = ownerId;
        
        var posts = await _posts.GetPostsAsync(new PostFilter 
        { 
            Categories = new List<string> { slug },
            Page = page,
            PageSize = settings.PostsPerPage,
            Status = Blog.Core.Domain.PostStatus.Published,
            AuthorId = ownerId == Guid.Empty ? null : ownerId
        });

        ViewBag.CurrentCategory = slug;
        ViewBag.CurrentPage = page;
        ViewBag.Settings = settings;

        return View("Index", posts);
    }

    [HttpGet("tag/{slug}")]
    public async Task<IActionResult> Tag(string slug, int page = 1)
    {
        var ownerId = await GetOwnerUserIdAsync(_users);
        var settings = await _settings.GetSettingsAsync(ownerId);
        ViewBag.OwnerId = ownerId;
        
        var posts = await _posts.GetPostsAsync(new PostFilter
        {
            Tags = new List<string> { slug },
            Page = page,
            PageSize = settings.PostsPerPage,
            Status = Blog.Core.Domain.PostStatus.Published,
            AuthorId = ownerId == Guid.Empty ? null : ownerId
        });

        ViewBag.CurrentTag = slug;
        ViewBag.CurrentPage = page;
        ViewBag.Settings = settings;

        return View("Index", posts);
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Post(string slug)
    {
        var ownerId = await GetOwnerUserIdAsync(_users);

        // Check if it's a page first
        var page = await _pages.GetBySlugAsync(slug);
        if (page != null) return View("Page", page);

        var post = await _posts.GetBySlugAsync(slug);
        if (post == null || (post.Status != Blog.Core.Domain.PostStatus.Published && !(post.Status == Blog.Core.Domain.PostStatus.Scheduled && post.ScheduledAt <= DateTime.Now)))
        {
            // Check the Redirects table — slug may have changed (e.g. year update)
            var destination = await _redirects.GetDestinationAsync($"/{slug}");
            if (destination != null)
                return RedirectPermanent(destination);
            return NotFound();
        }

        await _posts.IncrementViewCountAsync(post.Id);

        // Build nested comment tree
        var flatComments = await _comments.GetApprovedForPostAsync(post.Id);
        var commentLookup = flatComments.ToDictionary(c => c.Id);
        var rootComments = new List<Blog.Core.Domain.Comment>();
        foreach (var comment in flatComments)
        {
            if (comment.ParentId.HasValue && commentLookup.TryGetValue(comment.ParentId.Value, out var parent))
            {
                comment.Depth = Math.Min(parent.Depth + 1, 3);
                parent.Replies.Add(comment);
            }
            else
            {
                comment.Depth = 0;
                rootComments.Add(comment);
            }
        }
        ViewBag.Comments = rootComments;

        var settingsId = _tenantContext.IsCloudMode ? ownerId : ownerId;
        ViewBag.OwnerId = settingsId;

        var userSettings = await _settings.GetSettingsAsync(settingsId);
        ViewBag.CommentsEnabled = userSettings.CommentsEnabled;
        ViewBag.SiteLogoUrl = userSettings.SiteLogoUrl;
        ViewBag.Categories = await _categories.GetAllAsync(ownerId);
        var settings = await _themeSettings.GetAllAsync(ownerId);
        var layoutIndexSetting = settings.FirstOrDefault(s => s.SettingKey == "layout-index");
        var layoutPostCardSetting = settings.FirstOrDefault(s => s.SettingKey == "layout-postcard");
        var layoutPostSetting = settings.FirstOrDefault(s => s.SettingKey == "layout-post");
        var layoutPageSetting = settings.FirstOrDefault(s => s.SettingKey == "layout-page");

        ViewBag.LayoutIndex = layoutIndexSetting?.EffectiveValue ?? "Neutral";
        ViewBag.LayoutPostCard = layoutPostCardSetting?.EffectiveValue ?? "Neutral";
        ViewBag.LayoutPost = layoutPostSetting?.EffectiveValue ?? "Neutral";
        ViewBag.LayoutPage = layoutPageSetting?.EffectiveValue ?? "Neutral";

        ViewBag.Tags = await _tags.GetAllAsync(ownerId);

        var relatedPosts = await _posts.GetRelatedPostsAsync(
            post.Id,
            post.Categories.Select(c => c.Id).ToList(),
            post.Tags.Select(t => t.Id).ToList(),
            count: 5);
        ViewBag.RelatedPosts = relatedPosts;
        // First 2 used as See Also text links (internal linking), rest used as discovery cards
        ViewBag.SeeAlsoPosts = relatedPosts.Take(2).ToList();
        ViewBag.RelatedPostCards = relatedPosts.Skip(2).Take(3).ToList();

        return View(post);
    }

    [HttpPost("{slug}/comment")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddComment(string slug, string authorName, string authorEmail, string content, Guid? parentId)
    {
        var post = await _posts.GetBySlugAsync(slug);
        if (post == null) return NotFound();

        if (string.IsNullOrWhiteSpace(authorName) || string.IsNullOrWhiteSpace(authorEmail) || string.IsNullOrWhiteSpace(content))
        {
            TempData["CommentError"] = "All fields are required.";
            return RedirectToAction("Post", new { slug });
        }

        var ownerId = await GetOwnerUserIdAsync(_users);
        var moderation = (await _settings.GetSettingsAsync(ownerId)).CommentsModeration;
        var comment = new Blog.Core.Domain.Comment
        {
            PostId = post.Id,
            AuthorName = authorName,
            AuthorEmail = authorEmail,
            Content = content,
            ParentId = parentId,
            Status = Blog.Core.Domain.CommentStatus.Pending,
            AuthorIp = HttpContext.Connection.RemoteIpAddress?.ToString()
        };

        await _comments.CreateAsync(comment);
        TempData["CommentSuccess"] = moderation
            ? "Your comment is awaiting moderation."
            : "Comment posted!";
        return RedirectToAction("Post", new { slug });
    }

    [HttpGet("feed")]
    public async Task<IActionResult> Feed()
    {
        var ownerId = await GetOwnerUserIdAsync(_users);
        var posts = await _posts.GetPostsAsync(new Blog.Core.Interfaces.PostFilter
        {
            Status = Blog.Core.Domain.PostStatus.Published,
            AuthorId = ownerId == Guid.Empty ? null : ownerId,
            Page = 1, PageSize = 20
        });
        var userSettings = await _settings.GetSettingsAsync(ownerId);
        var siteName = userSettings.SiteName;
        var siteTagline = userSettings.SiteDescription;
        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<rss version=\"2.0\" xmlns:atom=\"http://www.w3.org/2005/Atom\">");
        sb.AppendLine("  <channel>");
        sb.AppendLine($"    <title>{System.Net.WebUtility.HtmlEncode(siteName)}</title>");
        sb.AppendLine($"    <link>{baseUrl}</link>");
        sb.AppendLine($"    <description>{System.Net.WebUtility.HtmlEncode(siteTagline)}</description>");
        sb.AppendLine($"    <atom:link href=\"{baseUrl}/feed\" rel=\"self\" type=\"application/rss+xml\" />");

        foreach (var post in posts.Items)
        {
            sb.AppendLine("    <item>");
            sb.AppendLine($"      <title>{System.Net.WebUtility.HtmlEncode(post.Title)}</title>");
            sb.AppendLine($"      <link>{baseUrl}/{post.Slug}</link>");
            sb.AppendLine($"      <guid>{baseUrl}/{post.Slug}</guid>");
            sb.AppendLine($"      <pubDate>{post.PublishedAt:R}</pubDate>");
            sb.AppendLine($"      <description>{System.Net.WebUtility.HtmlEncode(post.MetaDescription)}</description>");
            sb.AppendLine("    </item>");
        }

        sb.AppendLine("  </channel>");
        sb.AppendLine("</rss>");

        return Content(sb.ToString(), "application/rss+xml", Encoding.UTF8);
    }

    [HttpGet("robots.txt")]
    public IActionResult RobotsTxt()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var content = $@"User-agent: *
Allow: /
Disallow: /admin/
Disallow: /account/
Disallow: /setup/
Disallow: /api/

# AI answer engines — explicitly permitted for citation and indexing
User-agent: GPTBot
Allow: /

User-agent: ClaudeBot
Allow: /

User-agent: anthropic-ai
Allow: /

User-agent: PerplexityBot
Allow: /

User-agent: Googlebot-Extended
Allow: /

User-agent: Applebot-Extended
Allow: /

User-agent: FacebookBot
Allow: /

# AI mass-training scrapers — blocked
User-agent: CCBot
Disallow: /

User-agent: omgili
Disallow: /

Sitemap: {baseUrl}/sitemap.xml
";
        return Content(content, "text/plain");
    }

    [HttpGet("sitemap.xml")]
    public async Task<IActionResult> Sitemap()
    {
        var ownerId = await GetOwnerUserIdAsync(_users);
        var posts = await _posts.GetPostsAsync(new Blog.Core.Interfaces.PostFilter
        {
            Status = Blog.Core.Domain.PostStatus.Published,
            AuthorId = ownerId == Guid.Empty ? null : ownerId,
            Page = 1, PageSize = 1000
        });
        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        sb.AppendLine($"  <url><loc>{baseUrl}/</loc><changefreq>daily</changefreq><priority>1.0</priority></url>");

        foreach (var post in posts.Items)
        {
            sb.AppendLine($"  <url>");
            sb.AppendLine($"    <loc>{baseUrl}/{post.Slug}</loc>");
            sb.AppendLine($"    <lastmod>{post.UpdatedAt:yyyy-MM-dd}</lastmod>");
            sb.AppendLine($"    <changefreq>monthly</changefreq>");
            sb.AppendLine($"    <priority>0.8</priority>");
            sb.AppendLine($"  </url>");
        }

        sb.AppendLine("</urlset>");
        return Content(sb.ToString(), "application/xml", Encoding.UTF8);
    }
}
