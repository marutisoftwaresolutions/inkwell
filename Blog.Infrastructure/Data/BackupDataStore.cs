using System.Buffers;
using System.Data;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Blog.Core.Services;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Blog.Infrastructure.Data;

/// <summary>
/// The database half of backup and restore (ADR-0019). Writes a table as JSON Lines and loads JSON
/// Lines back with <see cref="SqlBulkCopy"/>, keeping primary keys and identity values so every
/// reference between rows survives. Table names only ever come from <see cref="BackupFormat.Tables"/>.
/// </summary>
public sealed class BackupDataStore
{
    private const int BatchSize = 5000;
    private static readonly byte[] NewLine = { (byte)'\n' };
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly DapperContext _ctx;
    public BackupDataStore(DapperContext ctx) => _ctx = ctx;

    private static string Quote(string table) => "[" + table.Replace("]", "]]") + "]";

    private static void RequireCatalogTable(string table)
    {
        if (BackupFormat.Find(table) is not { Role: not BackupTableRole.Excluded })
            throw new ArgumentException($"{table} is not a table backups carry.", nameof(table));
    }

    /// <summary>The names of every user table in the database (for the catalog-completeness test and the backup writer).</summary>
    public async Task<IReadOnlyList<string>> ExistingTablesAsync()
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<string>("SELECT name FROM sys.tables WHERE is_ms_shipped = 0")).ToList();
    }

    /// <summary>True when the install has never been set up: no users and no settings.</summary>
    public async Task<bool> IsEmptyInstallAsync()
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteScalarAsync<int>("SELECT (SELECT COUNT(*) FROM Users) + (SELECT COUNT(*) FROM Settings)") == 0;
    }

    /// <summary>Streams every row of <paramref name="table"/> to <paramref name="output"/> as JSON Lines. Returns the row count.</summary>
    public async Task<long> WriteTableAsync(string table, Stream output, CancellationToken ct)
    {
        RequireCatalogTable(table);
        await using var conn = _ctx.CreateSqlConnection();
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT * FROM " + Quote(table), conn) { CommandTimeout = 0 };
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);

        var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        var buffer = new ArrayBufferWriter<byte>(4096);
        using var writer = new Utf8JsonWriter(buffer, WriterOptions);
        long rows = 0;

        while (await reader.ReadAsync(ct))
        {
            writer.WriteStartObject();
            for (var i = 0; i < names.Length; i++)
            {
                writer.WritePropertyName(names[i]);
                BackupFormat.WriteValue(writer, await reader.IsDBNullAsync(i, ct) ? null : reader.GetValue(i));
            }
            writer.WriteEndObject();
            writer.Flush();
            await output.WriteAsync(buffer.WrittenMemory, ct);
            await output.WriteAsync(NewLine, ct);
            buffer.Clear();
            writer.Reset(buffer);
            rows++;
        }
        return rows;
    }

    /// <summary>
    /// Loads a backup into an empty install in one transaction: every table a backup can carry is
    /// emptied (a fresh install seeds a default taxonomy), each supplied table is bulk-loaded with its
    /// original keys, and constraints are re-validated. Throws, leaving the database unchanged, when
    /// the install is not empty or any table fails to load.
    /// </summary>
    /// <param name="openTable">Opens the JSON Lines for a table, or returns null when the backup has none.</param>
    public async Task<RestoreLoadResult> RestoreAsync(Func<string, Stream?> openTable, CancellationToken ct)
    {
        await using var conn = _ctx.CreateSqlConnection();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);

        // One restore at a time, and the emptiness check inside the same transaction as the load.
        var locked = await conn.ExecuteScalarAsync<int>(
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = 'inkwell-restore', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0; SELECT @r;",
            transaction: tx);
        if (locked < 0)
            throw new InvalidOperationException("Another restore is already running on this database.");
        if (await conn.ExecuteScalarAsync<int>("SELECT (SELECT COUNT(*) FROM Users) + (SELECT COUNT(*) FROM Settings)", transaction: tx) != 0)
            throw new InvalidOperationException("This install already has users or settings. A backup restores only into a fresh install.");

        var existing = new HashSet<string>(
            await conn.QueryAsync<string>("SELECT name FROM sys.tables WHERE is_ms_shipped = 0", transaction: tx),
            StringComparer.OrdinalIgnoreCase);
        var tables = BackupFormat.TablesToClearOnRestore().Where(existing.Contains).ToList();

        foreach (var t in tables)
            await conn.ExecuteAsync($"ALTER TABLE {Quote(t)} NOCHECK CONSTRAINT ALL", transaction: tx, commandTimeout: 0);
        foreach (var t in tables)
            await conn.ExecuteAsync($"DELETE FROM {Quote(t)}", transaction: tx, commandTimeout: 0);

        var rows = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        foreach (var t in tables)
        {
            await using var stream = openTable(t);
            if (stream is null) continue;
            var (loaded, dropped) = await LoadTableAsync(conn, tx, t, stream, ct);
            rows[t] = loaded;
            if (dropped.Count > 0)
                warnings.Add($"{t}: column(s) {string.Join(", ", dropped)} in the backup no longer exist and were skipped.");
        }

        foreach (var t in tables)
        {
            // Validate references. A source site whose rows already broke a reference (constraints
            // disabled there at some point) still restores; the constraint is enabled untrusted,
            // exactly as it was, and the operator is told which table.
            try { await conn.ExecuteAsync($"ALTER TABLE {Quote(t)} WITH CHECK CHECK CONSTRAINT ALL", transaction: tx, commandTimeout: 0); }
            catch (SqlException)
            {
                await conn.ExecuteAsync($"ALTER TABLE {Quote(t)} CHECK CONSTRAINT ALL", transaction: tx, commandTimeout: 0);
                warnings.Add($"{t}: some rows refer to rows that do not exist (they did in the source site too). They were restored as they were.");
            }
        }

        var identityTables = await conn.QueryAsync<string>(
            "SELECT OBJECT_NAME(object_id) FROM sys.identity_columns", transaction: tx);
        foreach (var t in identityTables.Where(rows.ContainsKey))
            await conn.ExecuteAsync($"DBCC CHECKIDENT ('{Quote(t).Replace("'", "''")}') WITH NO_INFOMSGS", transaction: tx);

        await tx.CommitAsync(ct);
        return new RestoreLoadResult(rows, warnings);
    }

    private static async Task<(long Rows, List<string> Dropped)> LoadTableAsync(
        SqlConnection conn, SqlTransaction tx, string table, Stream stream, CancellationToken ct)
    {
        // Destination column types, as the CLR sees them.
        var destination = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new SqlCommand("SELECT TOP 0 * FROM " + Quote(table), conn, tx))
        await using (var schema = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, ct))
            for (var i = 0; i < schema.FieldCount; i++) destination[schema.GetName(i)] = schema.GetFieldType(i);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        DataTable? batch = null;
        List<(string Name, Type Type)> columns = new();
        var dropped = new List<string>();
        long rows = 0;

        using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls, tx)
        {
            DestinationTableName = Quote(table),
            BulkCopyTimeout = 0,
            BatchSize = BatchSize,
        };

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (line.Length == 0) continue;
            using var doc = JsonDocument.Parse(line);

            if (batch is null)
            {
                // The first row fixes the column set: the backup's columns that this schema still has.
                // A column the schema added since keeps its default; one it dropped is reported.
                batch = new DataTable(table);
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (destination.TryGetValue(p.Name, out var type))
                    {
                        columns.Add((p.Name, type));
                        batch.Columns.Add(p.Name, type);
                        bulk.ColumnMappings.Add(p.Name, p.Name);
                    }
                    else dropped.Add(p.Name);
                }
            }

            var row = batch.NewRow();
            foreach (var (name, type) in columns)
                row[name] = doc.RootElement.TryGetProperty(name, out var v) ? BackupFormat.ReadValue(v, type) : DBNull.Value;
            batch.Rows.Add(row);
            rows++;

            if (batch.Rows.Count >= BatchSize)
            {
                await bulk.WriteToServerAsync(batch, ct);
                batch.Clear();
            }
        }

        if (batch is { Rows.Count: > 0 }) await bulk.WriteToServerAsync(batch, ct);
        return (rows, dropped);
    }
}

public sealed record RestoreLoadResult(IReadOnlyDictionary<string, long> Rows, IReadOnlyList<string> Warnings);
