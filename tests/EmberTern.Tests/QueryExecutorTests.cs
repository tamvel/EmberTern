using System.Threading.Tasks;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

public class QueryExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_WithEmptySql_Throws()
    {
        using var service = new FirebirdConnectionService();
        var executor = new FirebirdQueryExecutor(service);

        var ex = await Assert.ThrowsAsync<QueryExecutionException>(
            () => executor.ExecuteAsync(""));

        Assert.Contains("empty", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutConnection_ThrowsCleanError()
    {
        using var service = new FirebirdConnectionService();
        var executor = new FirebirdQueryExecutor(service);

        var ex = await Assert.ThrowsAsync<QueryExecutionException>(
            () => executor.ExecuteAsync("SELECT 1 FROM RDB$DATABASE"));

        Assert.DoesNotContain("Exception", ex.Message);
        Assert.Contains("connection", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RowLimit_DefaultsTo5000()
    {
        using var service = new FirebirdConnectionService();
        var executor = new FirebirdQueryExecutor(service);

        Assert.Equal(5000, executor.RowLimit);
        Assert.Equal(5000, FirebirdQueryExecutor.DefaultRowLimit);
    }
}
