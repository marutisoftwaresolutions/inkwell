using System.Text.Json;
using System.Text.Json.Nodes;

namespace Blog.Core.Services;

/// <summary>
/// The REST API written down once, as data: every operation under <c>/api/v1</c> with who may call
/// it, its parameters and its request body. <see cref="BuildOpenApi"/> renders it as an OpenAPI 3.1
/// document so a client generator, an API explorer or an automation tool can read it. The list is
/// kept by hand next to the controllers (no reflection, no generator package); a source-scanning test
/// fails when a route exists in a controller and not here, or here and not in a controller. Pure.
/// </summary>
public static class ApiDescription
{
    /// <summary>Who may call an operation.</summary>
    public enum Access
    {
        /// <summary>Anyone, no credentials.</summary>
        Public,
        /// <summary>A sign-in token or an API key.</summary>
        SignedIn,
    }

    public sealed record Param(string Name, string In, string Type, string Description, bool Required = false, string? Format = null);

    public sealed record Field(string Name, string Type, string Description, bool Required = false, string? ItemsType = null, string? Format = null);

    public sealed record Operation(
        string Method, string Path, string Id, string Tag, string Summary, Access Access,
        string? Needs = null, IReadOnlyList<Param>? Params = null, IReadOnlyList<Field>? Body = null, int Success = 200);

    private static Param Id(string what) => new("id", "path", "string", $"The {what}'s id.", true, "uuid");
    private static Param Slug(string what) => new("slug", "path", "string", $"The {what}'s slug, as in its address.", true);
    private static Param Page => new("page", "query", "integer", "Page number, starting at 1.");

    private static readonly Field[] PostBody =
    [
        new("title", "string", "The post's title."),
        new("slug", "string", "Address segment; made from the title when left out."),
        new("content", "string", "The body as HTML. Leave out when sending markdown."),
        new("markdown", "string", "The body as Markdown; the post is then kept as a Markdown post."),
        new("summary", "string", "Short description, used as the meta description."),
        new("categoryIds", "array", "Ids of the post's categories.", ItemsType: "string", Format: "uuid"),
        new("tags", "array", "Tag names; a tag that does not exist is created.", ItemsType: "string"),
        new("customFields", "object", "Values for the site's custom fields, by field key."),
    ];

    /// <summary>Every operation, in the order the document lists them.</summary>
    public static readonly IReadOnlyList<Operation> Operations =
    [
        new("POST", "/auth/login", "login", "Sign in", "Exchange an email and password (and a two-factor code when the account uses one) for a 15-minute bearer token.", Access.Public,
            Body: [new("email", "string", "The account's email address.", true), new("password", "string", "The account's password.", true), new("code", "string", "The 6-digit code, when two-factor sign-in is on.")]),
        new("POST", "/auth/logout", "logout", "Sign in", "Tell the server the token is finished with. Tokens are short-lived and not stored, so this is a courtesy.", Access.SignedIn),

        new("GET", "/posts", "listPosts", "Posts", "Published posts, newest first.", Access.Public,
            Params: [Page, new("pageSize", "query", "integer", "Posts per page."), new("search", "query", "string", "Words to look for in titles and text."),
                     new("categoryId", "query", "string", "Only posts in this category.", Format: "uuid"), new("tagId", "query", "string", "Only posts carrying this tag.", Format: "uuid")]),
        new("GET", "/posts/{slug}", "getPost", "Posts", "One published post, as a reader sees it. A caller who could edit it in the Desk also receives the custom fields kept from readers.", Access.Public, Params: [Slug("post")]),
        new("POST", "/posts", "createPost", "Posts", "Create a post as a draft.", Access.SignedIn, "The posts.edit permission; an API key needs the write scope.", Body: [PostBody[0] with { Required = true }, .. PostBody[1..]], Success: 201),
        new("PUT", "/posts/{id}", "updatePost", "Posts", "Change a post. Fields left out are kept. A published post can be changed only by a caller who may publish.", Access.SignedIn, "The posts.edit permission; an API key needs the write scope, and the publish scope for a published post.", [Id("post")], PostBody),
        new("PATCH", "/posts/{id}/publish", "publishPost", "Posts", "Publish a post. Refused, with the problems listed, when its structured data fails the same check the Desk runs.", Access.SignedIn, "The posts.publish permission; an API key needs the publish scope.", [Id("post")]),
        new("DELETE", "/posts/{id}", "deletePost", "Posts", "Move a post to Trash. Deleting a post that is already in Trash removes it for good.", Access.SignedIn, "The posts.edit permission; an API key needs the publish scope. Your own posts only, unless you are an Admin or Editor. A published or scheduled post also needs the publish permission.", [Id("post")]),

        new("GET", "/pages/{slug}", "getPage", "Pages", "One published page.", Access.Public, Params: [Slug("page")]),
        new("GET", "/pages", "listPages", "Pages", "Your own pages, unpublished ones included.", Access.SignedIn, "The pages.manage permission; an API key needs the write scope."),
        new("POST", "/pages", "createPage", "Pages", "Create a page. It starts unpublished unless publish is true.", Access.SignedIn, "The pages.manage permission; an API key needs the write scope, and the publish scope to publish.",
            Body: [new("title", "string", "The page's title.", true), new("slug", "string", "Address segment; made from the title when left out."), new("content", "string", "The body as HTML."),
                   new("summary", "string", "Short description, used as the meta description."), new("publish", "boolean", "True to publish, false to unpublish."), new("showInNavigation", "boolean", "Whether the page is offered to the site's menus.")], Success: 201),
        new("PUT", "/pages/{id}", "updatePage", "Pages", "Change a page. Fields left out are kept.", Access.SignedIn, "The pages.manage permission; an API key needs the write scope, and the publish scope to publish a page or change a published one.", [Id("page")],
            [new("title", "string", "The page's title."), new("slug", "string", "Address segment."), new("content", "string", "The body as HTML."),
             new("summary", "string", "Short description, used as the meta description."), new("publish", "boolean", "True to publish, false to unpublish."), new("showInNavigation", "boolean", "Whether the page is offered to the site's menus.")]),

        new("GET", "/categories", "listCategories", "Taxonomy", "Every category.", Access.Public),
        new("GET", "/tags", "listTags", "Taxonomy", "Every tag.", Access.Public),

        new("GET", "/comments", "listComments", "Comments", "Approved comments, optionally for one post, 20 to a page.", Access.SignedIn,
            Params: [new("postId", "query", "string", "Only comments on this post.", Format: "uuid"), Page]),
        new("PATCH", "/comments/{id}/approve", "approveComment", "Comments", "Approve a comment so readers see it.", Access.SignedIn, "The comments.manage permission; an API key needs the comments scope.", [Id("comment")]),
        new("DELETE", "/comments/{id}", "deleteComment", "Comments", "Delete a comment.", Access.SignedIn, "The comments.manage permission; an API key needs the comments scope.", [Id("comment")]),

        new("GET", "/subscribers", "listSubscribers", "Subscribers", "One page of newsletter subscribers, newest first. Personal data: every read is recorded in the audit trail.", Access.SignedIn, "An Admin account; an API key needs the subscribers scope.",
            [new("status", "query", "string", "confirmed (the default), pending, unsubscribed, bounced or all."), Page, new("pageSize", "query", "integer", "Subscribers per page, at most 200.")]),

        new("GET", "/users/me", "getMe", "Account", "The account the token or key belongs to.", Access.SignedIn),
        new("PUT", "/users/me", "updateMe", "Account", "Change your display name, bio or website. Not available to an API key.", Access.SignedIn, "A sign-in token; an API key is refused.",
            Body: [new("displayName", "string", "The name shown on your posts."), new("bio", "string", "A short text about you."), new("website", "string", "Your website's address.")]),

        new("GET", "/openapi.json", "getOpenApi", "About", "This document.", Access.Public),
    ];

    /// <summary>The OpenAPI 3.1 document for a site served at <paramref name="serverUrl"/> (scheme and host, no trailing slash).</summary>
    public static string BuildOpenApi(string serverUrl, string version)
    {
        var paths = new JsonObject();
        foreach (var op in Operations)
        {
            if (paths[op.Path] is not JsonObject item) paths[op.Path] = item = new JsonObject();

            var description = op.Needs is null ? op.Summary : $"{op.Summary} Needs: {op.Needs}";
            var node = new JsonObject
            {
                ["operationId"] = op.Id,
                ["tags"] = new JsonArray(op.Tag),
                ["summary"] = FirstSentence(op.Summary),
                ["description"] = description,
            };

            if (op.Params is { Count: > 0 })
            {
                var parameters = new JsonArray();
                foreach (var p in op.Params)
                {
                    var schema = new JsonObject { ["type"] = p.Type };
                    if (p.Format is not null) schema["format"] = p.Format;
                    parameters.Add(new JsonObject { ["name"] = p.Name, ["in"] = p.In, ["required"] = p.Required, ["description"] = p.Description, ["schema"] = schema });
                }
                node["parameters"] = parameters;
            }

            if (op.Body is { Count: > 0 })
            {
                var properties = new JsonObject();
                var required = new JsonArray();
                foreach (var f in op.Body)
                {
                    var schema = new JsonObject { ["type"] = f.Type, ["description"] = f.Description };
                    if (f.Type == "array")
                    {
                        var items = new JsonObject { ["type"] = f.ItemsType ?? "string" };
                        if (f.Format is not null) items["format"] = f.Format;
                        schema["items"] = items;
                    }
                    else if (f.Format is not null) schema["format"] = f.Format;
                    properties[f.Name] = schema;
                    if (f.Required) required.Add(f.Name);
                }
                var body = new JsonObject { ["type"] = "object", ["properties"] = properties };
                if (required.Count > 0) body["required"] = required;
                node["requestBody"] = new JsonObject
                {
                    ["required"] = true,
                    ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = body } },
                };
            }

            var responses = new JsonObject
            {
                [op.Success.ToString()] = new JsonObject
                {
                    ["description"] = "The result, as JSON.",
                    ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = new JsonObject { ["type"] = "object" } } },
                },
            };
            if (op.Body is { Count: > 0 } || op.Params is { Count: > 0 }) responses["400"] = ErrorResponse("The request was not understood; the message says why.");
            if (op.Access == Access.SignedIn)
            {
                responses["401"] = ErrorResponse("No token or key, or one that is expired, revoked or unknown.");
                responses["403"] = ErrorResponse("The account, or the key's scopes, do not allow this.");
                node["security"] = new JsonArray(new JsonObject { ["bearer"] = new JsonArray() });
            }
            else node["security"] = new JsonArray();
            if (op.Params?.Any(p => p.In == "path") == true) responses["404"] = ErrorResponse("Nothing with that id or slug.");
            node["responses"] = responses;

            item[op.Method.ToLowerInvariant()] = node;
        }

        var document = new JsonObject
        {
            ["openapi"] = "3.1.0",
            ["info"] = new JsonObject
            {
                ["title"] = "Inkwell REST API",
                ["version"] = version,
                ["description"] = "Read and write a site's posts, pages, comments and subscribers. Send a sign-in token from POST /auth/login, or an API key created in the Desk under Profile, API keys, as \"Authorization: Bearer ...\". A key acts as the account that created it, narrowed by its scopes. Results come back as {\"data\": ...}; errors as {\"statusCode\", \"error\", \"traceId\"}.",
            },
            ["servers"] = new JsonArray(new JsonObject { ["url"] = serverUrl.TrimEnd('/') + "/api/v1" }),
            ["tags"] = new JsonArray(Operations.Select(o => o.Tag).Distinct().Select(t => (JsonNode)new JsonObject { ["name"] = t }).ToArray()),
            ["paths"] = paths,
            ["components"] = new JsonObject
            {
                ["securitySchemes"] = new JsonObject
                {
                    ["bearer"] = new JsonObject { ["type"] = "http", ["scheme"] = "bearer", ["description"] = "A sign-in token from POST /auth/login, or an API key (ink_...)." },
                },
                ["schemas"] = new JsonObject
                {
                    ["Error"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["statusCode"] = new JsonObject { ["type"] = "integer" },
                            ["error"] = new JsonObject { ["type"] = "string" },
                            ["traceId"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                        },
                    },
                },
            },
        };
        return document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject ErrorResponse(string description) => new()
    {
        ["description"] = description,
        ["content"] = new JsonObject
        {
            ["application/json"] = new JsonObject { ["schema"] = new JsonObject { ["$ref"] = "#/components/schemas/Error" } },
        },
    };

    private static string FirstSentence(string text)
    {
        var end = text.IndexOf(". ", StringComparison.Ordinal);
        return end < 0 ? text.TrimEnd('.') : text[..end];
    }
}
