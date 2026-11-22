using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Blog.Tests;

/// <summary>
/// The one "is the database there?" check for render tests. A test that needs rows calls
/// <see cref="Available"/> first and returns when it is false, so a fresh clone with no SQL Server
/// reports zero failures instead of a wall of 500s. Probed once per run with a short timeout.
/// </summary>
public static class TestDatabase
{
    /// <summary>What a test writes when it leaves early because the API has no signing key.</summary>
    public const string NoApiKey = "Jwt:Key is not set on this machine, so the REST API signs nobody in; skipping the API part.";

    /// <summary>
    /// Whether the REST API can sign anyone in here. It needs a real <c>Jwt:Key</c>, which lives in the local settings
    /// file and never in the repository, so a fresh clone has none: the API then answers 503 by design, and a test that
    /// needs a signed-in API caller leaves early instead of failing. CI sets a throwaway key so these tests run there.
    /// </summary>
    public static bool ApiConfigured(IServiceProvider services) =>
        Blog.Core.Services.ApiSigningKey.IsUsable(services.GetRequiredService<IConfiguration>()["Jwt:Key"]);

    private static readonly object Gate = new();
    private static bool? _available;

    public static bool Available(WebApplicationFactory<Program> factory)
    {
        lock (Gate)
        {
            if (_available is not null) return _available.Value;
            try
            {
                var cs = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(cs)) return (_available = false).Value;
                using var conn = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
                conn.Open();
                using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(N'dbo.Users', N'U') IS NULL THEN 0 ELSE 1 END", conn);
                _available = (int)cmd.ExecuteScalar()! == 1;
            }
            catch { _available = false; }
            return _available.Value;
        }
    }
}
