using System.Diagnostics;
using System.Text;
using Blog.Core.Domain;
using Blog.Core.Services;
using Blog.Infrastructure.Data;
using Microsoft.Data.SqlClient;

namespace Blog.Web.Services.Jobs;

/// <summary>Configuration for Admin → Scheduled scripts (appsettings <c>ScheduledScripts</c> section).</summary>
public sealed class ScheduledScriptOptions
{
    /// <summary>Folder the Desk may pick script files from, relative to the content root (or absolute). Default App_Data/jobs.</summary>
    public string Folder { get; set; } = Path.Combine("App_Data", "jobs");
    /// <summary>Per-batch command timeout.</summary>
    public int TimeoutSeconds { get; set; } = 600;
    /// <summary>Schedules fixed in configuration; shown read-only in the Desk and re-asserted on every start.</summary>
    public List<ConfigScript> Jobs { get; set; } = new();

    public sealed class ConfigScript
    {
        public string Name { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public string Frequency { get; set; } = ScriptFrequencies.Daily;
        /// <summary>Day name or number (Sunday = 0); weekly only.</summary>
        public string? DayOfWeek { get; set; }
        /// <summary>"HH:mm" in UTC.</summary>
        public string TimeUtc { get; set; } = "03:00";
    }
}

/// <summary>
/// Runs one <see cref="ScheduledScript"/>: reads the file from the configured folder (and only from
/// there), splits it on GO, executes each batch through SqlClient on the app's own connection, and
/// captures PRINT / RAISERROR messages as the operator-visible output. A failing batch stops the
/// script and marks the run failed; nothing here throws to the caller.
/// </summary>
public sealed class ScheduledScriptRunner
{
    public const int MaxOutputChars = 20_000;
    private readonly DapperContext _ctx;
    private readonly ScheduledScriptOptions _options;
    private readonly IWebHostEnvironment _env;

    public ScheduledScriptRunner(DapperContext ctx, ScheduledScriptOptions options, IWebHostEnvironment env)
    {
        _ctx = ctx; _options = options; _env = env;
    }

    public string FolderPath => Path.IsPathRooted(_options.Folder) ? _options.Folder : Path.Combine(_env.ContentRootPath, _options.Folder);

    /// <summary>The .sql files the Desk may schedule, by name, sorted.</summary>
    public IReadOnlyList<string> AvailableFiles()
    {
        if (!Directory.Exists(FolderPath)) return Array.Empty<string>();
        return Directory.EnumerateFiles(FolderPath, "*.sql", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public bool FileExists(string fileName) =>
        !string.IsNullOrWhiteSpace(fileName) && fileName.IndexOfAny(['/', '\\']) < 0 && !fileName.Contains("..")
        && File.Exists(Path.Combine(FolderPath, fileName));

    public sealed record RunResult(bool Succeeded, string Output, int DurationMs);

    public async Task<RunResult> RunAsync(ScheduledScript script, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var log = new StringBuilder();
        if (!FileExists(script.ScriptFile))
        {
            log.AppendLine($"Script file not found in {FolderPath}: {script.ScriptFile}");
            return new RunResult(false, log.ToString(), (int)sw.ElapsedMilliseconds);
        }

        string text;
        try { text = await File.ReadAllTextAsync(Path.Combine(FolderPath, script.ScriptFile), Encoding.UTF8, ct); }
        catch (Exception ex) { return new RunResult(false, $"Could not read {script.ScriptFile}: {ex.Message}", (int)sw.ElapsedMilliseconds); }

        var batches = SqlBatchSplitter.Split(text);
        log.AppendLine($"{script.ScriptFile}: {batches.Count} batch(es).");

        try
        {
            await using var conn = _ctx.CreateSqlConnection();
            conn.InfoMessage += (_, e) => { if (log.Length < MaxOutputChars) log.AppendLine(e.Message); };
            conn.FireInfoMessageEventOnUserErrors = false;
            await conn.OpenAsync(ct);
            var i = 0;
            foreach (var batch in batches)
            {
                i++;
                ct.ThrowIfCancellationRequested();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = batch;
                cmd.CommandTimeout = Math.Max(30, _options.TimeoutSeconds);
                try { await cmd.ExecuteNonQueryAsync(ct); }
                catch (SqlException ex)
                {
                    log.AppendLine($"Batch {i} failed: Msg {ex.Number}, Level {ex.Class}, Line {ex.LineNumber}: {ex.Message}");
                    return new RunResult(false, Trim(log), (int)sw.ElapsedMilliseconds);
                }
            }
            log.AppendLine($"Done in {sw.Elapsed.TotalSeconds:n1} s.");
            return new RunResult(true, Trim(log), (int)sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) { log.AppendLine("Cancelled (application stopping)."); return new RunResult(false, Trim(log), (int)sw.ElapsedMilliseconds); }
        catch (Exception ex) { log.AppendLine("Failed: " + ex.Message); return new RunResult(false, Trim(log), (int)sw.ElapsedMilliseconds); }
    }

    private static string Trim(StringBuilder sb) => sb.Length <= MaxOutputChars ? sb.ToString() : sb.ToString(0, MaxOutputChars) + "\n… (output truncated)";
}
