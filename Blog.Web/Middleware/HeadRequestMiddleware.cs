namespace Blog.Web.Middleware;

/// <summary>
/// Makes HEAD work on MVC routes.
///
/// ASP.NET Core routing matches methods exactly, and every controller action here is declared
/// <c>[HttpGet]</c>, so a HEAD request matched no endpoint and came back <b>405 Method Not Allowed</b>
/// — including <c>HEAD /</c> and <c>HEAD /robots.txt</c> (confirmed on production 2026-08-15). That
/// breaks uptime monitors and link checkers, which use HEAD precisely because it is cheap, and it
/// filled the Error Monitor with 405 rows that looked like attack traffic.
///
/// The request is rewritten to GET so it routes normally, and the response body is discarded so the
/// client still gets headers only — the semantics HEAD is supposed to have. Static files are left
/// alone: <c>UseStaticFiles</c> already handles HEAD correctly, so this runs after it.
/// </summary>
public sealed class HeadRequestMiddleware
{
    /// <summary>Set on requests that arrived as HEAD, so downstream code can tell them from real GETs.</summary>
    public const string OriginalMethodWasHeadKey = "OriginalMethodWasHead";

    private readonly RequestDelegate _next;

    public HeadRequestMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsHead(context.Request.Method))
        {
            await _next(context);
            return;
        }

        context.Items[OriginalMethodWasHeadKey] = true;
        context.Request.Method = HttpMethods.Get;

        var originalBody = context.Response.Body;
        context.Response.Body = Stream.Null; // headers still flow; the body is dropped

        try
        {
            await _next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
            context.Request.Method = HttpMethods.Head;
        }
    }
}

public static class HeadRequestMiddlewareExtensions
{
    public static IApplicationBuilder UseHeadRequests(this IApplicationBuilder app)
        => app.UseMiddleware<HeadRequestMiddleware>();
}
