using Xunit;

namespace Blog.Tests;

/// <summary>
/// Docker deployment (2026-09-24), added after a GitHub issue reported that useinkwell.app/docs
/// documented a Dockerfile and docker-compose.yml that had never existed in this repo. These are
/// source/file-existence checks only — the dev machine has no `docker` CLI. The real `docker build`
/// and `docker compose up` smoke test is `.github/workflows/docker.yml`; these checks never replace
/// it. See the reverse-proxy rationale in Program.cs and docker-compose.yml.
/// </summary>
public class DockerDeploymentTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
    private static string Read(string rel) => File.ReadAllText(Path.Combine(Root(), rel.Replace('/', Path.DirectorySeparatorChar)));
    private static bool Exists(string rel) => File.Exists(Path.Combine(Root(), rel.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void Dockerfile_exists_at_the_repo_root_as_documented()
    {
        Assert.True(Exists("Dockerfile"));
        var df = Read("Dockerfile");
        Assert.Contains("FROM mcr.microsoft.com/dotnet/sdk:10.0", df);
        Assert.Contains("FROM mcr.microsoft.com/dotnet/aspnet:10.0", df);
        Assert.Contains("EXPOSE 8080", df);
        Assert.Contains("HEALTHCHECK", df);
        Assert.Contains("ENTRYPOINT [\"dotnet\", \"Blog.Web.dll\"]", df);
        Assert.Contains("USER app", df); // never runs as root
    }

    [Fact]
    public void Dockerfile_reuses_the_base_image_user_and_copies_every_build_input()
    {
        // 2026-10-25 GitHub report: the image never built. aspnet:10.0 already has `app` at UID 1654,
        // so adding our own user at that UID failed; and Blog.Infrastructure embeds a file from
        // ../DBScripts/, which the build stage never copied.
        var df = Read("Dockerfile");
        Assert.DoesNotContain("adduser", df);
        Assert.DoesNotContain("useradd", df);
        Assert.Contains("COPY DBScripts/ DBScripts/", df);
        Assert.True(df.IndexOf("COPY DBScripts/", StringComparison.Ordinal) < df.IndexOf("RUN dotnet publish", StringComparison.Ordinal),
            "DBScripts must be in the build stage before dotnet publish.");

        // Any other out-of-project build input added later must be copied too.
        var csproj = Read("Blog.Infrastructure/Blog.Infrastructure.csproj") + Read("Blog.Web/Blog.Web.csproj");
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(csproj, @"<(?:EmbeddedResource|Content|None) Include=""\.\.\\([^\\""]+)\\"))
            Assert.Contains($"COPY {m.Groups[1].Value}/ {m.Groups[1].Value}/", df);
    }

    [Fact]
    public void Compose_creates_and_seeds_the_database_before_the_app_starts()
    {
        // Same report: a new SQL Server container has no `blog` database, so the app could not
        // connect and no schema was ever applied. db-init creates it and runs init.sql first.
        var compose = Read("docker-compose.yml");
        Assert.Contains("db-init:", compose);
        Assert.Contains("CREATE DATABASE [blog]", compose);
        Assert.Contains("-i /scripts/init.sql", compose);
        Assert.Contains("-f 65001", compose); // sqlcmd encoding rule
        Assert.Contains("condition: service_completed_successfully", compose);
        Assert.Contains("Database=blog;", compose);
    }

    [Fact]
    public void Compose_stack_has_the_three_documented_services_with_health_gating_and_no_published_app_port()
    {
        Assert.True(Exists("docker-compose.yml"));
        var compose = Read("docker-compose.yml");
        Assert.Contains("db:", compose);
        Assert.Contains("app:", compose);
        Assert.Contains("caddy:", compose);
        Assert.Contains("mcr.microsoft.com/mssql/server:2022-latest", compose);
        Assert.Contains("condition: service_healthy", compose); // app waits for a ready db, not just a started container
        Assert.Contains("ConnectionStrings__DefaultConnection", compose);
        Assert.Contains("ReverseProxy__TrustForwardedHeaders: \"true\"", compose);
        Assert.Contains("DataProtection__KeysPath: /data/keys", compose); // key ring on a volume, survives a recreate
        Assert.Contains("app-uploads:/app/wwwroot/uploads", compose);    // uploads are a volume, never baked into the image
        // The app's own port must stay unpublished — that is what makes trusting forwarded headers
        // safe. It may only appear inside a comment (the local-eval opt-in), never as a live mapping.
        var live = string.Join('\n', compose.Split('\n').Where(l => !l.TrimStart().StartsWith('#')));
        Assert.DoesNotContain("\"8080:8080\"", live);
    }

    [Fact]
    public void Caddyfile_proxies_to_the_app_service_on_its_container_port()
    {
        Assert.True(Exists("docker/Caddyfile"));
        var caddyfile = Read("docker/Caddyfile");
        Assert.Contains("{$DOMAIN}", caddyfile);
        Assert.Contains("reverse_proxy app:8080", caddyfile);
    }

    [Fact]
    public void Env_example_exists_and_the_real_env_file_is_gitignored_not_env_example()
    {
        Assert.True(Exists(".env.example"));
        // .gitignore is local-only by owner decision (2026-09-27): it is never committed, so a fresh
        // clone has none and there is nothing to check. Where it exists, the rules must hold.
        if (!Exists(".gitignore")) return;
        // Compare whole lines, not "\n.env\n": the file is checked out with CRLF or LF depending on the
        // machine's git settings, and the rule is the same either way.
        var lines = Read(".gitignore").Split('\n').Select(l => l.TrimEnd('\r').Trim()).ToList();
        Assert.Contains(".env", lines);
        Assert.Contains("!.env.example", lines);
    }

    [Fact]
    public void Dockerignore_excludes_secrets_and_tenant_uploads_from_the_build_context()
    {
        Assert.True(Exists(".dockerignore"));
        var di = Read(".dockerignore");
        Assert.Contains("appsettings.json", di);
        Assert.Contains("appsettings.*.json", di);
        Assert.Contains(".env\n", di);
        Assert.Contains("Blog.Web/wwwroot/uploads/**", di);
    }

    [Fact]
    public void Program_only_trusts_forwarded_headers_when_explicitly_enabled_and_builds_once()
    {
        var program = Read("Blog.Web/Program.cs");
        Assert.Contains("ReverseProxy:TrustForwardedHeaders", program);
        Assert.Contains("options.KnownNetworks.Clear();", program);
        Assert.Contains("options.KnownProxies.Clear();", program);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(program, @"^\s*var app = builder\.Build\(\);", System.Text.RegularExpressions.RegexOptions.Multiline).Count);
    }

    [Fact]
    public void Csproj_never_lists_an_individual_uploaded_file_as_a_build_item()
    {
        // The 2026-09-24 defect this release also fixed: a literal <Content>/<None> path for one
        // specific uploaded file only builds on the machine that happens to have it — including a
        // fresh clone, CI, and this very Docker build, which must exclude uploads from the image.
        var csproj = Read("Blog.Web/Blog.Web.csproj");
        var offenders = System.Text.RegularExpressions.Regex.Matches(csproj, @"<(?:Content|None) Include=""wwwroot\\uploads\\[^*""]+""")
            .Select(m => m.Value)
            .Where(v => !v.EndsWith(".gitkeep\"")) // an empty tracked placeholder, not tenant content — deliberate
            .ToList();
        Assert.True(offenders.Count == 0, "Literal per-file upload references in the csproj:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void Readme_documents_docker_deployment_with_the_real_config_keys()
    {
        var readme = Read("README.md");
        Assert.Contains("docker compose up", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ConnectionStrings__DefaultConnection", readme);
        // The fabricated env var name from the original bug report must never appear as if it works.
        Assert.DoesNotContain("Inkwell__Database__ConnectionString", readme);
    }
}
