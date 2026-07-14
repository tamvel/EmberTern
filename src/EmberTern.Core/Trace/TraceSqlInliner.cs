using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using EmberTern.Core.Sql.Language;

namespace EmberTern.Core.Trace;

/// <summary>
/// Presentation-only expansion of a parameterised trace SQL into a readable form with the
/// captured parameter values inlined (<c>WHERE ID_NAGL = ?</c> → <c>WHERE ID_NAGL = 10036</c>)
/// — the reverse-engineering aid (V1.1). The result is a DISPLAY string, never executed, so
/// it is not a re-run guarantee (usually runnable, but we don't promise it). Pure; no DB, no
/// Avalonia; reuses the shared quote/comment-skipping scanner so a placeholder inside a
/// literal is never touched.
/// <para>
/// Safety / limitations (by design):
/// <list type="bullet">
/// <item>Substitutes ONLY top-level positional <c>?</c> placeholders — a <c>?</c> inside a
/// string literal, quoted identifier, or comment is left verbatim.</item>
/// <item>Inlines ONLY when the top-level <c>?</c> count equals the parameter count; otherwise
/// returns the SQL unchanged. This guards a <c>MaxSQLLength</c>-truncated statement (fewer
/// <c>?</c> than params) and any other mismatch — the faithful source is shown instead.</item>
/// <item><c>NULL</c> → <c>NULL</c>; numeric/boolean → verbatim; char/text and date/time/
/// timestamp → single-quoted (internal <c>'</c> doubled). A timestamp's ISO <c>T</c> date/time
/// separator is normalized to a space so the literal is valid Firebird. BLOB/array/unknown are
/// NOT inlined — the <c>?</c> is left in place (the value stays visible in the detail
/// Parameters list).</item>
/// </list>
/// </para>
/// </summary>
public static class TraceSqlInliner
{
    /// <summary>Returns <paramref name="sql"/> with each top-level <c>?</c> replaced by the
    /// formatted value of the corresponding <paramref name="parameters"/> entry, or the SQL
    /// unchanged when there are no parameters or the placeholder count doesn't match.</summary>
    /// <remarks>
    /// Tokenization is delegated to the shared <see cref="SqlLexer"/> (Etap 1): the positional
    /// <c>?</c> markers are its <see cref="TokenKind.Parameter"/> tokens whose text is <c>"?"</c>
    /// (a <c>?</c> inside a string literal, quoted identifier, or comment never becomes such a
    /// token; <c>:name</c>/<c>@name</c> are parameters but are left untouched). The result is
    /// rebuilt by copying the source verbatim between the <c>?</c> spans — so every non-
    /// substituted character is byte-for-byte identical to the input (§0 Paramount Law).
    /// </remarks>
    public static string Inline(string? sql, IReadOnlyList<RawTraceParam>? parameters)
    {
        if (string.IsNullOrEmpty(sql)) return sql ?? string.Empty;
        if (parameters is null || parameters.Count == 0) return sql!;

        var marks = new List<SqlToken>();
        foreach (var t in SqlLexer.Tokenize(sql!))
        {
            if (t.Kind == TokenKind.Parameter && t.Text == "?")
            {
                marks.Add(t);
            }
        }
        if (marks.Count != parameters.Count) return sql!; // mismatch → faithful source

        var sb = new StringBuilder(sql!.Length + 32);
        int last = 0;
        for (int p = 0; p < marks.Count; p++)
        {
            var m = marks[p];
            sb.Append(sql, last, m.Start - last); // verbatim gap (literals/comments/other text)
            sb.Append(Format(parameters[p]));
            last = m.End;
        }
        sb.Append(sql, last, sql.Length - last);
        return sb.ToString();
    }

    private enum Category { Numeric, Text, Temporal, Boolean, NonInlinable }

    private static string Format(RawTraceParam param)
    {
        if (param.Value is null) return "NULL";
        return Classify(param.DataType) switch
        {
            Category.Numeric => param.Value.Trim(),
            Category.Boolean => param.Value.Trim(),
            Category.Text => Quote(param.Value),
            Category.Temporal => Quote(NormalizeTemporal(param.Value)),
            _ => "?", // BLOB / array / unknown — keep the placeholder; the value stays in the Parameters list
        };
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    // Firebird's trace captures a TIMESTAMP parameter with the ISO 'T' date/time separator
    // ("1899-12-30T00:00:00"), but a Firebird datetime LITERAL rejects the 'T' — so an inlined
    // timestamp pasted back into the editor won't run. Convert that single separator to the space
    // Firebird expects, making the inlined SQL runnable (the inliner's whole purpose). A value
    // that's already space-separated, date-only, or time-only doesn't match and is left verbatim.
    private static readonly Regex IsoTemporalRx =
        new(@"^(\d{4}-\d{2}-\d{2})T(.+)$", RegexOptions.Compiled);

    private static string NormalizeTemporal(string value)
    {
        var v = value.Trim();
        var m = IsoTemporalRx.Match(v);
        return m.Success ? m.Groups[1].Value + " " + m.Groups[2].Value : v;
    }

    private static Category Classify(string? dataType)
    {
        var t = (dataType ?? string.Empty).Trim().ToLowerInvariant();
        if (t.Length == 0) return Category.NonInlinable;
        if (t.StartsWith("blob", StringComparison.Ordinal) || t.Contains("array")) return Category.NonInlinable;
        if (t.Contains("char") || t.Contains("string") || t.Contains("text")) return Category.Text;
        if (t.StartsWith("timestamp", StringComparison.Ordinal)
            || t.StartsWith("date", StringComparison.Ordinal)
            || t.StartsWith("time", StringComparison.Ordinal)) return Category.Temporal;
        if (t.StartsWith("bool", StringComparison.Ordinal)) return Category.Boolean;
        if (t.Contains("int") || t.StartsWith("numeric", StringComparison.Ordinal)
            || t.StartsWith("dec", StringComparison.Ordinal) || t.StartsWith("double", StringComparison.Ordinal)
            || t.StartsWith("float", StringComparison.Ordinal) || t.StartsWith("real", StringComparison.Ordinal)
            || t.StartsWith("number", StringComparison.Ordinal)) return Category.Numeric;
        return Category.NonInlinable;
    }
}
