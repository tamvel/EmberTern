using System;
using System.Linq;
using EmberTern.Core.Export.Sql;
using EmberTern.Firebird;
using FirebirdSql.Data.FirebirdClient;
using Xunit;

namespace EmberTern.Tests;

// E2 — the Firebird→Core type translation. Pure; no DB needed (the enum is the driver's, the kinds are
// ours). The engine's side of this — which CLR value each type actually produces — is pinned by the
// live round-trip probe, not here.
public class FirebirdValueKindMapTests
{
    [Theory]
    [InlineData(FbDbType.SmallInt, SqlValueKind.Integer)]
    [InlineData(FbDbType.Integer, SqlValueKind.Integer)]
    [InlineData(FbDbType.BigInt, SqlValueKind.Integer)]
    [InlineData(FbDbType.Numeric, SqlValueKind.Decimal)]
    [InlineData(FbDbType.Decimal, SqlValueKind.Decimal)]
    [InlineData(FbDbType.Float, SqlValueKind.Float)]
    [InlineData(FbDbType.Double, SqlValueKind.Float)]
    [InlineData(FbDbType.Char, SqlValueKind.Text)]
    [InlineData(FbDbType.VarChar, SqlValueKind.Text)]
    [InlineData(FbDbType.Date, SqlValueKind.Date)]
    [InlineData(FbDbType.Time, SqlValueKind.Time)]
    [InlineData(FbDbType.TimeStamp, SqlValueKind.Timestamp)]
    [InlineData(FbDbType.Boolean, SqlValueKind.Boolean)]
    [InlineData(FbDbType.Binary, SqlValueKind.BinaryBlob)]
    [InlineData(FbDbType.Text, SqlValueKind.TextBlob)]
    public void A_Supported_Type_Maps_To_Its_Kind(FbDbType type, SqlValueKind expected)
        => Assert.Equal(expected, FirebirdValueKindMap.ToValueKind(type));

    // DATE and TIMESTAMP are the same CLR type — this map is the ONLY thing that separates them, and
    // getting it wrong either invents a '00:00:00' or drops a real time.
    [Fact]
    public void Date_And_Timestamp_Are_Different_Kinds_Despite_One_Clr_Type()
        => Assert.NotEqual(
            FirebirdValueKindMap.ToValueKind(FbDbType.Date),
            FirebirdValueKindMap.ToValueKind(FbDbType.TimeStamp));

    // Refusing is the safe outcome for a type we have no faithful literal for; each of these is a
    // conscious decision, and a test so that "map it" is a deliberate act with a measured literal.
    [Theory]
    [InlineData(FbDbType.Array)]      // no literal form
    [InlineData(FbDbType.Guid)]       // really CHAR(16) OCTETS — the dashed text form would corrupt it
    [InlineData(FbDbType.Dec16)]      // FB4 DECFLOAT(16)
    [InlineData(FbDbType.Dec34)]      // FB4 DECFLOAT(34)
    [InlineData(FbDbType.Int128)]     // FB4 INT128
    [InlineData(FbDbType.TimeTZ)]
    [InlineData(FbDbType.TimeTZEx)]
    [InlineData(FbDbType.TimeStampTZ)]
    [InlineData(FbDbType.TimeStampTZEx)]
    public void An_Unmapped_Type_Is_Unknown_And_Therefore_Refused(FbDbType type)
    {
        Assert.Equal(SqlValueKind.Unknown, FirebirdValueKindMap.ToValueKind(type));
        // …and Unknown is what makes SqlLiteralWriter refuse — the two halves of the guarantee.
        Assert.False(SqlLiteralWriter.Write(1, SqlValueKind.Unknown).IsWritten);
    }

    // The coverage guarantee: every member of the driver's enum must be an explicit decision. If a
    // future driver version adds a type, this fails and forces someone to choose — rather than letting
    // it fall silently into a default. (The same "make the seam impossible to miss" rule the editor
    // milestones learned the hard way.)
    [Fact]
    public void Every_Driver_Type_Is_Decided()
    {
        var undecided = Enum.GetValues<FbDbType>()
            .Where(t => FirebirdValueKindMap.ToValueKind(t) == SqlValueKind.Unknown)
            .Select(t => t.ToString())
            .ToArray();

        // Exactly the nine deliberate refusals — no more, no fewer.
        Assert.Equal(
            new[] { "Array", "Dec16", "Dec34", "Guid", "Int128", "TimeStampTZ", "TimeStampTZEx", "TimeTZ", "TimeTZEx" },
            undecided.OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }
}
