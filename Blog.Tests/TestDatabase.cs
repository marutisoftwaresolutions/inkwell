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
