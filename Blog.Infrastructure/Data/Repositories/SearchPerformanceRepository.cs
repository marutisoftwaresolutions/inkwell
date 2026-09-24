using System.Data;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Blog.Infrastructure.Data.Repositories;

public class SearchPerformanceRepository : ISearchPerformanceRepository
{
    private readonly DapperContext _ctx;
    public SearchPerformanceRepository(DapperContext ctx) => _ctx = ctx;

    /// <summary>
    /// Replace one owner-day: delete whatever is stored for the date, then bulk-insert the new rows in
    /// the same transaction, so a reader never sees a half-written day. Bulk copy rather than one INSERT
    /// per row because the first sync backfills 16 months — hundreds of thousands of rows — and a
    /// round trip per row turned that into hours.
    /// </summary>
    public async Task ReplaceDayAsync(Guid ownerId, DateTime date, IReadOnlyList<SearchPerformanceRow> rows)
    {
        using var conn = _ctx.CreateSqlConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();
        try
        {
            await conn.ExecuteAsync("DELETE FROM SearchPerformance WHERE OwnerId = @OwnerId AND [Date] = @Date",
                new { OwnerId = ownerId, Date = date.Date }, tx);

            if (rows.Count > 0)
            {
                using var table = BuildTable(ownerId, date.Date, rows);
                using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx)
                {
                    DestinationTableName = "SearchPerformance",
                    BatchSize = 5000,
                    BulkCopyTimeout = 120
                };
                // Map by name so the IDENTITY Id and any future column order change cannot shift data.
                foreach (DataColumn c in table.Columns) bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
                await bulk.WriteToServerAsync(table);
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>The exact insertable column set of SearchPerformance (everything but the IDENTITY Id).</summary>
    private static DataTable BuildTable(Guid ownerId, DateTime date, IReadOnlyList<SearchPerformanceRow> rows)
    {
        var table = new DataTable();
        table.Columns.Add("OwnerId",     typeof(Guid));
        table.Columns.Add("Date",        typeof(DateTime));
        table.Columns.Add("Page",        typeof(string));
        table.Columns.Add("Query",       typeof(string));
        table.Columns.Add("Clicks",      typeof(int));
        table.Columns.Add("Impressions", typeof(int));
        table.Columns.Add("Ctr",         typeof(double));
        table.Columns.Add("Position",    typeof(double));
        foreach (var r in rows)
            table.Rows.Add(ownerId, date, Clip(r.Page, 1024), Clip(r.Query, 512), r.Clicks, r.Impressions, r.Ctr, r.Position);
        return table;
    }

    public async Task<IReadOnlyList<PageSearchDay>> GetPageDaysAsync(Guid ownerId, DateTime fromDate, DateTime toDate)
    {
        using var conn = _ctx.CreateConnection();
        var rows = await conn.QueryAsync<PageSearchDay>(@"
            SELECT Page, [Date],
                   SUM(Clicks)      AS Clicks,
                   SUM(Impressions) AS Impressions,
                   CASE WHEN SUM(Impressions) = 0 THEN 0
                        ELSE SUM(Position * Impressions) / SUM(Impressions) END AS Position
            FROM SearchPerformance
            WHERE OwnerId = @OwnerId AND [Date] >= @From AND [Date] <= @To
            GROUP BY Page, [Date]",
            new { OwnerId = ownerId, From = fromDate.Date, To = toDate.Date });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<SearchQueryStat>> GetPageQueriesAsync(Guid ownerId, string page, DateTime fromDate, DateTime toDate, int take = 10)
    {
        using var conn = _ctx.CreateConnection();
        var rows = await conn.QueryAsync<SearchQueryStat>(@"
            SELECT TOP (@Take) Query,
                   SUM(Clicks)      AS Clicks,
                   SUM(Impressions) AS Impressions,
                   CASE WHEN SUM(Impressions) = 0 THEN 0 ELSE CAST(SUM(Clicks) AS FLOAT) / SUM(Impressions) END AS Ctr,
                   CASE WHEN SUM(Impressions) = 0 THEN 0 ELSE SUM(Position * Impressions) / SUM(Impressions) END AS Position
            FROM SearchPerformance
            WHERE OwnerId = @OwnerId AND Page = @Page AND [Date] >= @From AND [Date] <= @To AND Query <> ''
            GROUP BY Query
            ORDER BY SUM(Clicks) DESC, SUM(Impressions) DESC",
            new { OwnerId = ownerId, Page = page, From = fromDate.Date, To = toDate.Date, Take = take });
        return rows.ToList();
    }

    public async Task<DateTime?> GetLatestDateAsync(Guid ownerId)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteScalarAsync<DateTime?>(
            "SELECT MAX([Date]) FROM SearchPerformance WHERE OwnerId = @OwnerId", new { OwnerId = ownerId });
    }

    public async Task<int> PruneAsync(Guid ownerId, DateTime olderThanDate)
    {
        using var conn = _ctx.CreateConnection();
        return await conn.ExecuteAsync("DELETE FROM SearchPerformance WHERE OwnerId = @OwnerId AND [Date] < @Cutoff",
            new { OwnerId = ownerId, Cutoff = olderThanDate.Date });
    }

    public async Task<int> DeleteForOwnerAsync(Guid ownerId)
    {
        using var conn = _ctx.CreateConnection();
        // Seeks IX_SearchPerformance_Owner_Date; a full backfill is a few hundred thousand rows, so
        // give it longer than the default 30 seconds rather than leave a half-cleared owner behind.
        return await conn.ExecuteAsync("DELETE FROM SearchPerformance WHERE OwnerId = @OwnerId",
            new { OwnerId = ownerId }, commandTimeout: 120);
    }

    private static string Clip(string? s, int max) => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max]);
}
