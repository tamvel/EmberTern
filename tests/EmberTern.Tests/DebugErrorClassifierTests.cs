using EmberTern.Core.Sql.Debugging;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stage X / D15.4 Seam B — the pure code-only classification of a <see cref="DebugError"/> into a
/// <see cref="FriendlyErrorCategory"/>. The GDS codes are the ones MEASURED against the live FB5 lab (the
/// D15.4 probe): the leading at-or-above-ISC-base number the driver surfaces. The key case is that a
/// token-unknown / table-unknown / column-unknown all arrive as the same generic 335544569 ⇒ one SqlError
/// bucket (the precise split is Seam C's local pre-validation, not this).
/// </summary>
public sealed class DebugErrorClassifierTests
{
    [Fact]
    public void Null_IsUnknown()
        => Assert.Equal(FriendlyErrorCategory.Unknown, DebugErrorClassifier.Classify(null));

    [Fact]
    public void UserException_KeyedOnExceptionName()
    {
        // isc_except (335544517) → the mapper fills ExceptionName; that is the reliable signal.
        var e = new DebugError(ExceptionName: "E_CUSTOMER_NOT_FOUND", GdsCode: 335544517);
        Assert.Equal(FriendlyErrorCategory.UserException, DebugErrorClassifier.Classify(e));
    }

    [Fact]
    public void ExceptionName_WinsEvenWithAnotherGdsCode()
    {
        // A raise can carry other codes in the vector; ExceptionName still means "user exception".
        var e = new DebugError(ExceptionName: "E_X", GdsCode: 335544569);
        Assert.Equal(FriendlyErrorCategory.UserException, DebugErrorClassifier.Classify(e));
    }

    [Theory]
    [InlineData(335544879)] // NOT NULL validation (domain var)
    [InlineData(335544347)] // CHECK constraint (isc_not_valid)
    [InlineData(335544665)] // PRIMARY / UNIQUE key violation
    public void ConstraintCodes_AreConstraintViolation(long gds)
        => Assert.Equal(FriendlyErrorCategory.ConstraintViolation,
            DebugErrorClassifier.Classify(new DebugError(GdsCode: gds)));

    [Fact]
    public void DsqlError_IsSqlError()
    {
        // Syntax (-104), table-unknown (-204) and column-unknown (-206) ALL surface as 335544569.
        var e = new DebugError(GdsCode: 335544569, SqlState: "42000");
        Assert.Equal(FriendlyErrorCategory.SqlError, DebugErrorClassifier.Classify(e));
    }

    [Fact]
    public void UnrecognizedCode_IsUnknown()
        => Assert.Equal(FriendlyErrorCategory.Unknown,
            DebugErrorClassifier.Classify(new DebugError(GdsCode: 335544345, SqlState: "40001")));

    [Fact]
    public void NoCodeAtAll_IsUnknown()
        => Assert.Equal(FriendlyErrorCategory.Unknown,
            DebugErrorClassifier.Classify(new DebugError(Message: "something")));
}
