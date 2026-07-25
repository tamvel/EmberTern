using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stage X — Firebird Debugger, milestone D2 seam (c): the pure error-mapping decision (spec §3.6). The
/// <see cref="DebugErrorMapper.Build"/> half is testable without a live server (an <c>FbException</c> cannot
/// be constructed in a test); its inputs are exactly what the driver shim reads. The concrete GDS/SQLSTATE
/// values are grounded against the live FB5 engine (the D2 seam c probe): a user <c>EXCEPTION</c> carries
/// <c>isc_except</c> (335544517) and its name on the message's first line; a domain <c>NOT NULL</c> validation
/// is SQLSTATE 42000 / GDS 335544879 with no <c>isc_except</c>.
/// </summary>
public class DebugErrorMapperTests
{
    [Fact]
    public void UserException_TakesNameFromFirstMessageLine_AndFlagsIscExcept()
    {
        var error = DebugErrorMapper.Build(
            sqlState: "HY000",
            message: "E_CUSTOMER_NOT_FOUND\nCustomer not found.\nAt block line: 1, col: 24",
            gdsNumbers: new long[] { 335544517, 1, 335544382, 0 });

        Assert.Equal("E_CUSTOMER_NOT_FOUND", error.ExceptionName);
        Assert.Equal(335544517, error.GdsCode);   // the first ISC-base code = isc_except
        Assert.Equal("HY000", error.SqlState);
        Assert.Null(error.SqlCode);                // legacy SQLCODE not distinctly exposed (D2 boundary)
    }

    [Fact]
    public void ValidationError_MapsGdsAndSqlState_ButNoExceptionName()
    {
        var error = DebugErrorMapper.Build(
            sqlState: "42000",
            message: "validation error for variable V, value \"*** null ***\"\nAt block line: 1, col: 53",
            gdsNumbers: new long[] { 335544879, 0, 0, 335544842, 0, 335544879 });

        Assert.Null(error.ExceptionName);          // no isc_except in the vector
        Assert.Equal(335544879, error.GdsCode);
        Assert.Equal("42000", error.SqlState);
    }

    [Fact]
    public void SmallVectorEntries_AreNotTreatedAsGdsCodes()
    {
        // 0 and 1 are argument/separator entries, not ISC error codes.
        var error = DebugErrorMapper.Build("HY000", "msg", new long[] { 0, 1, 335544345 });

        Assert.Equal(335544345, error.GdsCode);    // the first entry at/above the ISC base
    }

    [Fact]
    public void NoGdsCode_WhenVectorHasOnlyArgumentEntries()
    {
        var error = DebugErrorMapper.Build("00000", "ok", new long[] { 0, 1, 2 });

        Assert.Null(error.GdsCode);
        Assert.Null(error.ExceptionName);
    }

    [Fact]
    public void EmptySqlState_MapsToNull()
    {
        var error = DebugErrorMapper.Build(sqlState: "", message: null, gdsNumbers: System.Array.Empty<long>());

        Assert.Null(error.SqlState);
        Assert.Null(error.ExceptionName);
        Assert.Null(error.GdsCode);
    }
}
