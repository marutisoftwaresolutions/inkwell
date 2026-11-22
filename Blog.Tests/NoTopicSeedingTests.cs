using System.Text.RegularExpressions;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// The platform ships no content of its own choosing. A start-up seeder once tried to add optometry
/// categories and tags to every install (it failed on a foreign key each time, about seventy rejected
/// inserts per start), and an Admin link seeded sample posts and renamed the site. Both are gone;
/// these tests keep them gone.
/// </summary>
public class NoTopicSeedingTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return dir!.FullName;
    }

    [Fact]
    public void Startup_and_the_infrastructure_layer_name_no_subject_matter()
    {
        var files = Directory.GetFiles(Path.Combine(Root(), "Blog.Infrastructure"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Append(Path.Combine(Root(), "Blog.Web", "Program.cs"));
        foreach (var file in files)
        {
            // Comments may mention the site that first ran the platform; code and string literals may not.
            var code = string.Join('\n', File.ReadAllLines(file).Where(l => !l.TrimStart().StartsWith("//")));
            Assert.False(Regex.IsMatch(code, @"optomet|eyefinity|\bEHR\b", RegexOptions.IgnoreCase), Path.GetFileName(file) + " names a subject");
        }
        Assert.False(File.Exists(Path.Combine(Root(), "Blog.Infrastructure", "Data", "OptometryTaxonomySeeder.cs")));
    }

    [Fact]
    public void The_seeder_adds_roles_and_permissions_and_nothing_else()
    {
        var seeder = File.ReadAllText(Path.Combine(Root(), "Blog.Infrastructure", "Data", "ApplicationDbSeeder.cs"));
        Assert.Contains("SeedRolesAndPermissionsAsync", seeder);
        foreach (var repository in new[] { "ICategoryRepository", "ITagRepository", "IPostRepository", "IPageRepository", "ISettingRepository", "ICommentRepository" })
            Assert.DoesNotContain(repository, seeder);
    }

    [Fact]
    public void Restoring_roles_is_a_protected_audited_post_and_no_link_changes_data()
    {
        var dashboard = File.ReadAllText(Path.Combine(Root(), "Blog.Web", "Controllers", "DashboardController.cs"));
        Assert.DoesNotContain("[HttpGet(\"seed\")]", dashboard);
        var action = dashboard[dashboard.IndexOf("[HttpPost(\"restore-roles\")]", StringComparison.Ordinal)..];
        action = action[..action.IndexOf("return Redirect", StringComparison.Ordinal)];
        Assert.Contains("[ValidateAntiForgeryToken]", action);
        Assert.Contains("[Authorize(Policy = \"AdminOnly\")]", action);
        Assert.Contains("AuditActions.RoleDefaultsRestored", action);

        var users = File.ReadAllText(Path.Combine(Root(), "Blog.Web", "Views", "Users", "Index.cshtml"));
        Assert.DoesNotContain("/admin/dashboard/seed", users);
        Assert.Contains("<form method=\"post\" action=\"/admin/dashboard/restore-roles\"", users);
    }
}
