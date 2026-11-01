using Xunit;

namespace Blog.Tests;

/// <summary>
/// Avatar URLs carry the author's display name as a path segment ("/avatar/Demo Admin"). Encoding it
/// with <c>WebUtility.UrlEncode</c> turns the space into "+", and IIS request filtering rejects a "+"
/// in a path (404.11, double escaping) unless the server is reconfigured, so every multi-word author
/// showed a broken avatar on an IIS host (seen on demo.useinkwell.app, 2026-11-01). Path segments are
/// percent-encoded with <c>Uri.EscapeDataString</c>, which writes "%20". This scan keeps it that way.
/// </summary>
public class AvatarUrlEncodingTests
{
    [Fact]
    public void Avatar_urls_are_never_built_with_WebUtility_UrlEncode()
    {
        var root = FindRepoRoot();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Blog.Web"), "*.*", SearchOption.AllDirectories))
        {
            if (!(file.EndsWith(".cshtml") || file.EndsWith(".cs"))) continue;
            if (file.Contains(Path.Combine("bin", "")) || file.Contains(Path.Combine("obj", ""))) continue;
            foreach (var (line, i) in File.ReadLines(file).Select((l, i) => (l, i + 1)))
            {
                if (line.Contains("/avatar/") && line.Contains("WebUtility.UrlEncode"))
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i}");
            }
        }
        Assert.True(offenders.Count == 0,
            "Build /avatar/ URLs with Uri.EscapeDataString (percent-encoding), not WebUtility.UrlEncode ('+' breaks on IIS):\n" + string.Join("\n", offenders));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}
