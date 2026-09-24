using Microsoft.Data.SqlClient;
using System.Data;

namespace Blog.Infrastructure.Data;

public class DapperContext
{
    private readonly string _connectionString;

    public DapperContext(string connectionString)
    {
        _connectionString = connectionString;
    }

    public IDbConnection CreateConnection()
    {
        var conn = new SqlConnection(_connectionString);
        conn.Open();
        return conn;
    }

    /// <summary>
    /// An unopened <see cref="SqlConnection"/> for the few callers that need the provider type
    /// (bulk copy, transactions) and want to open it asynchronously. The caller owns opening and disposal.
    /// </summary>
    public SqlConnection CreateSqlConnection() => new(_connectionString);
}
