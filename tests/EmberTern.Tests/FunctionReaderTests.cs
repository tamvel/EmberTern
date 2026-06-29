using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// SQL-shape regression pins for the Function Detail catalog readers. The actual
/// reads need a live Firebird (smoke), so these assert the queries target the right
/// catalog tables / type codes — the part that silently drifts.
/// </summary>
public class FunctionReaderTests
{
    [Fact]
    public void FunctionInfoSql_ReadsReturnArgAndDeterministicFromRdbFunctions()
    {
        var sql = FirebirdTableDetailReader.FunctionInfoSql;
        Assert.Contains("RDB$FUNCTIONS", sql);
        Assert.Contains("RDB$RETURN_ARGUMENT", sql);
        Assert.Contains("RDB$DETERMINISTIC_FLAG", sql);
        Assert.Contains("= @name", sql);
    }

    [Fact]
    public void FunctionArgumentsSql_JoinsArgumentsToFieldsByFieldSource()
    {
        var sql = FirebirdTableDetailReader.FunctionArgumentsSql;
        Assert.Contains("RDB$FUNCTION_ARGUMENTS", sql);
        Assert.Contains("RDB$FIELDS", sql);
        Assert.Contains("RDB$FIELD_SOURCE", sql);
        Assert.Contains("RDB$ARGUMENT_POSITION", sql);
        Assert.Contains("ORDER BY", sql);
    }

    [Fact]
    public void FunctionDependsOnSql_FiltersDependentType15()
    {
        var sql = FirebirdTableDetailReader.FunctionDependsOnSql;
        Assert.Contains("RDB$DEPENDENCIES", sql);
        Assert.Contains("RDB$DEPENDENT_TYPE = 15", sql);
        Assert.Contains("RDB$DEPENDED_ON_NAME", sql);
    }

    [Fact]
    public void FunctionDependedOnBySql_FiltersDependedOnType15()
    {
        var sql = FirebirdTableDetailReader.FunctionDependedOnBySql;
        Assert.Contains("RDB$DEPENDENCIES", sql);
        Assert.Contains("RDB$DEPENDED_ON_TYPE = 15", sql);
        Assert.Contains("RDB$DEPENDENT_NAME", sql);
    }
}
