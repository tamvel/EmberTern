using System;
using System.Collections.Generic;
using System.Text;

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
/// timestamp → single-quoted (internal <c>'</c> doubled). BLOB/array/unknown are NOT inlined —
/// the <c>?</c> is left in place (the value stays visible in the detail Parameters list).</item>
/// </list>
/// </para>
/// </summary>
public static class TraceSqlInliner
{
    /// <summary>Returns <paramref name="sql"/> with each top-level <c>?</c> replaced by the
    /// formatted value of the corresponding <paramref name="parameters"/> entry, or the SQL
    /// unchanged when there are no parameters or the placeholder count doesn't match.</summary>
    public static string Inline(string? sql, IReadOnlyList<RawTraceParam>? parameters)
    {
        if (string.IsNullOrEmpty(sql)) return sql ?? string.Empty;
        if (parameters is null || parameters.Count == 0) return sql!;
        if (CountPlaceholders(sql!) != parameters.Count) return sql!; // mismatch → faithful source

        var sb = new StringBuilder(sql!.Length + 32);
        int i = 0, p = 0;
        while (i < sql.Length)
        {
            char c = sql[i];
            if (c == '\'' || c == '"')
            {
                int start = i;
                SkipQuoted(sql, ref i, c);
                sb.Append(sql, start, i - start);           // copy the literal verbatim
            }
            else if (IsLineCommentStart(sql, i))
            {
                int start = i;
                while (i < sql.Length && sql[i] != '\n') i++;
                sb.Append(sql, start, i - start);
            }
            else if (IsBlockCommentStart(sql, i))
            {
                int start = i;
                SkipBlockComment(sql, ref i);
                sb.Append(sql, start, i - start);
            }
            else if (c == '?')
            {
                sb.Append(Format(parameters[p++]));
                i++;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }
        return sb.ToString();
    }

    private static int CountPlaceholders(string sql)
    {
        int i = 0, n = 0;
        while (i < sql.Length)
        {
            char c = sql[i];
            if (c == '\'' || c == '"') SkipQuoted(sql, ref i, c);
            else if (IsLineCommentStart(sql, i)) { while (i < sql.Length && sql[i] != '\n') i++; }
            else if (IsBlockCommentStart(sql, i)) SkipBlockComment(sql, ref i);
            else { if (c == '?') n++; i++; }
        }
        return n;
    }

    private static bool IsLineCommentStart(string s, int i)
        => s[i] == '-' && i + 1 < s.Length && s[i + 1] == '-';

    private static bool IsBlockCommentStart(string s, int i)
        => s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*';

    private static void SkipBlockComment(string s, ref int i)
    {
        i += 2;
        while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
        i = i + 1 < s.Length ? i + 2 : s.Length;
    }

    private static void SkipQuoted(string s, ref int i, char q)
    {
        i++; // opening quote
        while (i < s.Length)
        {
            if (s[i] == q)
            {
                if (i + 1 < s.Length && s[i + 1] == q) { i += 2; continue; } // doubled escape
                i++;
                return;
            }
            i++;
        }
    }

    private enum Category { Numeric, Text, Temporal, Boolean, NonInlinable }

    private static string Format(RawTraceParam param)
    {
        if (param.Value is null) return "NULL";
        return Classify(param.DataType) switch
        {
            Category.Numeric => param.Value.Trim(),
            Category.Boolean => param.Value.Trim(),
            Category.Text or Category.Temporal => "'" + param.Value.Replace("'", "''") + "'",
            _ => "?", // BLOB / array / unknown — keep the placeholder; the value stays in the Parameters list
        };
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
