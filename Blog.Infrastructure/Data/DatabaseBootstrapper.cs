using Blog.Core.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Blog.Infrastructure.Data;

/// <summary>
/// First-run setup, so the README's Quick Start works on a machine that has only SQL Server: when the
/// configured database does not exist it is created, and when it holds no Inkwell schema the embedded
/// <c>init.sql</c> (the same complete schema a manual install runs) is applied. An existing install is
/// never touched: once the <c>Users</c> table exists this does nothing and <see cref="MigrationService"/>
/// applies upgrades as before. Every step fails soft with a message that says what to do by hand,
/// because the login may lack the right to create a database (shared hosts, the demo).
/// </summary>
public sealed class DatabaseBootstrapper
{
    private const int CannotOpenDatabase = 4060;

    private readonly string _connectionString;
    private readonly ILogger<DatabaseBootstrapper> _log;

    public DatabaseBootstrapper(string connectionString, ILogger<DatabaseBootstrapper> log)
    {
        _connectionString = connectionString;
        _log = log;
    }

    /// <summary>What the bootstrapper did; <see cref="Problem"/> is set when the operator must act.</summary>
    public sealed record Result(bool CreatedDatabase, bool AppliedSchema, string? Problem);

    public async Task<Result> EnsureAsync(CancellationToken ct = default)
    {
        var created = false;
        try
        {
            if (!await CanOpenAsync(ct))
            {
                await CreateDatabaseAsync(ct);
                created = true;
            }

            if (await HasSchemaAsync(ct)) return new Result(created, false, null);

            var script = ReadInitScript();
            if (script is null)
                return new Result(created, false, "The database is empty and the bundled schema script is missing from this build. Run DBScripts/init.sql against it by hand.");

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            foreach (var batch in SqlBatchSplitter.Split(script))
            {
                await using var cmd = new SqlCommand(batch, conn) { CommandTimeout = 300 };
                await cmd.ExecuteNonQueryAsync(ct);
            }
            _log.LogInformation("First run: the Inkwell schema was created from the bundled init.sql.");
            return new Result(created, true, null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "First-run database setup failed.");
            return new Result(created, false,
                "The database could not be prepared automatically (" + ex.Message.Split('\n')[0].Trim() +
                "). Create the database named in ConnectionStrings:DefaultConnection and run DBScripts/init.sql against it, then restart.");
        }
    }

    /// <summary>True when the target database opens; false only for "database does not exist".</summary>
    private async Task<bool> CanOpenAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            return true;
        }
        catch (SqlException ex) when (ex.Number == CannotOpenDatabase)
        {
            return false;
        }
    }

    private async Task CreateDatabaseAsync(CancellationToken ct)
    {
        var builder = new SqlConnectionStringBuilder(_connectionString);
        var name = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("The connection string names no database.");

        builder.InitialCatalog = "master";
        await using var conn = new SqlConnection(builder.ConnectionString);
        await conn.OpenAsync(ct);
        // QUOTENAME on the server side: the name comes from configuration, never from a request.
        await using var cmd = new SqlCommand(
            "IF DB_ID(@name) IS NULL BEGIN DECLARE @sql NVARCHAR(400) = N'CREATE DATABASE ' + QUOTENAME(@name); EXEC (@sql); END", conn);
        cmd.Parameters.AddWithValue("@name", name);
        await cmd.ExecuteNonQueryAsync(ct);
        SqlConnection.ClearAllPools();   // the failed attempts above poisoned nothing, but start clean
        _log.LogInformation("First run: database {Database} was created.", name);
    }

    private async Task<bool> HasSchemaAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(N'dbo.Users', N'U') IS NULL THEN 0 ELSE 1 END", conn);
        return (int)(await cmd.ExecuteScalarAsync(ct) ?? 0) == 1;
    }

    /// <summary>The embedded copy of <c>DBScripts/init.sql</c>; null if the build did not carry it.</summary>
    public static string? ReadInitScript()
    {
        using var stream = typeof(DatabaseBootstrapper).Assembly.GetManifestResourceStream("Blog.Infrastructure.Init.sql");
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
