using System.Text.Json;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services.Export;
using Blog.Core.Services.Export.Push;

namespace Blog.Web.Services.Export.Push;

/// <summary>The outcome of starting or resuming a push: the push, or a message and the form field it concerns.</summary>
public sealed record PushStartResult(ExportPush? Push, string? Error, string? Field)
{
    public static PushStartResult Fail(string error, string? field = null) => new(null, error, field);
}

/// <summary>What one run of the push job did, for the scheduler's one-line report.</summary>
public sealed record PushRunReport(int Sent, int Skipped, int Retrying, int Failed, int Finished, int Paused);

/// <summary>
/// Direct push to WordPress and Ghost (ADR-0029). Starting a push happens in the admin's request: the address
/// and credential are checked against the target (preflight), the push and one item per post and page are
/// recorded, and the credential is kept in memory only. The work is done by <see cref="RunAsync"/>, called by
/// <c>ExportPushJob</c> every minute, a batch at a time, so a push survives a closed tab, a rate limit and a
/// restart (after which the operator enters the credential again).
/// </summary>
public sealed class ExportPushService
{
    public const int DefaultBatchSize = 25;
    public static readonly TimeSpan TimeBudget = TimeSpan.FromSeconds(40);

    /// <summary>
    /// The longest one item may take (find, image uploads, save). The scheduler runs jobs one after another, so a slow
    /// target must not hold scheduled publishing, newsletters and webhooks for minutes. An item that runs out of time
    /// counts as an attempt and is retried with the usual backoff, so one that is always too slow ends as Failed.
    /// </summary>
    public static readonly TimeSpan ItemDeadline = TimeSpan.FromSeconds(50);
    public static readonly TimeSpan PreflightTimeout = TimeSpan.FromSeconds(15);

    private readonly IExportPushRepository _repo;
    private readonly IExportSource _source;
    private readonly IPushClientFactory _clients;
    private readonly PushCredentialStore _credentials;
    private readonly AuditService _audit;
    private readonly IConfiguration _config;
    private readonly ILogger<ExportPushService> _log;
    private readonly string _uploadsRoot;

    public ExportPushService(IExportPushRepository repo, IExportSource source, IPushClientFactory clients, PushCredentialStore credentials,
        AuditService audit, IWebHostEnvironment env, IConfiguration config, ILogger<ExportPushService> log)
    {
        _repo = repo;
        _source = source;
        _clients = clients;
        _credentials = credentials;
        _audit = audit;
        _config = config;
        _log = log;
        _uploadsRoot = Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"), "uploads");
    }

    public int BatchSize => Math.Clamp(_config.GetValue("Exports:PushBatchSize", DefaultBatchSize), 1, 200);

    /// <summary>The per-item deadline (<c>Exports:PushItemSeconds</c>, default 50, 1–300).</summary>
    public TimeSpan ItemTime => TimeSpan.FromSeconds(Math.Clamp(_config.GetValue("Exports:PushItemSeconds", (int)ItemDeadline.TotalSeconds), 1, 300));
    public bool AllowPrivateTargets => _clients.AllowPrivateTargets;

    // ── Start, resume, cancel (the admin's request) ──────────────────────────

    public async Task<PushStartResult> StartAsync(ExportTarget target, string? siteInput, string? userName, string? secret,
        PushConflictPolicy policy, ExportOptions options, string sourceBaseUrl, Guid startedBy, string? startedByName, CancellationToken ct)
    {
        var (site, siteError) = PushRules.NormaliseSiteUrl(siteInput, _clients.AllowPrivateTargets);
        if (siteError is not null) return PushStartResult.Fail(siteError, "pushSite");
        var (credential, credentialError, field) = Credential(target, userName, secret);
        if (credentialError is not null) return PushStartResult.Fail(credentialError, field);

        var active = await _repo.GetActiveAsync();
        if (active is not null)
            return PushStartResult.Fail($"A push to {active.SiteUrl} is not finished yet. Wait for it, or cancel it, before starting another.");

        var preflight = await PreflightAsync(target, site!, credential!, options.IncludePages, ct);
        if (!preflight.Ok) return PushStartResult.Fail(preflight.Error ?? "The site could not be checked.", preflight.Field ?? "pushSite");

        // A push never sends comments or email addresses (fidelity screen); only these two choices apply.
        var selection = new ExportOptions { IncludeDrafts = options.IncludeDrafts, IncludePages = options.IncludePages, IncludeComments = false };
        var items = PushRules.Order(await _source.ListItemsAsync(selection));
        if (items.Count == 0) return PushStartResult.Fail("Nothing matches these choices, so there is nothing to push.");

        var now = DateTime.UtcNow;
        var push = new ExportPush
        {
            Id = Guid.NewGuid(), Target = target, SiteUrl = site!, ConflictPolicy = policy,
            IncludeDrafts = selection.IncludeDrafts, IncludePages = selection.IncludePages,
            SourceBaseUrl = sourceBaseUrl.TrimEnd('/'), Status = PushStatus.Running,
            Message = preflight.SiteName is null ? null : $"Pushing to “{preflight.SiteName}”.",
            StartedBy = startedBy, StartedByName = startedByName, StartedAt = now, UpdatedAt = now,
        };
        await _repo.CreateAsync(push, items);
        _credentials.Set(push.Id, credential!);
        await _audit.LogAsync(AuditActions.ExportPushStarted, "Export", push.Id.ToString(), $"{target} push to {push.SiteUrl}",
            newValues: Summary(push) + $"; {items.Count} items");
        return new PushStartResult(push, null, null);
    }

    public async Task<PushStartResult> ResumeAsync(Guid pushId, string? userName, string? secret, CancellationToken ct)
    {
        var push = await _repo.GetAsync(pushId);
        if (push is null) return PushStartResult.Fail("That push no longer exists.");
        if (push.Status != PushStatus.Paused) return PushStartResult.Fail("Only a paused push can be resumed.");
        var (credential, error, field) = Credential(push.Target, userName, secret);
        if (error is not null) return PushStartResult.Fail(error, field);
        var preflight = await PreflightAsync(push.Target, push.SiteUrl, credential!, push.IncludePages, ct);
        if (!preflight.Ok) return PushStartResult.Fail(preflight.Error ?? "The site could not be checked.", preflight.Field ?? "pushSite");

        _credentials.Set(push.Id, credential!);
        await _repo.SetStatusAsync(push.Id, PushStatus.Running, "Resumed.", null, DateTime.UtcNow, null);
        await _audit.LogAsync(AuditActions.ExportPushResumed, "Export", push.Id.ToString(), $"{push.Target} push to {push.SiteUrl}",
            newValues: Summary(push));
        push.Status = PushStatus.Running;
        return new PushStartResult(push, null, null);
    }

    public async Task<bool> CancelAsync(Guid pushId)
    {
        var push = await _repo.GetAsync(pushId);
        if (push is null || !push.IsActive) return false;
        var now = DateTime.UtcNow;
        var cancelled = await _repo.CancelPendingAsync(push.Id, now);
        await _repo.SetStatusAsync(push.Id, PushStatus.Cancelled, cancelled == 1 ? "Cancelled; 1 item was not sent." : $"Cancelled; {cancelled} items were not sent.", null, now, now);
        _credentials.Forget(push.Id);
        var counts = await _repo.CountAsync(push.Id);
        await _audit.LogAsync(AuditActions.ExportPushCancelled, "Export", push.Id.ToString(), $"{push.Target} push to {push.SiteUrl}",
            newValues: Summary(push) + "; " + Counts(counts));
        return true;
    }

    public Task<ExportPush?> GetAsync(Guid id) => _repo.GetAsync(id);
    public Task<IReadOnlyList<ExportPush>> ListRecentAsync(int take = 10) => _repo.ListRecentAsync(take);
    public Task<ExportPushCounts> CountAsync(Guid id) => _repo.CountAsync(id);
    public Task<IReadOnlyList<ExportPushItem>> ItemsAsync(Guid id, PushItemStatus? status) => _repo.ListContentItemsAsync(id, status);
    public Task<IReadOnlyList<ExportPushItem>> SearchItemsAsync(Guid id, PushItemStatus? status, string? query, int page, int pageSize) => _repo.SearchContentItemsAsync(id, status, query, page, pageSize);
    public Task<int> CountItemsAsync(Guid id, PushItemStatus? status, string? query) => _repo.CountContentItemsAsync(id, status, query);

    /// <summary>Finished and cancelled pushes are kept this long for their outcome pages, then removed.</summary>
    public static readonly TimeSpan KeepFinishedFor = TimeSpan.FromDays(90);
    public Task<int> PruneFinishedAsync() => _repo.PruneFinishedAsync(DateTime.UtcNow - KeepFinishedFor);

    private async Task<PushPreflight> PreflightAsync(ExportTarget target, string site, PushCredential credential, bool needPages, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(PreflightTimeout);
        try
        {
            return await _clients.Create(target, site, credential).PreflightAsync(needPages, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new PushPreflight(false, null, null, $"The site did not answer within {PreflightTimeout.TotalSeconds:0} seconds.", "pushSite");
        }
        catch (PushException ex)
        {
            return new PushPreflight(false, null, null, ex.Message, "pushSite");
        }
    }

    private static (PushCredential? Credential, string? Error, string? Field) Credential(ExportTarget target, string? userName, string? secret)
    {
        if (target == ExportTarget.WordPress)
        {
            if (string.IsNullOrWhiteSpace(userName)) return (null, "Enter the WordPress user name the application password belongs to.", "pushUser");
            if (string.IsNullOrWhiteSpace(secret)) return (null, "Enter the application password from WordPress → Users → Profile → Application Passwords.", "pushSecret");
            return (new PushCredential(target, userName.Trim(), secret.Trim()), null, null);
        }
        var (_, _, error) = PushRules.ParseGhostKey(secret);
        return error is not null ? (null, error, "pushSecret") : (new PushCredential(target, null, secret!.Trim()), null, null);
    }

    private static string Summary(ExportPush p) =>
        $"{p.Target} at {p.SiteUrl}; existing items {p.ConflictPolicy.ToString().ToLowerInvariant()}; " +
        $"drafts {(p.IncludeDrafts ? "included" : "excluded")}; pages {(p.IncludePages ? "included" : "excluded")}";

    private static string Counts(ExportPushCounts c) =>
        $"{c.Created} created, {c.Updated} updated, {c.Skipped} skipped, {c.Failed} failed" + (c.Cancelled > 0 ? $", {c.Cancelled} not sent" : "");

    // ── The work (the push job) ──────────────────────────────────────────────

    /// <summary>
    /// Works through running pushes for up to <see cref="TimeBudget"/> or <see cref="BatchSize"/> items. A push
    /// whose credential this process does not hold (a restart) is paused for the operator; a rate limit pauses
    /// it until the target's <c>Retry-After</c>; a rejected credential pauses it and forgets the credential.
    /// </summary>
    public async Task<PushRunReport> RunAsync(CancellationToken ct)
    {
        int sent = 0, skipped = 0, retrying = 0, failed = 0, finished = 0, paused = 0;
        var started = DateTime.UtcNow;
        var budget = BatchSize;

        foreach (var push in await _repo.GetRunningAsync())
        {
            if (ct.IsCancellationRequested || budget <= 0 || DateTime.UtcNow - started >= TimeBudget) break;
            if (push.ResumeAt is DateTime resume && resume > DateTime.UtcNow) continue;

            var credential = _credentials.Get(push.Id);
            if (credential is null)
            {
                await PauseAsync(push, "The server restarted, and the password or key is never stored. Enter it again to resume.");
                paused++;
                continue;
            }

            var client = _clients.Create(push.Target, push.SiteUrl, credential);
            var catalog = await _source.LoadCatalogAsync(new ExportOptions { IncludeDrafts = push.IncludeDrafts, IncludePages = push.IncludePages });
            var stop = false;

            while (!stop && budget > 0 && DateTime.UtcNow - started < TimeBudget && !ct.IsCancellationRequested)
            {
                var due = await _repo.GetDueAsync(push.Id, DateTime.UtcNow, Math.Min(budget, 10));
                if (due.Count == 0) break;
                foreach (var item in due)
                {
                    if (budget <= 0 || DateTime.UtcNow - started >= TimeBudget) break;
                    budget--;
                    using var itemCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    itemCts.CancelAfter(ItemTime);
                    try
                    {
                        var outcome = await SendItemAsync(push, client, catalog, item, itemCts.Token);
                        if (outcome == PushItemStatus.Skipped) skipped++; else sent++;
                    }
                    catch (PushException ex) when (ex.IsThrottle)
                    {
                        var wait = ex.RetryAfter ?? PushRules.DefaultRetryAfter;
                        var until = DateTime.UtcNow + wait;
                        await _repo.SetStatusAsync(push.Id, PushStatus.Running,
                            $"The site asked us to slow down; continuing after {until:HH:mm} UTC.", until, DateTime.UtcNow, null);
                        stop = true;
                        break;
                    }
                    catch (PushException ex) when (ex.IsAuthFailure)
                    {
                        await MarkFailureAsync(item, ex.Message, countAttempt: false);
                        await PauseAsync(push, ex.Message + " Enter the credential again to resume.");
                        paused++;
                        stop = true;
                        break;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        stop = true;
                        break;
                    }
                    catch (OperationCanceledException) when (itemCts.IsCancellationRequested)
                    {
                        // Counted as an attempt: an item that is always too slow (one huge image) ends as Failed after
                        // the usual retries instead of holding the push open for ever.
                        if (await MarkFailureAsync(item, "The site took too long to answer; this item is tried again later.", countAttempt: true)) failed++; else retrying++;
                        stop = true;   // give the scheduler back; the site is slow right now
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (ex is not PushException) _log.LogWarning(ex, "Push {PushId}: item {ItemId} failed.", push.Id, item.Id);
                        var message = ex is PushException or JsonException or IOException ? ex.Message : "An unexpected error occurred; the Error Monitor log has the details.";
                        if (await MarkFailureAsync(item, message, countAttempt: true)) failed++; else retrying++;
                    }
                }
            }

            if (!stop && await FinishIfDoneAsync(push)) finished++;
        }
        return new PushRunReport(sent, skipped, retrying, failed, finished, paused);
    }

    private async Task PauseAsync(ExportPush push, string message)
    {
        _credentials.Forget(push.Id);
        await _repo.SetStatusAsync(push.Id, PushStatus.Paused, message, null, DateTime.UtcNow, null);
        await _audit.LogAsync(AuditActions.ExportPushPaused, "Export", push.Id.ToString(), $"{push.Target} push to {push.SiteUrl}",
            newValues: PushRules.Trim(message, 300), userIdOverride: push.StartedBy, userNameOverride: push.StartedByName);
    }

    /// <summary>Records a failed attempt: retried later, or failed for good after <see cref="PushRules.MaxAttempts"/>. True when final.</summary>
    private async Task<bool> MarkFailureAsync(ExportPushItem item, string message, bool countAttempt)
    {
        var attempts = item.Attempts + (countAttempt ? 1 : 0);
        var now = DateTime.UtcNow;
        if (countAttempt && attempts >= PushRules.MaxAttempts)
        {
            await _repo.MarkItemAsync(item.Id, PushItemStatus.Failed, attempts, now, null, null, message, now);
            return true;
        }
        await _repo.MarkItemAsync(item.Id, PushItemStatus.Pending, attempts, countAttempt ? now + PushRules.RetryDelay(attempts) : now,
            null, null, message, now);
        return false;
    }

    private async Task<bool> FinishIfDoneAsync(ExportPush push)
    {
        var counts = await _repo.CountAsync(push.Id);
        if (counts.Pending > 0) return false;
        var now = DateTime.UtcNow;
        var message = counts.Failed > 0
            ? $"Finished with {counts.Failed} failed {(counts.Failed == 1 ? "item" : "items")}. Their reasons are listed below; a new push with “Skip” sends only what is missing."
            : "Finished.";
        await _repo.SetStatusAsync(push.Id, PushStatus.Completed, message, null, now, now);
        _credentials.Forget(push.Id);
        await _audit.LogAsync(AuditActions.ExportPushed, "Export", push.Id.ToString(), $"{push.Target} push to {push.SiteUrl}",
            newValues: Summary(push) + "; " + Counts(counts), userIdOverride: push.StartedBy, userNameOverride: push.StartedByName);
        return true;
    }

    /// <summary>Sends one post or page: look it up by slug, apply the conflict policy, upload its images, then save it.</summary>
    internal async Task<PushItemStatus> SendItemAsync(ExportPush push, IPushClient client, ExportCatalog catalog, ExportPushItem item, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (!Guid.TryParse(item.SourceKey, out var id)) throw new PushException("The item's record is damaged.");
        ExportPost? post = null;
        ExportPage? page = null;
        if (item.Kind == PushItemKind.Post) post = await _source.GetPostAsync(id);
        else page = await _source.GetPageAsync(id);
        var slug = post?.Slug ?? page?.Slug;
        if (slug is null)
        {
            await _repo.MarkItemAsync(item.Id, PushItemStatus.Skipped, item.Attempts, now, null, null, "Deleted on this site after the push started.", now);
            return PushItemStatus.Skipped;
        }

        var existing = await client.FindAsync(item.Kind, slug, ct);
        var action = PushRules.Decide(existing, push.ConflictPolicy);
        if (action == PushAction.Skip)
        {
            await _repo.MarkItemAsync(item.Id, PushItemStatus.Skipped, item.Attempts, now, existing!.Id, existing.Url,
                "Already on the site; left as it is.", now);
            return PushItemStatus.Skipped;
        }

        var origin = push.SourceBaseUrl;
        var wp = push.Target == ExportTarget.WordPress;
        var html = post is not null ? ExportHtml.PostBody(post) : page!.Html;
        var featureSource = post is not null ? post.FeatureImage : page!.FeatureImage;
        var featurePath = ExportHtml.UploadPathOf(featureSource, origin);
        var seo = post is not null ? post.Seo : page!.Seo;

        // Images first: each upload path once per push; a file missing here keeps pointing at this site.
        var found = new SortedSet<string>(StringComparer.Ordinal);
        ExportHtml.RewriteUploads(html, origin, p => p, found);
        var images = new Dictionary<string, RemoteItem?>(StringComparer.Ordinal);
        foreach (var path in found) images[path] = await ImageAsync(push, client, path, catalog, ct);
        if (featurePath is not null && !images.ContainsKey(featurePath)) images[featurePath] = await ImageAsync(push, client, featurePath, catalog, ct);
        string Address(string path) => images.TryGetValue(path, out var r) && r?.Url is not null ? r.Url : origin + path;
        var body = ExportHtml.RewriteUploads(html, origin, Address, new HashSet<string>());

        string? Social(string? url)
        {
            var p = ExportHtml.UploadPathOf(url, origin);
            return p is null ? url : images.TryGetValue(p, out var r) && r?.Url is not null ? r.Url : origin + p;
        }
        if (!wp)
        {
            foreach (var extra in new[] { seo.OgImage, seo.TwitterImage })
            {
                var p = ExportHtml.UploadPathOf(extra, origin);
                if (p is not null && !images.ContainsKey(p)) images[p] = await ImageAsync(push, client, p, catalog, ct);
            }
        }

        var feature = featurePath is null ? null : images.GetValueOrDefault(featurePath);
        (string? Alt, string? Caption) featureMeta = featurePath is not null && catalog.MediaByUrl.TryGetValue(featurePath, out var m) ? m : (null, null);
        var truncated = 0;
        System.Text.Json.Nodes.JsonObject payload;
        if (wp && post is not null)
        {
            var catIds = new List<string>();
            var tagIds = new List<string>();
            foreach (var term in PushPayloads.WordPressTerms(post, catalog))
                (term.Taxonomy == "categories" ? catIds : tagIds).Add(await TermAsync(push, client, term, ct));
            payload = PushPayloads.WordPressPost(post, body, feature?.Id, catIds, tagIds, now);
        }
        else if (wp)
        {
            string? parentId = null;
            if (page!.ParentId is Guid parent)
                parentId = (await _repo.FindAsync(push.Id, PushItemKind.Page, parent.ToString()))?.RemoteId;
            payload = PushPayloads.WordPressPage(page, body, feature?.Id, parentId);
        }
        else
        {
            var featureUrl = feature?.Url ?? (featurePath is null ? featureSource : origin + featurePath);
            payload = post is not null
                ? PushPayloads.GhostPost(post, body, featureUrl, featureMeta.Alt, featureMeta.Caption, PushPayloads.GhostTags(post, catalog),
                    Social(seo.OgImage), Social(seo.TwitterImage), action == PushAction.Update ? existing!.UpdatedAt : null, now, ref truncated)
                : PushPayloads.GhostPage(page!, body, featureUrl, featureMeta.Alt, featureMeta.Caption,
                    Social(seo.OgImage), Social(seo.TwitterImage), action == PushAction.Update ? existing!.UpdatedAt : null, ref truncated);
        }

        var saved = await client.SaveAsync(item.Kind, payload, action == PushAction.Update ? existing : null, ct);
        var status = action == PushAction.Update ? PushItemStatus.Updated : PushItemStatus.Created;
        await _repo.MarkItemAsync(item.Id, status, item.Attempts + 1, DateTime.UtcNow, saved.Id, saved.Url,
            truncated > 0 ? $"{truncated} value(s) shortened to fit Ghost's limits." : null, DateTime.UtcNow);
        return status;
    }

    /// <summary>Uploads an image once per push. Null when the file cannot be sent (missing or not an image): links keep pointing here.</summary>
    private async Task<RemoteItem?> ImageAsync(ExportPush push, IPushClient client, string path, ExportCatalog catalog, CancellationToken ct)
    {
        var item = await _repo.GetOrAddAsync(push.Id, PushItemKind.Media, PushRules.Trim(path, 400)!, null);
        if (item.IsDone) return new RemoteItem(item.RemoteId ?? "", item.RemoteUrl);
        if (item.Status == PushItemStatus.Failed) return null;

        var now = DateTime.UtcNow;
        var contentType = PushRules.ImageContentType(path, push.Target);
        var root = Path.GetFullPath(_uploadsRoot) + Path.DirectorySeparatorChar;
        var file = Path.GetFullPath(Path.Combine(_uploadsRoot, path["/uploads/".Length..].Replace('/', Path.DirectorySeparatorChar)));
        if (contentType is null || !file.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
        {
            await _repo.MarkItemAsync(item.Id, PushItemStatus.Failed, item.Attempts, now, null, null,
                contentType is null ? "Not an image type the site accepts; the link points at this site." : "Not found on this server; the link points at this site.", now);
            return null;
        }

        catalog.MediaByUrl.TryGetValue(path, out var meta);
        try
        {
            await using var stream = File.OpenRead(file);
            var uploaded = await client.UploadImageAsync(Path.GetFileName(file), contentType, stream, meta.Alt, meta.Caption, ct);
            await _repo.MarkItemAsync(item.Id, PushItemStatus.Created, item.Attempts + 1, DateTime.UtcNow, uploaded.Id, uploaded.Url, null, DateTime.UtcNow);
            return uploaded;
        }
        catch (PushException ex) when (!ex.IsThrottle && !ex.IsAuthFailure && ex.StatusCode is >= 400)
        {
            // The target refuses this file (type or size; WordPress answers an unsupported type with a 500): link to it
            // here rather than fail the post and re-upload the image on every retry.
            await _repo.MarkItemAsync(item.Id, PushItemStatus.Failed, item.Attempts + 1, DateTime.UtcNow, null, null,
                ex.Message + " The link points at this site.", DateTime.UtcNow);
            return null;
        }
    }

    /// <summary>A WordPress category or tag id, found or created once per push.</summary>
    private async Task<string> TermAsync(ExportPush push, IPushClient client, PushTerm term, CancellationToken ct)
    {
        var item = await _repo.GetOrAddAsync(push.Id, PushItemKind.Term, PushRules.Trim(term.Key, 400)!, term.Name);
        if (item.IsDone && item.RemoteId is not null) return item.RemoteId;
        var id = await client.EnsureTermAsync(term, ct);
        await _repo.MarkItemAsync(item.Id, PushItemStatus.Created, item.Attempts + 1, DateTime.UtcNow, id, null, null, DateTime.UtcNow);
        return id;
    }
}
