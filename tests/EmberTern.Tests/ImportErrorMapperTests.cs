using System;
using EmberTern.Core.Import;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Data Import — etap I4: mapping a Firebird refusal onto a structured cause.
/// <para>
/// ⭐ <b>Every vector below is one the I0 probe MEASURED against a live FB5 engine</b> (findings §2.6), copied
/// verbatim. That is what makes these tests worth having: they are not a restatement of the implementation,
/// they are the engine's own answers, frozen — so if a future Firebird changes a code, this fails and someone
/// re-measures instead of quietly shipping a wrong report.
/// </para>
/// <para>
/// The three <c>335544321</c> cases are the reason the mapper reads the vector at all. Under a mapper keyed on
/// <c>ErrorCode</c> they would be indistinguishable, and a user whose name is six characters too long would be
/// told they had an "arithmetic error".
/// </para>
/// </summary>
public class ImportErrorMapperTests
{
    private static ImportErrorKind Classify(params int[] vector)
        => FirebirdImportErrorMapper.Classify(vector, out _, out _);

    // ── The unambiguous classes ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NotNull()
        => Assert.Equal(
            ImportErrorKind.ServerNullViolation,
            Classify(335544347, 0, 0, 335544347));

    [Fact]
    public void Check()
        => Assert.Equal(
            ImportErrorKind.ServerCheckViolation,
            Classify(335544558, 0, 0, 335544842, 0, 335544558));

    [Fact]
    public void ForeignKey()
        => Assert.Equal(
            ImportErrorKind.ServerForeignKeyViolation,
            Classify(335544466, 0, 0, 335544838, 335545072, 0, 335544466));

    /// <summary>⭐ A PRIMARY KEY and a UNIQUE <b>constraint</b> produce the SAME vector at every depth, so the
    /// mapper reports "uniqueness violated" and does not pretend to know which. Claiming one would be inventing
    /// information (§0).</summary>
    [Fact]
    public void PrimaryKeyAndUniqueConstraint_AreOneKind_BecauseTheEngineDoesNotDistinguishThem()
    {
        var primaryKey = new[] { 335544665, 0, 0, 335545072, 0, 335544665 };
        var uniqueConstraint = new[] { 335544665, 0, 0, 335545072, 0, 335544665 };

        Assert.Equal(primaryKey, uniqueConstraint);        // the measurement itself
        Assert.Equal(ImportErrorKind.ServerUniqueViolation, Classify(primaryKey));
    }

    /// <summary>
    /// ⭐⭐ A standalone <c>CREATE UNIQUE INDEX</c> leads with a <b>different code</b> from the constraint form
    /// — and this one was found by the I4 LIVE run, not by I0.
    /// <para>
    /// I0 exercised a PK and a UNIQUE constraint, saw <c>335544665</c> for both, and concluded the two were
    /// interchangeable. They are. What it never exercised was an index created on its own, which reports
    /// <c>335544349</c> (<em>"attempt to store duplicate value … in unique index"</em>) — so until this was
    /// measured, the import reported a duplicate key as a generic <c>ServerError</c>.
    /// </para>
    /// <para>
    /// The lesson is the reason the etap requires a live run at all: the earlier measurement was correct about
    /// what it measured and incomplete about the world, and no amount of re-reading it would have shown that.
    /// </para>
    /// </summary>
    [Fact]
    public void AStandaloneUniqueIndex_LeadsWithADifferentCode_AndIsStillOneKind()
    {
        var uniqueIndex = new[] { 335544349, 0, 335545072, 0, 335544349 };

        Assert.NotEqual(FirebirdImportErrorMapper.GdsUniqueViolation, uniqueIndex[0]);
        Assert.Equal(ImportErrorKind.ServerUniqueViolation, Classify(uniqueIndex));
    }

    // ── ⭐ The three that share a leading code ───────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ THE test this mapper exists for. All three vectors lead with <c>335544321</c> and carry SQLSTATE
    /// 22000; only a later element tells them apart. A mapper reading <c>ErrorCode</c> would give all three the
    /// same, useless answer.
    /// </summary>
    [Fact]
    public void ThreeDifferentFailures_ShareALeadingCode_AndSeparateOnlyOnTheVector()
    {
        Assert.Equal(
            ImportErrorKind.ServerStringTruncation,
            Classify(335544321, 335544914, 335545033, 10, 16, 335544321));

        Assert.Equal(
            ImportErrorKind.ServerNumericOverflow,
            Classify(335544321, 335544916, 335544321));

        Assert.Equal(
            ImportErrorKind.ServerTransliterationFailed,
            Classify(335544321, 335544565, 335544321));
    }

    /// <summary>⭐ The vector carries the limit and the actual length as plain numbers, so "16 characters,
    /// limit 10" comes from the SERVER — never from parsing English out of a message that changes between
    /// versions and locales.</summary>
    [Fact]
    public void ATruncationVector_YieldsTheLimitAndTheActualLength()
    {
        FirebirdImportErrorMapper.Classify(
            new[] { 335544321, 335544914, 335545033, 10, 16, 335544321 }, out var limit, out var actual);

        Assert.Equal(10, limit);
        Assert.Equal(16, actual);
    }

    /// <summary>The numbers are found by VALUE, not by position: the measured vector puts another GDS code
    /// between the discriminator and the lengths, and nothing promises that stays put.</summary>
    [Fact]
    public void TheLengthsAreFoundPastAnyInterveningGdsCode()
    {
        FirebirdImportErrorMapper.Classify(
            new[] { 335544321, 335544914, 335545033, 335545072, 20, 26, 335544321 },
            out var limit, out var actual);

        Assert.Equal(20, limit);
        Assert.Equal(26, actual);
    }

    [Fact]
    public void OtherKinds_ReportNoLengths()
    {
        FirebirdImportErrorMapper.Classify(
            new[] { 335544347, 0, 0, 335544347 }, out var limit, out var actual);

        Assert.Null(limit);
        Assert.Null(actual);
    }

    // ── Honest fallbacks ────────────────────────────────────────────────────────────────────────────────

    /// <summary>A genuine arithmetic fault with no discriminator we recognise. An honest bucket that still
    /// carries the server's own message beats a guess dressed up as precision.</summary>
    [Fact]
    public void AnArithmeticFaultWithNoKnownDiscriminator_FallsBackHonestly()
        => Assert.Equal(ImportErrorKind.ServerError, Classify(335544321, 335544778, 335544321));

    [Fact]
    public void AnUnknownLeadingCode_IsServerError()
        => Assert.Equal(ImportErrorKind.ServerError, Classify(335544569, 0, 0));

    [Fact]
    public void AnEmptyOrMissingVector_IsServerError()
    {
        Assert.Equal(ImportErrorKind.ServerError, Classify());
        Assert.Equal(ImportErrorKind.ServerError, FirebirdImportErrorMapper.Classify(null!, out _, out _));
    }

    /// <summary>
    /// ⭐ The leading code decides the unambiguous classes, and that is not an optimization. The measured
    /// foreign-key vector shares element <c>335545072</c> with the primary-key one, so a mapper that scanned
    /// the whole vector for "any code it knows" would report a missing parent row as a duplicate key.
    /// </summary>
    [Fact]
    public void AWholeVectorScanWouldMisreadForeignKeyAsUnique_SoTheLeadingCodeDecides()
    {
        var foreignKey = new[] { 335544466, 0, 0, 335544838, 335545072, 0, 335544466 };

        Assert.Contains(335545072, foreignKey);   // the element both vectors carry
        Assert.Equal(ImportErrorKind.ServerForeignKeyViolation, Classify(foreignKey));
    }

    // ── The exception surface ───────────────────────────────────────────────────────────────────────────

    /// <summary>Something that is not a Firebird refusal at all — a dropped connection, a driver fault — is
    /// reported as such rather than classified into a data error the user would then hunt for in their file.</summary>
    [Fact]
    public void ANonFirebirdException_IsNotDressedUpAsADataError()
    {
        var result = FirebirdImportErrorMapper.Map(new InvalidOperationException("connection lost"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ImportErrorKind.ServerError, result.Kind);
        Assert.Equal("connection lost", result.ServerMessage);
    }

    [Fact]
    public void ANullException_StillProducesAFailure()
    {
        var result = FirebirdImportErrorMapper.Map(null);

        Assert.False(result.IsSuccess);
        Assert.Equal(ImportErrorKind.ServerError, result.Kind);
    }
}
