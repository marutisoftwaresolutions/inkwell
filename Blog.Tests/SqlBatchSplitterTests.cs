using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>GO handling for scheduled scripts must match sqlcmd/SSMS, or a script that works by hand fails on the schedule.</summary>
public class SqlBatchSplitterTests
{
    [Fact]
    public void Splits_on_GO_lines_and_drops_empty_batches()
    {
        var batches = SqlBatchSplitter.Split("SELECT 1\r\nGO\r\n\r\ngo\r\nSELECT 2\nGO -- trailing comment\n   GO   \nSELECT 3");
        Assert.Equal(new[] { "SELECT 1", "SELECT 2", "SELECT 3" }, batches);
    }

    [Fact]
    public void GO_with_a_count_repeats_the_batch()
    {
        var batches = SqlBatchSplitter.Split("INSERT t DEFAULT VALUES\nGO 3");
        Assert.Equal(3, batches.Count);
        Assert.All(batches, b => Assert.Equal("INSERT t DEFAULT VALUES", b));
    }

    [Fact]
    public void GO_inside_a_line_is_not_a_separator()
    {
        var batches = SqlBatchSplitter.Split("SELECT 'GO' AS word\nSELECT GOrdon FROM t\nUPDATE t SET x = 1 -- GO\n");
        Assert.Single(batches);
    }

    [Fact]
    public void A_script_without_GO_is_one_batch_and_empty_input_is_none()
    {
        Assert.Single(SqlBatchSplitter.Split("PRINT 'hi'"));
        Assert.Empty(SqlBatchSplitter.Split(""));
        Assert.Empty(SqlBatchSplitter.Split("GO\nGO"));
    }
}
