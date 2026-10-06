using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Two-factor sign-in (ADR-0021): the TOTP maths against the RFC test vectors, replay and drift rules,
/// recovery codes, the role policy, source scans that every sign-in path goes through the second step,
/// and an end-to-end sign-in through the real app on the dev database.
/// </summary>
public class TwoFactorTests
{
    private readonly ITestOutputHelper _out;
    public TwoFactorTests(ITestOutputHelper output) => _out = output;

    // RFC 6238 Appendix B, SHA-1 secret "12345678901234567890"; the 6-digit code is the last six digits.
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void Codes_match_the_rfc_6238_vectors(long unixSeconds, string expected)
    {
        var at = DateTime.UnixEpoch.AddSeconds(unixSeconds);
        Assert.Equal(expected, TwoFactor.CodeAt(RfcSecret, TwoFactor.StepAt(at)));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Base32_matches_rfc_4648(string text, string base32)
    {
        Assert.Equal(base32, TwoFactor.ToBase32(Encoding.ASCII.GetBytes(text)));
        Assert.Equal(text, Encoding.ASCII.GetString(TwoFactor.FromBase32(base32)));
    }

    [Fact]
    public void New_secrets_are_160_bits_and_round_trip_through_display_form()
    {
        var secret = TwoFactor.NewSecret();
        Assert.Equal(20, secret.Length);
        var shown = TwoFactor.ForDisplay(TwoFactor.ToBase32(secret));
        Assert.Matches(@"^([A-Z2-7]{4} )+[A-Z2-7]{1,4}$", shown);
        Assert.Equal(secret, TwoFactor.FromBase32(shown));          // typed with the spaces still works
    }

    [Fact]
    public void Verify_allows_one_step_of_drift_and_never_a_replay()
    {
        var now = new DateTime(2026, 10, 5, 12, 0, 15, DateTimeKind.Utc);
        var step = TwoFactor.StepAt(now);
        string Code(long s) => TwoFactor.CodeAt(RfcSecret, s);

        Assert.Equal(step, TwoFactor.Verify(RfcSecret, Code(step), now, null));
        Assert.Equal(step - 1, TwoFactor.Verify(RfcSecret, Code(step - 1), now, null));
        Assert.Equal(step + 1, TwoFactor.Verify(RfcSecret, Code(step + 1), now, null));
        Assert.Null(TwoFactor.Verify(RfcSecret, Code(step - 2), now, null));
        Assert.Null(TwoFactor.Verify(RfcSecret, Code(step + 2), now, null));

        Assert.Null(TwoFactor.Verify(RfcSecret, Code(step), now, lastUsedStep: step));        // same code again
        Assert.Null(TwoFactor.Verify(RfcSecret, Code(step - 1), now, lastUsedStep: step));    // an older one
        Assert.Equal(step + 1, TwoFactor.Verify(RfcSecret, Code(step + 1), now, lastUsedStep: step));

        var c = Code(step);
        Assert.Equal(step, TwoFactor.Verify(RfcSecret, c[..3] + " " + c[3..], now, null));    // "123 456" is fine
        foreach (var bad in new[] { null, "", "12345", "1234567", "12a456", "abcdef" })
            Assert.Null(TwoFactor.Verify(RfcSecret, bad, now, null));
    }

    [Fact]
    public void Recovery_codes_are_unique_hashed_and_single_use()
    {
        var codes = TwoFactor.NewRecoveryCodes();
        Assert.Equal(TwoFactor.RecoveryCodeCount, codes.Count);
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Matches("^[a-hjkmnp-z2-9]{5}-[a-hjkmnp-z2-9]{5}$", c));
        Assert.All(codes, c => Assert.True(TwoFactor.LooksLikeRecoveryCode(c)));
        Assert.False(TwoFactor.LooksLikeRecoveryCode("123456"));

        var hashes = codes.Select(TwoFactor.HashRecoveryCode).ToList();
        Assert.All(hashes, h => Assert.DoesNotContain(codes[0], h));

        var left = TwoFactor.UseRecoveryCode(hashes, " " + codes[3].ToUpperInvariant().Replace("-", "") + " ");
        Assert.NotNull(left);
        Assert.Equal(9, left!.Count);
        Assert.Null(TwoFactor.UseRecoveryCode(left, codes[3]));                                 // used up
        Assert.Null(TwoFactor.UseRecoveryCode(hashes, "aaaaa-aaaaa"));
    }

    [Fact]
    public void Requirement_is_per_role_and_off_by_default()
    {
        Assert.Empty(new UserSettings().TwoFactorRequiredRoles);
        Assert.False(TwoFactor.IsRequiredFor("Admin", new UserSettings().TwoFactorRequiredRoles));
        Assert.True(TwoFactor.IsRequiredFor("admin", new[] { "Admin" }));
        Assert.False(TwoFactor.IsRequiredFor("Author", new[] { "Admin", "Editor" }));
        Assert.False(TwoFactor.IsRequiredFor(null, new[] { "Admin" }));
        var old = JsonSerializer.Deserialize<UserSettings>("{\"SiteName\":\"Old\"}")!;
        Assert.Empty(old.TwoFactorRequiredRoles);
    }

    [Fact]
    public void Provisioning_uri_follows_the_key_uri_format()
    {
        var uri = TwoFactor.ProvisioningUri("Optical Software", "jane@example.com", "JBSWY3DPEHPK3PXP");
        Assert.Equal("otpauth://totp/Optical%20Software:jane%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=Optical%20Software&algorithm=SHA1&digits=6&period=30", uri);
    }

    // ── Every way in goes through the second step (source scans) ────────────────

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DBScripts", "init.sql"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    [Fact]
    public void Only_the_sign_in_service_issues_the_cookie()
    {
        var web = Path.Combine(new DirectoryInfo(Path.GetDirectoryName(RepoFileDir("Blog.Web"))!).FullName, "Blog.Web");
        var issuers = Directory.EnumerateFiles(web, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains(".SignInAsync(CookieAuthenticationDefaults"))
            .Select(Path.GetFileName).ToList();
        Assert.Equal(new[] { "UserSignInService.cs" }, issuers);
    }

    private static string RepoFileDir(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, name))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, name);
    }

    [Fact]
    public void Password_sign_in_checks_two_factor_before_signing_in()
    {
        var src = RepoFile("Blog.Web", "Controllers", "AccountController.cs");
        var login = src[src.IndexOf("public async Task<IActionResult> Login(string email", StringComparison.Ordinal)..];
        var end = login.IndexOf("[HttpPost(\"logout\")]", StringComparison.Ordinal);
        if (end > 0) login = login[..end];
        var check = login.IndexOf("GetTwoFactorAsync(user.Id))?.IsEnabled == true", StringComparison.Ordinal);
        var signIn = login.IndexOf("await SignInUser(user);", StringComparison.Ordinal);
        Assert.True(check > 0 && signIn > check, "Login must branch to the two-factor step before signing in.");

        var api = RepoFile("Blog.Web", "Controllers", "Api", "AuthApiController.cs");
        Assert.Contains("allowRecoveryCode: false", api);
        Assert.True(api.IndexOf("GetTwoFactorAsync", StringComparison.Ordinal) < api.IndexOf("GenerateAccessToken(user)", StringComparison.Ordinal));
        Assert.Contains("MustEnrolAsync", api);

        var ctrl = RepoFile("Blog.Web", "Controllers", "TwoFactorController.cs");
        Assert.Equal(Regex.Matches(ctrl, @"\[HttpPost\(").Count, Regex.Matches(ctrl, @"\[ValidateAntiForgeryToken\]").Count);
        foreach (var a in new[] { "TwoFactorEnabled", "TwoFactorDisabled", "TwoFactorReset", "TwoFactorRecoveryCodesRegenerated", "TwoFactorRecoveryCodeUsed", "TwoFactorFailed" })
            Assert.Contains("AuditActions." + a, ctrl);
        Assert.Contains("RegisterFailedLoginAsync", ctrl);
        Assert.Contains("[Authorize(Policy = \"AdminOnly\")]", ctrl[(ctrl.IndexOf("public async Task<IActionResult> Reset", StringComparison.Ordinal) - 200)..]);

        var program = RepoFile("Blog.Web", "Program.cs");
        var authz = program.IndexOf("app.UseAuthorization();", StringComparison.Ordinal);
        var enforce = program.IndexOf("UseMiddleware<Blog.Web.Middleware.TwoFactorEnforcementMiddleware>", StringComparison.Ordinal);
        Assert.True(authz > 0 && enforce > authz && enforce < program.IndexOf("app.MapControllers", StringComparison.Ordinal));
    }

    [Fact]
    public void Backups_carry_recovery_codes_but_the_secret_needs_the_key_ring()
    {
        // After a restore on a new server the encrypted secret cannot be read; recovery codes (plain hashes) still work.
        Assert.Equal(BackupTableRole.Content, BackupFormat.Find("Users")!.Role);
        var svc = RepoFile("Blog.Web", "Services", "TwoFactorService.cs");
        Assert.Contains("TwoFactorCheck.SecretUnreadable", svc);
        Assert.True(svc.IndexOf("LooksLikeRecoveryCode", StringComparison.Ordinal) < svc.IndexOf("_secrets.Unprotect", StringComparison.Ordinal),
            "Recovery codes must be checked before the secret is decrypted, so they work when it cannot be.");
    }

    // ── End to end through the app (dev database) ───────────────────────────────

    [Fact]
    public async Task A_user_with_two_factor_signs_in_only_with_a_valid_unused_code()
    {
        using var factory = new WebApplicationFactory<Program>();
        try
        {
            var cs = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
            using var probe = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            await probe.OpenAsync();
        }
        catch (Exception ex) { _out.WriteLine("Dev database unavailable — skipping. " + ex.Message); return; }

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        var email = $"2fa-test-{Guid.NewGuid():N}@example.test";
        const string password = "Correct-Horse-9";
        var userId = await users.CreateAsync(new User { Email = email, Username = "2fa-" + Guid.NewGuid().ToString("N")[..8], DisplayName = "2FA Test",
            PasswordHash = auth.HashPassword(password), Role = "Author", IsActive = true });

        try
        {
            // Turn it on through the service, exactly as the setup page does.
            var tf = scope.ServiceProvider.GetRequiredService<TwoFactorService>();
            var enrolment = await tf.StartEnrolmentAsync(email);
            Assert.StartsWith("<svg", enrolment.QrSvg.TrimStart().Substring(enrolment.QrSvg.TrimStart().IndexOf("<svg", StringComparison.Ordinal)));
            var secret = TwoFactor.FromBase32(enrolment.SecretForDisplay);
            var now = DateTime.UtcNow;
            Assert.Null(await tf.CompleteEnrolmentAsync(userId, enrolment.Token, "000000"));
            // A wrong code keeps the same secret: the entry already in the user's app must keep working.
            var resumed = await tf.ResumeEnrolmentAsync(email, enrolment.Token);
            Assert.Equal(enrolment.SecretForDisplay, resumed!.SecretForDisplay);
            Assert.Null(await tf.ResumeEnrolmentAsync(email, "not-a-token"));
            var codes = await tf.CompleteEnrolmentAsync(userId, enrolment.Token, TwoFactor.CodeAt(secret, TwoFactor.StepAt(now)));
            Assert.NotNull(codes);
            Assert.True((await users.GetByIdAsync(userId))!.TwoFactorEnabled);

            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
            async Task<string> Token(string url) =>
                Regex.Match(await client.GetStringAsync(url), "__RequestVerificationToken[^>]*value=\"([^\"]+)\"").Groups[1].Value;

            // Password alone: no sign-in, only the code step.
            var login = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["email"] = email, ["password"] = password, ["__RequestVerificationToken"] = await Token("/account/login"),
            }));
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            Assert.Equal("/account/two-factor", login.Headers.Location!.OriginalString);
            Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/admin/dashboard")).StatusCode);   // still signed out

            // A wrong code keeps them out.
            var wrong = await client.PostAsync("/account/two-factor", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = "000000", ["__RequestVerificationToken"] = await Token("/account/two-factor"),
            }));
            Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
            Assert.Contains("That code didn", await wrong.Content.ReadAsStringAsync());

            // A recovery code works once.
            var ok = await client.PostAsync("/account/two-factor", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = codes![0], ["__RequestVerificationToken"] = await Token("/account/two-factor"),
            }));
            Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
            Assert.Equal("/admin/dashboard", ok.Headers.Location!.OriginalString);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/dashboard")).StatusCode);
            Assert.Equal(9, await tf.RecoveryCodesLeftAsync(userId));

            // The API needs the code too, and the code used at enrolment cannot be replayed.
            var api = factory.CreateClient();
            var noCode = await api.PostAsync("/api/v1/auth/login", new StringContent(JsonSerializer.Serialize(new { email, password }), Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Unauthorized, noCode.StatusCode);
            var replay = await api.PostAsync("/api/v1/auth/login", new StringContent(JsonSerializer.Serialize(new { email, password, code = TwoFactor.CodeAt(secret, TwoFactor.StepAt(now)) }), Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
            var next = TwoFactor.CodeAt(secret, TwoFactor.StepAt(now) + 1);                    // the next step is within the drift window
            var withCode = await api.PostAsync("/api/v1/auth/login", new StringContent(JsonSerializer.Serialize(new { email, password, code = next }), Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, withCode.StatusCode);
        }
        finally
        {
            await users.DeleteAsync(userId);
        }
    }
}

public class TwoFactorEnforcementTests
{
    private static (Blog.Web.Middleware.TwoFactorEnforcementMiddleware Mw, Func<bool> NextCalled) Build()
    {
        var called = false;
        var mw = new Blog.Web.Middleware.TwoFactorEnforcementMiddleware(_ => { called = true; return Task.CompletedTask; });
        return (mw, () => called);
    }

    private static TwoFactorService Service(Guid userId, string[] requiredRoles, bool enrolled)
    {
        // The policy is read through a one-minute cache; seeding it means no database is needed.
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        cache.Set("2fa:roles", (IReadOnlyList<string>)requiredRoles.ToList());
        cache.Set($"2fa:on:{userId}", enrolled);
        return new TwoFactorService(null!, null!, cache, new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
    }

    private static Microsoft.AspNetCore.Http.DefaultHttpContext Ctx(Guid userId, string role, string path, bool htmx = false)
    {
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.Request.Path = path;
        if (htmx) ctx.Request.Headers["HX-Request"] = "true";
        ctx.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role),
        }, "Test"));
        return ctx;
    }

    [Fact]
    public async Task A_role_that_requires_it_is_sent_to_setup_before_any_desk_page()
    {
        var id = Guid.NewGuid();
        var (mw, next) = Build();
        var ctx = Ctx(id, "Author", "/admin/posts");
        await mw.InvokeAsync(ctx, Service(id, new[] { "Author" }, enrolled: false));
        Assert.False(next());
        Assert.Equal(302, ctx.Response.StatusCode);
        Assert.Equal("/account/two-factor/setup?required=true", ctx.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Htmx_requests_get_a_client_redirect()
    {
        var id = Guid.NewGuid();
        var (mw, next) = Build();
        var ctx = Ctx(id, "Admin", "/admin/posts/table", htmx: true);
        await mw.InvokeAsync(ctx, Service(id, new[] { "Admin" }, enrolled: false));
        Assert.False(next());
        Assert.Equal("/account/two-factor/setup?required=true", ctx.Response.Headers["HX-Redirect"].ToString());
    }

    [Theory]
    [InlineData("Author", new[] { "Author" }, true, "/admin/posts")]    // already set up
    [InlineData("Author", new[] { "Admin" }, false, "/admin/posts")]    // not required for this role
    [InlineData("Author", new[] { "Author" }, false, "/account/two-factor/setup")]   // the setup page itself
    [InlineData("Author", new[] { "Author" }, false, "/some-post")]     // the public site
    public async Task Everyone_else_passes_through(string role, string[] required, bool enrolled, string path)
    {
        var id = Guid.NewGuid();
        var (mw, next) = Build();
        await mw.InvokeAsync(Ctx(id, role, path), Service(id, required, enrolled));
        Assert.True(next());
    }
}
