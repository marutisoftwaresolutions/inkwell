using Blog.Infrastructure.Data;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// The first ten minutes of a new install: the README's Quick Start has to work as written, and the
/// schema the app applies to an empty database has to be the very file a manual install runs.
/// </summary>
public class FirstRunTests
{
    [Fact]
    public void The_app_carries_init_sql_word_for_word()
    {
        var embedded = DatabaseBootstrapper.ReadInitScript();
        Assert.NotNull(embedded);
        var onDisk = File.ReadAllText(Path.Combine(RepoRoot(), "DBScripts", "init.sql"));
        Assert.Equal(Normalize(onDisk), Normalize(embedded!));
    }

    [Fact]
    public void Quick_start_names_the_project_to_run()
    {
        var readme = File.ReadAllText(Path.Combine(RepoRoot(), "README.md"));
        var start = readme.IndexOf("## Quick Start", StringComparison.Ordinal);
        Assert.True(start >= 0, "README has no Quick Start section.");
        var section = readme[start..readme.IndexOf("\n## ", start + 5, StringComparison.Ordinal)];

        Assert.Contains("dotnet run --project Blog.Web", section);
        // A bare `dotnet run` at the repository root fails: there is no project there.
        Assert.DoesNotMatch(@"(?m)^dotnet run\s*$", section);
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "Blog.Web", "Blog.Web.csproj")));
    }

    [Fact]
    public void First_run_setup_happens_before_migrations_and_the_wizard_redirect_is_in_the_pipeline()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "Program.cs"));
        var boot = program.IndexOf("DatabaseBootstrapper>().EnsureAsync()", StringComparison.Ordinal);
        var migrate = program.IndexOf("GetRequiredService<MigrationService>()", StringComparison.Ordinal);
        Assert.True(boot > 0 && boot < migrate, "The bootstrapper must run before MigrationService.");

        var firewall = program.IndexOf("app.UseIpFirewall()", StringComparison.Ordinal);
        var redirect = program.IndexOf("FirstRunRedirectMiddleware", StringComparison.Ordinal);
        Assert.True(redirect > firewall, "New middleware goes after the firewall.");
    }

    [Fact]
    public void The_setup_wizard_creates_roles_and_never_saves_settings_under_an_empty_owner()
    {
        var setup = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "Controllers", "SetupController.cs"));
        Assert.Contains("SeedRolesAndPermissionsAsync()", setup);
        Assert.Contains("AssignRoleToUserAsync(userId", setup);
        // Settings.UserId references Users: a row for Guid.Empty is rejected and ended the wizard on an error.
        Assert.DoesNotContain("SaveSettingsAsync(Guid.Empty", setup);
    }

    [Fact]
    public void Ci_builds_and_tests_with_and_without_a_database_on_a_pinned_image()
    {
        var dir = Path.Combine(RepoRoot(), ".github", "workflows");
        var ci = File.ReadAllText(Path.Combine(dir, "build-and-test.yml"));
        Assert.Contains("with-database:", ci);
        Assert.Contains("without-database:", ci);
        Assert.Contains("DBScripts/init.sql", ci);
        Assert.Contains("Blog.Tests/Fixtures/ci-seed.sql", ci);
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "Blog.Tests", "Fixtures", "ci-seed.sql")));
        foreach (var file in Directory.GetFiles(dir, "*.yml"))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("ubuntu-latest", text);       // a moving image changes under a green build
            Assert.DoesNotContain("actions/checkout@v4", text);  // targets a deprecated Node runtime
        }
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimStart('\uFEFF');

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
