using System.Text.RegularExpressions;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Regression guard for the UTC convention. Publishing and visibility once mixed server-local time
/// (DateTime.Now, GETDATE()) with UTC everywhere else, so a post could be live at its URL yet
/// missing from the homepage, feed and sitemap on a host behind UTC — and dev could not reproduce
/// it. This test reads the source files on the publish / visibility / timestamp paths and fails
/// the moment a local-time call comes back.
/// </summary>
public class TimeConventionTests
{
    private static readonly string[] GuardedFiles =
    {
        "Blog.Core/Services/PostService.cs",
        "Blog.Infrastructure/Data/Repositories/PostRepository.cs",
        "Blog.Infrastructure/Data/Repositories/RedirectRepository.cs",
        "Blog.Infrastructure/Data/Repositories/CommentRepository.cs",
        "Blog.Infrastructure/Data/Repositories/PageRepository.cs",
        "Blog.Infrastructure/Data/Repositories/SettingRepository.cs",
        "Blog.Infrastructure/Data/Repositories/RevisionRepository.cs",
        "Blog.Web/Controllers/PostsController.cs",
        "Blog.Web/Controllers/BlogController.cs",
        "Blog.Web/Controllers/Api/PostsApiController.cs",
        "Blog.Web/Views/Posts/Index.cshtml",
        "Blog.Web/Views/Dashboard/Index.cshtml",
    };

    private static readonly Regex LocalNow = new(@"\bDateTime\.Now\b|\bGETDATE\(\)", RegexOptions.Compiled);

    [Fact]
    public void Publish_and_visibility_paths_never_use_server_local_time()
    {
        var root = RepoRoot();
        var offenders = new List<string>();
        foreach (var rel in GuardedFiles)
        {
            var path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"guarded file missing: {rel}");
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
                if (LocalNow.IsMatch(lines[i]) && !lines[i].TrimStart().StartsWith("//"))
                    offenders.Add($"{rel}:{i + 1}: {lines[i].Trim()}");
        }
        Assert.True(offenders.Count == 0, "Local-time call on a UTC path:\n" + string.Join('\n', offenders));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
