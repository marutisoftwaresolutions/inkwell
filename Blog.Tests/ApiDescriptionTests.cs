using System.Text.Json;
using System.Text.RegularExpressions;
using Blog.Core.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// The OpenAPI document is written by hand beside the controllers, so these tests keep the two the
/// same: every route in an API controller is described, nothing is described that no controller
/// serves, and the document is well-formed and names no site.
/// </summary>
public class ApiDescriptionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public ApiDescriptionTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return dir!.FullName;
    }

    /// <summary>"METHOD /path" for every action in the API controllers, read from their attributes.</summary>
    private static SortedSet<string> RoutesInControllers()
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(Path.Combine(Root(), "Blog.Web", "Controllers", "Api"), "*.cs"))
        {
            var source = File.ReadAllText(file);
            var prefix = Regex.Match(source, @"^\[Route\(""([^""]*)""\)\]", RegexOptions.Multiline).Groups[1].Value;
            if (!prefix.StartsWith("api/v1", StringComparison.Ordinal)) continue;   // the MCP endpoint is not part of the REST API
            var rest = prefix["api/v1".Length..];
            foreach (Match m in Regex.Matches(source, @"^\s*\[Http(Get|Post|Put|Patch|Delete)(?:\(""([^""]*)""\))?\]", RegexOptions.Multiline))
            {
                var path = (rest + (m.Groups[2].Value.Length > 0 ? "/" + m.Groups[2].Value : "")).Replace("//", "/");
                path = Regex.Replace(path, @"\{(\w+):\w+\}", "{$1}");               // {id:guid} is {id} to a client
                found.Add(m.Groups[1].Value.ToUpperInvariant() + " " + path);
            }
        }
        return found;
    }

    [Fact]
    public void Every_route_is_described_and_nothing_is_described_that_is_not_served()
    {
        var described = new SortedSet<string>(ApiDescription.Operations.Select(o => o.Method + " " + o.Path), StringComparer.Ordinal);
        Assert.Equal(RoutesInControllers(), described);
        Assert.True(described.Count >= 20);
    }

    [Fact]
    public void Operation_ids_are_unique_and_every_path_parameter_is_declared()
    {
        Assert.Equal(ApiDescription.Operations.Count, ApiDescription.Operations.Select(o => o.Id).Distinct().Count());
        foreach (var op in ApiDescription.Operations)
        {
            var inPath = Regex.Matches(op.Path, @"\{(\w+)\}").Select(m => m.Groups[1].Value).OrderBy(n => n).ToArray();
            var declared = (op.Params ?? []).Where(p => p.In == "path").ToList();
            Assert.Equal(inPath, declared.Select(p => p.Name).OrderBy(n => n).ToArray());
            Assert.All(declared, p => Assert.True(p.Required));
            if (op.Access == ApiDescription.Access.Public) Assert.Null(op.Needs);
        }
    }

    [Fact]
    public void The_document_is_valid_json_with_the_expected_shape_and_names_no_site()
    {
        var json = ApiDescription.BuildOpenApi("https://blog.example.org/", "9.9.9");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("3.1.0", root.GetProperty("openapi").GetString());
        Assert.Equal("9.9.9", root.GetProperty("info").GetProperty("version").GetString());
        Assert.Equal("https://blog.example.org/api/v1", root.GetProperty("servers")[0].GetProperty("url").GetString());

        var paths = root.GetProperty("paths");
        var create = paths.GetProperty("/posts").GetProperty("post");
        Assert.Equal("createPost", create.GetProperty("operationId").GetString());
        Assert.True(create.GetProperty("responses").TryGetProperty("201", out _));
        Assert.True(create.GetProperty("responses").TryGetProperty("401", out _));
        Assert.Equal("title", create.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("required")[0].GetString());
        Assert.Equal(1, create.GetProperty("security").GetArrayLength());

        var list = paths.GetProperty("/posts").GetProperty("get");
        Assert.Equal(0, list.GetProperty("security").GetArrayLength());            // public: no credentials
        Assert.False(list.GetProperty("responses").TryGetProperty("401", out _));
        Assert.True(paths.GetProperty("/posts/{slug}").GetProperty("get").GetProperty("responses").TryGetProperty("404", out _));

        Assert.True(root.GetProperty("components").GetProperty("schemas").TryGetProperty("Error", out _));
        Assert.True(root.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("bearer", out _));
        Assert.Contains("subscribers scope", paths.GetProperty("/subscribers").GetProperty("get").GetProperty("description").GetString());

        Assert.DoesNotContain("opticalsoftware", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localhost", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_document_is_served_without_credentials()
    {
        if (!TestDatabase.Available(_factory)) return;   // tenant resolution needs the database: skip without one
        var response = await _factory.CreateClient().GetAsync("/api/v1/openapi.json");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.EndsWith("/api/v1", doc.RootElement.GetProperty("servers")[0].GetProperty("url").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("info").GetProperty("version").GetString()));
    }
}
