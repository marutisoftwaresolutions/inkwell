namespace Blog.Web.Middleware;

/// <summary>
/// Adds the response headers a browser needs to treat the site defensively. Each one is set only when
/// the response has not set it already, so an action with its own policy (the newsletter preview's
/// sandbox) keeps it. Nothing here can fail a request: headers are written in OnStarting inside a
/// try/catch, and the whole middleware is switched off with <c>Security:Headers:Enabled=false</c>.
///
/// - <c>X-Content-Type-Options: nosniff</c>: a file is what its content type says, never guessed.
/// - <c>Referrer-Policy: strict-origin-when-cross-origin</c>: other sites see our origin, not the path.
/// - <c>X-Frame-Options</c> and <c>frame-ancestors</c>: the site is framed only by itself (the Theme
///   preview and newsletter preview are same-origin); <c>Security:Headers:FrameAncestors</c> widens it.
/// - <c>Permissions-Policy</c>: camera, microphone, geolocation and payment are never requested.
/// - <c>Strict-Transport-Security</c>: only on HTTPS and only when <c>Security:Headers:Hsts</c> is on,
///   because browsers remember it for a year.
/// - A content security policy: report-only by default (it blocks nothing; violations show in the
///   browser console), enforced when the operator sets <c>Security:Headers:ContentSecurityPolicy</c>.
/// - Uploaded SVG files get their own enforced policy that stops script even when one is opened
///   directly, which also covers files stored before uploads were sanitised.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    /// <summary>The default report-only policy: what a stock Inkwell page loads, all from its own origin.</summary>
    public const string DefaultReportOnlyPolicy =
        "default-src 'self'; img-src 'self' data: https:; media-src 'self' https:; style-src 'self' 'unsafe-inline'; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval'; font-src 'self' data:; connect-src 'self'; frame-src 'self' https:; " +
        "object-src 'none'; base-uri 'self'; form-action 'self'";

    /// <summary>Applied to every uploaded SVG: no script, no plugins, no network, sandboxed.</summary>
    public const string SvgPolicy = "default-src 'none'; style-src 'unsafe-inline'; img-src data:; sandbox";

    private readonly RequestDelegate _next;
    private readonly bool _enabled;
    private readonly bool _hsts;
    private readonly string _frameAncestors;
    private readonly string? _enforcedPolicy;
    private readonly string? _reportOnlyPolicy;

    public SecurityHeadersMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _enabled = config.GetValue("Security:Headers:Enabled", true);
        _hsts = config.GetValue("Security:Headers:Hsts", false);
        _frameAncestors = Clean(config["Security:Headers:FrameAncestors"]) ?? "'self'";
        _enforcedPolicy = Clean(config["Security:Headers:ContentSecurityPolicy"]);
        // An explicit empty string switches the report-only header off; absent means the default.
        var reportOnly = config["Security:Headers:ContentSecurityPolicyReportOnly"];
        _reportOnlyPolicy = reportOnly is null ? DefaultReportOnlyPolicy : Clean(reportOnly);
    }

    public Task InvokeAsync(HttpContext ctx)
    {
        if (_enabled)
        {
            ctx.Response.OnStarting(() =>
            {
                try { Apply(ctx); }
                catch { /* a header we could not add must never cost the reader the page */ }
                return Task.CompletedTask;
            });
        }
        return _next(ctx);
    }

    private void Apply(HttpContext ctx)
    {
        var headers = ctx.Response.Headers;
        SetIfAbsent(headers, "X-Content-Type-Options", "nosniff");
        SetIfAbsent(headers, "Referrer-Policy", "strict-origin-when-cross-origin");
        SetIfAbsent(headers, "Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=()");
        if (_frameAncestors == "'self'") SetIfAbsent(headers, "X-Frame-Options", "SAMEORIGIN");

        if (_hsts && ctx.Request.IsHttps)
            SetIfAbsent(headers, "Strict-Transport-Security", "max-age=31536000");

        if (IsUploadedSvg(ctx.Request.Path))
        {
            headers["Content-Security-Policy"] = SvgPolicy;   // always, whatever else is configured
            return;
        }

        if (headers.ContainsKey("Content-Security-Policy")) return;   // the action set its own

        if (_enforcedPolicy is not null)
            headers["Content-Security-Policy"] = WithFrameAncestors(_enforcedPolicy);
        else
        {
            headers["Content-Security-Policy"] = "frame-ancestors " + _frameAncestors;
            if (_reportOnlyPolicy is not null) SetIfAbsent(headers, "Content-Security-Policy-Report-Only", _reportOnlyPolicy);
        }
    }

    private string WithFrameAncestors(string policy) =>
        policy.Contains("frame-ancestors", StringComparison.OrdinalIgnoreCase) ? policy : policy.TrimEnd(';', ' ') + "; frame-ancestors " + _frameAncestors;

    /// <summary>True for an .svg under /uploads, the only place a user-supplied SVG is served from.</summary>
    public static bool IsUploadedSvg(PathString path) =>
        path.HasValue
        && path.Value!.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)
        && path.Value.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);

    private static void SetIfAbsent(IHeaderDictionary headers, string name, string value)
    {
        if (!headers.ContainsKey(name)) headers[name] = value;
    }

    /// <summary>Null for blank; line breaks removed so a configured value can never split a header.</summary>
    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Replace("\r", " ").Replace("\n", " ").Trim();
    }
}
