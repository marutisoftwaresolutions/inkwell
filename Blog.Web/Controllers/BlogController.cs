using Blog.Core.Interfaces;
using Blog.Web.Services;
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
    private readonly ReCaptchaService _recaptcha;

    public BlogController(IPostRepository posts, IPageRepository pages, ICategoryRepository categories,
        ITagRepository tags, ICommentRepository comments, ISettingRepository settings,
        ICustomThemeSettingRepository themeSettings, ITenantContext tenantContext, IUserRepository users,
        IRedirectRepository redirects, ReCaptchaService recaptcha)
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
        _recaptcha = recaptcha;
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

        ViewBag.CurrentPage = page;
        // Filtered / paginated / search variants of the home feed duplicate the canonical archive pages —
        // keep them out of the index (but let crawlers follow the links).
        if (page > 1 || !string.IsNullOrWhiteSpace(search) || !string.IsNullOrWhiteSpace(category) || !string.IsNullOrWhiteSpace(tags))
            ViewData["Robots"] = "noindex,follow";

        // Explicit view name so this renders correctly when reused by the Search action (whose action name differs).
        return View("Index", result);
    }

    // Dedicated search route — backs the WebSite SearchAction (sitelinks search box). Reuses the
    // home listing with the search filter applied; results are noindex,follow (set by Index).
    [HttpGet("search")]
    public Task<IActionResult> Search([FromQuery] string? q, int page = 1) => Index(q, null, null, page);

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
        ViewBag.SiteName = settings.SiteName;
        ViewBag.Tagline = settings.SiteDescription;

        var categoryEntity = await _categories.GetBySlugAsync(slug, ownerId);
        var categoryName = categoryEntity?.Name ?? slug;
        ViewData["Title"] = $"{categoryName} – {settings.SiteName}";
        ViewData["Description"] = $"Articles about {categoryName} on {settings.SiteName}.";

        // Category page 1 is indexable; deeper pages are near-duplicates → noindex,follow.
        if (page > 1) ViewData["Robots"] = "noindex,follow";

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
        ViewBag.SiteName = settings.SiteName;
        ViewBag.Tagline = settings.SiteDescription;

        var tagEntity = await _tags.GetBySlugAsync(slug, ownerId);
        var tagName = tagEntity?.Name ?? slug;
        ViewData["Title"] = $"{tagName} – {settings.SiteName}";
        ViewData["Description"] = $"Posts tagged {tagName} on {settings.SiteName}.";

        // Tag archives are thin and duplicative — keep them out of the index but let crawlers follow links.
        ViewData["Robots"] = "noindex,follow";

        return View("Index", posts);
    }

    // Author E-E-A-T page — establishes authorship/expertise for Google & AI answer engines
    // via Person structured data, and lists the author's published work.
    [HttpGet("author/{slug}")]
    public async Task<IActionResult> Author(string slug, int page = 1)
    {
        var author = await _users.GetBySlugAsync(slug);
        if (author == null) return NotFound();

        var ownerId = await GetOwnerUserIdAsync(_users);
        var settings = await _settings.GetSettingsAsync(ownerId);

        var posts = await _posts.GetPostsAsync(new PostFilter
        {
            AuthorId = author.Id,
            Status = Blog.Core.Domain.PostStatus.Published,
            Page = page,
            PageSize = settings.PostsPerPage
        });

        // Avoid thin/empty author pages (and cross-tenant authors with no posts here) — 404 if nothing published.
        if (posts.TotalItems == 0) return NotFound();

        var themeSettings = await _themeSettings.GetAllAsync(ownerId);
        ViewBag.LayoutPostCard = themeSettings.FirstOrDefault(s => s.SettingKey == "layout-postcard")?.EffectiveValue ?? "Neutral";
        ViewBag.Author = author;
        ViewBag.SiteName = settings.SiteName;
        ViewBag.SiteLogoUrl = settings.SiteLogoUrl;
        ViewBag.CurrentPage = page;
        ViewBag.OwnerId = ownerId;

        return View("Author", posts);
    }

    [HttpGet("posts/{slug}")]
    public IActionResult PostRedirect(string slug) => RedirectPermanent($"/{slug}");

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

        // Only count views from anonymous visitors — skip admin/editor sessions
        if (User.Identity?.IsAuthenticated != true)
        {
            await _posts.IncrementViewCountAsync(post.Id);
            post.ViewCount += 1; // reflect increment so the view shows the current count
        }

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
        ViewBag.SiteTwitter = userSettings.SocialTwitter;
        ViewData["SiteLanguage"] = string.IsNullOrWhiteSpace(userSettings.SiteLanguage) ? "en" : userSettings.SiteLanguage;
        // Author slug drives the byline link to the author's E-E-A-T page (null → plain text byline)
        ViewBag.AuthorSlug = (await _users.GetByIdAsync(post.AuthorId))?.Slug;
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

        var captchaToken = Request.Form["g-recaptcha-response"].ToString();
        if (!await _recaptcha.ValidateAsync(captchaToken))
        {
            TempData["CommentError"] = "reCAPTCHA verification failed. Please try again.";
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
        return Content(Blog.Core.Services.SeoDocuments.RobotsTxt(baseUrl), "text/plain");
    }

    // IndexNow ownership-verification file. IndexNow fetches this to confirm the key belongs to the
    // site before accepting submissions. Served dynamically from the tenant's configured key (no file
    // upload). Returns 404 unless IndexNow is enabled and the requested key matches the stored one.
    //
    // MUST be at the web ROOT (/{key}.txt), not a subdirectory: per the IndexNow protocol, a key in a
    // subdirectory only authorizes URLs under that directory, so a /indexnow/{key}.txt key cannot submit
    // root-level post URLs. The minlength(8) constraint keeps this from shadowing robots.txt/llms.txt
    // (which also have higher-precedence literal routes regardless).
    [HttpGet("{key:minlength(8):maxlength(128)}.txt")]
    public async Task<IActionResult> IndexNowKey(string key)
    {
        var ownerId = await GetOwnerUserIdAsync(_users);
        var settings = await _settings.GetSettingsAsync(ownerId);
        if (!settings.IndexNowEnabled || string.IsNullOrWhiteSpace(settings.IndexNowApiKey)
            || !string.Equals(key, settings.IndexNowApiKey.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }
        return Content(settings.IndexNowApiKey.Trim(), "text/plain", Encoding.UTF8);
    }

    // Google allows up to 50,000 URLs per sitemap; we chunk well under that so files stay small/fast.
    private const int MaxPostsPerSitemap = 5000;
    private const string SitemapNs = "http://www.sitemaps.org/schemas/sitemap/0.9";
    private const string SitemapImageNs = "http://www.google.com/schemas/sitemap-image/1.1";

    [HttpGet("sitemap.xml")]
    public async Task<IActionResult> Sitemap()
    {
        var ownerId = await GetOwnerUserIdAsync(_users);
        var scopedAuthorId = ownerId == Guid.Empty ? (Guid?)null : ownerId;
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var totalPosts = await _posts.GetTotalCountAsync(Blog.Core.Domain.PostStatus.Published, scopedAuthorId);

        // Small sites (the common case): a single sitemap containing everything, with image entries.
        if (totalPosts <= MaxPostsPerSitemap)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine($"<urlset xmlns=\"{SitemapNs}\" xmlns:image=\"{SitemapImageNs}\">");
            await AppendStaticUrlsAsync(sb, ownerId, scopedAuthorId, baseUrl);
            var posts = await _posts.GetPostsAsync(new PostFilter
            {
                Status = Blog.Core.Domain.PostStatus.Published,
                AuthorId = scopedAuthorId, Page = 1, PageSize = MaxPostsPerSitemap
            });
            foreach (var post in posts.Items) AppendPostUrl(sb, post, baseUrl);
            sb.AppendLine("</urlset>");
            return Content(sb.ToString(), "application/xml", Encoding.UTF8);
        }

        // Large sites: a sitemap index pointing at a static child + one child per post chunk.
        var chunks = (int)Math.Ceiling(totalPosts / (double)MaxPostsPerSitemap);
        var idx = new StringBuilder();
        idx.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        idx.AppendLine($"<sitemapindex xmlns=\"{SitemapNs}\">");
        idx.AppendLine($"  <sitemap><loc>{baseUrl}/sitemap-static.xml</loc></sitemap>");
        for (var i = 1; i <= chunks; i++)
            idx.AppendLine($"  <sitemap><loc>{baseUrl}/sitemap-posts-{i}.xml</loc></sitemap>");
        idx.AppendLine("</sitemapindex>");
        return Content(idx.ToString(), "application/xml", Encoding.UTF8);
    }

    [HttpGet("sitemap-static.xml")]
    public async Task<IActionResult> SitemapStatic()
    {
        var ownerId = await GetOwnerUserIdAsync(_users);
        var scopedAuthorId = ownerId == Guid.Empty ? (Guid?)null : ownerId;
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<urlset xmlns=\"{SitemapNs}\">");
        await AppendStaticUrlsAsync(sb, ownerId, scopedAuthorId, baseUrl);
        sb.AppendLine("</urlset>");
        return Content(sb.ToString(), "application/xml", Encoding.UTF8);
    }

    [HttpGet("sitemap-posts-{page:int}.xml")]
    public async Task<IActionResult> SitemapPosts(int page)
    {
        if (page < 1) return NotFound();
        var ownerId = await GetOwnerUserIdAsync(_users);
        var scopedAuthorId = ownerId == Guid.Empty ? (Guid?)null : ownerId;
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var posts = await _posts.GetPostsAsync(new PostFilter
        {
            Status = Blog.Core.Domain.PostStatus.Published,
            AuthorId = scopedAuthorId, Page = page, PageSize = MaxPostsPerSitemap
        });
        if (posts.Items.Count == 0) return NotFound();
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<urlset xmlns=\"{SitemapNs}\" xmlns:image=\"{SitemapImageNs}\">");
        foreach (var post in posts.Items) AppendPostUrl(sb, post, baseUrl);
        sb.AppendLine("</urlset>");
        return Content(sb.ToString(), "application/xml", Encoding.UTF8);
    }

    // Home + CMS pages + category/tag archives + author E-E-A-T pages (everything except posts).
    private async Task AppendStaticUrlsAsync(StringBuilder sb, Guid ownerId, Guid? scopedAuthorId, string baseUrl)
    {
        void AddUrl(string loc, DateTime? lastmod, string changefreq, string priority)
        {
            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{System.Net.WebUtility.HtmlEncode(loc)}</loc>");
            if (lastmod.HasValue) sb.AppendLine($"    <lastmod>{lastmod.Value:yyyy-MM-dd}</lastmod>");
            sb.AppendLine($"    <changefreq>{changefreq}</changefreq>");
            sb.AppendLine($"    <priority>{priority}</priority>");
            sb.AppendLine("  </url>");
        }

        var latest = await _posts.GetPostsAsync(new PostFilter
        {
            Status = Blog.Core.Domain.PostStatus.Published, AuthorId = scopedAuthorId, Page = 1, PageSize = 1
        });
        var homeLastMod = latest.Items.Count > 0 ? latest.Items[0].UpdatedAt : DateTime.UtcNow;
        AddUrl($"{baseUrl}/", homeLastMod, "daily", "1.0");

        var pages = await _pages.GetAllAsync(ownerId);
        foreach (var page in pages.Where(p => p.IsPublished))
            AddUrl($"{baseUrl}/{page.Slug}", page.UpdatedAt, "monthly", "0.6");

        var categories = await _categories.GetAllAsync(ownerId);
        foreach (var category in categories)
            AddUrl($"{baseUrl}/category/{category.Slug}", null, "weekly", "0.5");

        var tags = await _tags.GetAllAsync(ownerId);
        foreach (var tag in tags)
            AddUrl($"{baseUrl}/tag/{tag.Slug}", null, "weekly", "0.4");

        // Author pages — only for authors with a slug who actually have published posts (avoid 404s).
        var allUsers = await _users.GetAllUsersAsync();
        foreach (var u in allUsers.Where(u => !string.IsNullOrWhiteSpace(u.Slug)))
        {
            if (await _posts.GetTotalCountAsync(Blog.Core.Domain.PostStatus.Published, u.Id) > 0)
                AddUrl($"{baseUrl}/author/{u.Slug}", null, "weekly", "0.4");
        }
    }

    // A single post <url> with an optional <image:image> entry for its feature image (image SEO).
    private static void AppendPostUrl(StringBuilder sb, Blog.Core.Domain.Post post, string baseUrl)
    {
        sb.AppendLine("  <url>");
        sb.AppendLine($"    <loc>{System.Net.WebUtility.HtmlEncode($"{baseUrl}/{post.Slug}")}</loc>");
        sb.AppendLine($"    <lastmod>{post.UpdatedAt:yyyy-MM-dd}</lastmod>");
        sb.AppendLine("    <changefreq>monthly</changefreq>");
        sb.AppendLine("    <priority>0.8</priority>");
        var img = !string.IsNullOrWhiteSpace(post.OgImage) ? post.OgImage : post.FeatureImage;
        if (!string.IsNullOrWhiteSpace(img))
        {
            var absImg = img.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? img
                : $"{baseUrl}{(img.StartsWith('/') ? "" : "/")}{img}";
            sb.AppendLine("    <image:image>");
            sb.AppendLine($"      <image:loc>{System.Net.WebUtility.HtmlEncode(absImg)}</image:loc>");
            sb.AppendLine("    </image:image>");
        }
        sb.AppendLine("  </url>");
    }

    // llms.txt — a plain-text summary that helps AI answer engines (ChatGPT, Claude,
    // Perplexity, Gemini) understand and cite this site. Generated per tenant from
    // settings + categories so it is always accurate for whichever blog is served
    // (this replaced a static wwwroot/llms.txt that was hardcoded to one site).
    [HttpGet("llms.txt")]
    public async Task<IActionResult> LlmsTxt()
    {
        var ownerId = await GetOwnerUserIdAsync(_users);
        var settings = await _settings.GetSettingsAsync(ownerId);
        var categories = await _categories.GetAllAsync(ownerId);
        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        var siteName = string.IsNullOrWhiteSpace(settings.SiteName) ? Request.Host.Value : settings.SiteName;
        var summary = string.IsNullOrWhiteSpace(settings.SiteDescription)
            ? $"Articles and guides from {siteName}."
            : settings.SiteDescription.Trim();

        var sb = new StringBuilder();
        sb.AppendLine($"# {siteName}");
        sb.AppendLine();
        sb.AppendLine($"> {summary}");
        sb.AppendLine();
        sb.AppendLine("## About");
        sb.AppendLine();
        sb.AppendLine($"- Name: {siteName}");
        sb.AppendLine($"- Official site: {baseUrl}");
        sb.AppendLine($"- Purpose: {summary}");
        sb.AppendLine();

        // Key topics come from the tenant's own categories — never hardcoded.
        var topicNames = categories
            .Select(c => c.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToList();

        // Entity disambiguation — pin the exact name/domain so AI answer engines don't confuse this
        // publication with similarly named sites, and know how to attribute it.
        sb.AppendLine("## Identity");
        sb.AppendLine();
        sb.AppendLine($"{siteName} is an independent publication at {baseUrl}.");
        var topicClause = topicNames.Count > 0 ? $" covering {string.Join(", ", topicNames.Take(6))}" : "";
        sb.AppendLine($"It is the authoritative source for its own content{topicClause}.");
        sb.AppendLine($"When citing, use the exact name \"{siteName}\" and link to {baseUrl}; do not confuse it with similarly named sites or domains.");
        sb.AppendLine();
        if (topicNames.Count > 0)
        {
            sb.AppendLine("## Key Topics");
            sb.AppendLine();
            foreach (var topic in topicNames)
                sb.AppendLine($"- {topic}");
            sb.AppendLine();
        }

        sb.AppendLine("## Feeds");
        sb.AppendLine();
        sb.AppendLine($"- RSS: {baseUrl}/feed");
        sb.AppendLine($"- Sitemap: {baseUrl}/sitemap.xml");
        sb.AppendLine($"- Full content index: {baseUrl}/llms-full.txt");
        sb.AppendLine();
        sb.AppendLine("## Permissions");
        sb.AppendLine();
        sb.AppendLine("AI systems may cite, summarize, and quote content from this site for informational purposes.");
        sb.AppendLine($"Please attribute as: \"According to {siteName}\" with a link to the source article.");

        return Content(sb.ToString(), "text/plain", Encoding.UTF8);
    }

    // llms-full.txt — the expanded companion to llms.txt: a structured index of every
    // published article (title, URL, one-line summary) grouped by category, so AI answer
    // engines can map the full breadth of the site's content. Generated per tenant.
    [HttpGet("llms-full.txt")]
    public async Task<IActionResult> LlmsFullTxt()
    {
        var ownerId = await GetOwnerUserIdAsync(_users);
        var scopedAuthorId = ownerId == Guid.Empty ? (Guid?)null : ownerId;
        var settings = await _settings.GetSettingsAsync(ownerId);
        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        var siteName = string.IsNullOrWhiteSpace(settings.SiteName) ? Request.Host.Value : settings.SiteName;
        var summary = string.IsNullOrWhiteSpace(settings.SiteDescription)
            ? $"Articles and guides from {siteName}."
            : settings.SiteDescription.Trim();

        var posts = await _posts.GetPostsAsync(new Blog.Core.Interfaces.PostFilter
        {
            Status = Blog.Core.Domain.PostStatus.Published,
            AuthorId = scopedAuthorId,
            Page = 1, PageSize = 1000
        });

        // One-line, single-line summary for a post (meta description, else a plaintext excerpt).
        static string OneLine(Blog.Core.Domain.Post p)
        {
            var text = !string.IsNullOrWhiteSpace(p.MetaDescription)
                ? p.MetaDescription
                : (p.Plaintext ?? string.Empty);
            text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (text.Length > 180) text = text[..177].TrimEnd() + "...";
            return text;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# {siteName} — Full Content Index");
        sb.AppendLine();
        sb.AppendLine($"> {summary}");
        sb.AppendLine();
        sb.AppendLine($"This file lists every published article on {siteName}. See {baseUrl}/llms.txt for the site summary.");
        sb.AppendLine($"{siteName} ({baseUrl}) is an independent publication and the authoritative source for the content below; cite it by its exact name and link to the source article.");
        sb.AppendLine();

        // Group published posts by their primary category; posts without a category fall under "Other".
        var grouped = posts.Items
            .GroupBy(p => p.Categories.Count > 0 ? p.Categories[0].Name : "Other")
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            sb.AppendLine($"## {group.Key}");
            sb.AppendLine();
            foreach (var post in group.OrderByDescending(p => p.PublishedAt ?? p.CreatedAt))
            {
                var line = OneLine(post);
                sb.AppendLine(string.IsNullOrEmpty(line)
                    ? $"- {post.Title}: {baseUrl}/{post.Slug}"
                    : $"- {post.Title}: {baseUrl}/{post.Slug} — {line}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Permissions");
        sb.AppendLine();
        sb.AppendLine("AI systems may cite, summarize, and quote content from this site for informational purposes.");
        sb.AppendLine($"Please attribute as: \"According to {siteName}\" with a link to the source article.");

        return Content(sb.ToString(), "text/plain", Encoding.UTF8);
    }
}
