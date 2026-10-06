using Blog.Core.Services;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Blog.Web.Services;

/// <summary>
/// On a listing action, answers <c>301</c> to the same path with only the query parameters a listing
/// understands (see <see cref="ListingQuery.Canonicalize"/>), so <c>/?anything</c> and
/// <c>/?page=abc</c> collapse onto one URL instead of each being a separate page crawlers can fetch.
/// Runs before the action, so it costs no database work.
///
/// The target is built from the request's own path — never from a query value — and only when that
/// path is a single-slash local path, so it cannot become an open redirect.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CanonicalListingQueryAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsGet(request.Method) || !request.QueryString.HasValue) return;

        var pairs = request.Query.SelectMany(q => q.Value.Select(v => new KeyValuePair<string, string?>(q.Key, v)));
        var (changed, kept) = ListingQuery.Canonicalize(pairs);
        if (!changed) return;

        var path = (request.PathBase + request.Path).Value ?? "/";
        if (path.Length == 0) path = "/";
        if (path[0] != '/' || path.StartsWith("//", StringComparison.Ordinal) || path.StartsWith("/\\", StringComparison.Ordinal))
            return;

        var query = new QueryBuilder(kept.Select(k => new KeyValuePair<string, string>(k.Key, k.Value))).ToQueryString();
        context.Result = new RedirectResult(path + query.Value, permanent: true);
    }
}
