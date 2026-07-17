using EmberTern.Core.Export.Sql;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>
/// Maps the driver's <see cref="FbDbType"/> onto Core's <see cref="SqlValueKind"/> — the one place
/// Firebird's type vocabulary is translated for SQL generation. It lives here, not in Core, because Core
/// has no FirebirdSql dependency (rule #1).
/// <para>
/// This map is the reason <see cref="SqlValueKind"/> exists at all: <c>Date</c> and <c>TimeStamp</c> are
/// the same CLR type, so only the <b>declared</b> type can say which literal format is faithful.
/// </para>
/// <para>
/// <b>Unmapped is a decision, not an omission.</b> Nine Firebird types have no kind — ARRAY, GUID, the
/// four FB4 <c>WITH TIME ZONE</c> types, both DECFLOATs, and INT128 — and each becomes
/// <see cref="SqlValueKind.Unknown"/>, which <see cref="SqlLiteralWriter"/> refuses. That is a loud,
/// safe outcome (the statement is not offered, with a reason) rather than a guessed literal, and adding
/// one later is purely additive. GUID is the subtle one: Firebird has no GUID type — the driver surfaces
/// a <c>CHAR(16) CHARACTER SET OCTETS</c> as one — so a faithful literal is a binary hex literal, not the
/// dashed text form <see cref="System.Guid.ToString()"/> would produce, and getting that wrong would be
/// silent corruption of a real key.
/// </para>
/// </summary>
public static class FirebirdValueKindMap
{
    /// <summary>The kind for <paramref name="type"/>, or <see cref="SqlValueKind.Unknown"/> when no
    /// faithful literal form is defined for it.</summary>
    public static SqlValueKind ToValueKind(FbDbType type) => type switch
    {
        FbDbType.SmallInt or FbDbType.Integer or FbDbType.BigInt => SqlValueKind.Integer,
        FbDbType.Numeric or FbDbType.Decimal => SqlValueKind.Decimal,
        FbDbType.Float or FbDbType.Double => SqlValueKind.Float,
        FbDbType.Char or FbDbType.VarChar => SqlValueKind.Text,
        FbDbType.Date => SqlValueKind.Date,
        FbDbType.Time => SqlValueKind.Time,
        FbDbType.TimeStamp => SqlValueKind.Timestamp,
        FbDbType.Boolean => SqlValueKind.Boolean,

        // BLOB SUB_TYPE 0 arrives as byte[] → an x'…' hex literal; SUB_TYPE TEXT arrives already
        // decoded as a string → quoted like text.
        FbDbType.Binary => SqlValueKind.BinaryBlob,
        FbDbType.Text => SqlValueKind.TextBlob,

        // Deliberately unmapped — see the type-level remarks. Listed explicitly rather than falling
        // through the discard, so this reads as the decision it is.
        FbDbType.Array => SqlValueKind.Unknown,
        FbDbType.Guid => SqlValueKind.Unknown,
        FbDbType.TimeStampTZ or FbDbType.TimeStampTZEx => SqlValueKind.Unknown,
        FbDbType.TimeTZ or FbDbType.TimeTZEx => SqlValueKind.Unknown,
        FbDbType.Dec16 or FbDbType.Dec34 => SqlValueKind.Unknown,
        FbDbType.Int128 => SqlValueKind.Unknown,

        _ => SqlValueKind.Unknown,
    };
}
