using System;
using System.Collections.Generic;
using System.Text;

namespace EmberTern.Core.Sql.Debugging;

/// <summary>
/// Builds the Evaluation Harness — the anonymous <c>EXECUTE BLOCK</c> that is the <b>only</b> server
/// mechanism (spec §3.2/§3.3). A pure function <see cref="HarnessRequest"/> → <see cref="HarnessResult"/>:
/// zero Avalonia, zero FirebirdSql. It never re-implements Firebird semantics — it assembles the fragment,
/// the injected state and the write-back around the server, which computes everything (evaluation order,
/// types, collations). The §3.4 declaration rules are enforced here:
/// <list type="bullet">
/// <item><b>R1</b> — never assign an injected <c>NULL</c>: only reads with a non-null value become a
/// parameter + an injection assignment (a declared variable is already <c>NULL</c>; assigning <c>NULL</c>
/// into a <c>NOT NULL</c>-domain variable is what crashes real ERP code).</item>
/// <item><b>R2</b> — parameters and <c>RETURNS</c> columns use the variable's <b>base type</b>, never its
/// domain (a domain-typed <c>RETURNS</c> re-validates on <c>SUSPEND</c> and fails on a legitimately-null
/// write-back).</item>
/// <item><b>R3</b> — frame variables are declared <b>verbatim</b> from source (domain / <c>NOT NULL</c> /
/// <c>CHECK</c> / default preserved, so the statement's own assignments keep domain semantics).</item>
/// <item><b>R4</b> — inject only the reads, return only the writes.</item>
/// <item><b>R5</b> — every in-scope sub-routine declaration is carried, verbatim, always (dropping one lets
/// a local <c>F()</c> silently resolve to a global <c>F()</c> — a §F violation).</item>
/// </list>
/// </summary>
public static class HarnessBuilder
{
    // App-generated prefixes, kept deliberately distinctive so they cannot collide with a real ERP variable
    // name (which is why they are not the terse P_/O_ of the spec's illustrative example).
    private const string ParamPrefix = "ET_P_";
    private const string ReturnPrefix = "ET_O_";
    private const string ResultColumnName = "ET_DBG_RESULT";
    private const string Indent = "  ";

    /// <summary>Builds the harness for <paramref name="request"/> (a pure function — the same request always
    /// yields the same SQL, parameters and write-back map).</summary>
    public static HarnessResult Build(HarnessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Mode == HarnessMode.Expression && string.IsNullOrWhiteSpace(request.ExpressionResultType))
        {
            throw new ArgumentException("Expression mode requires an ExpressionResultType.", nameof(request));
        }

        var byName = BuildIndex(request.Variables);

        // R4 + R1: parameters = the reads that have a non-null value to inject (in read order).
        var injected = new List<HarnessVariable>();
        var parameters = new List<object?>();
        foreach (var name in Distinct(request.Reads))
        {
            if (byName.TryGetValue(name, out var v) && v.IsInjectable)
            {
                injected.Add(v);
                parameters.Add(v.Value);
            }
        }

        // R4: RETURNS columns = the writes (in write order) + the evaluated value in Expression mode.
        var writeBacks = new List<HarnessWriteBack>();
        foreach (var name in Distinct(request.Writes))
        {
            if (byName.TryGetValue(name, out var v))
            {
                writeBacks.Add(new HarnessWriteBack(ReturnPrefix + v.Name, v.Name));
            }
        }
        string? resultColumn = request.Mode == HarnessMode.Expression ? ResultColumnName : null;

        var sql = new StringBuilder();

        // Header: EXECUTE BLOCK (params) RETURNS (cols) AS
        sql.Append("EXECUTE BLOCK");
        if (injected.Count > 0)
        {
            sql.Append(" (");
            for (int i = 0; i < injected.Count; i++)
            {
                if (i > 0) sql.Append(", ");
                sql.Append(ParamPrefix).Append(injected[i].Name).Append(' ').Append(injected[i].BaseType).Append(" = ?"); // R2
            }
            sql.Append(')');
        }

        bool hasReturns = writeBacks.Count > 0 || resultColumn is not null;
        if (hasReturns)
        {
            sql.Append('\n').Append("RETURNS (");
            bool first = true;
            if (resultColumn is not null)
            {
                sql.Append(resultColumn).Append(' ').Append(request.ExpressionResultType); // R2 (result base type)
                first = false;
            }
            foreach (var wb in writeBacks)
            {
                if (!first) sql.Append(", ");
                var v = byName[wb.Variable];
                sql.Append(wb.Column).Append(' ').Append(v.BaseType); // R2
                first = false;
            }
            sql.Append(')');
        }
        sql.Append('\n').Append("AS");

        // Declarations: frame variables verbatim (R3), then sub-routines verbatim (R5). Firebird requires
        // variable declarations before local-routine declarations.
        foreach (var v in request.Variables)
        {
            sql.Append('\n').Append(v.Declaration.Trim());
        }
        foreach (var sub in request.SubRoutines)
        {
            sql.Append('\n').Append(sub.Trim());
        }

        // Body: inject the reads (R1 — only the non-null ones), run the fragment, write back, SUSPEND.
        sql.Append('\n').Append("BEGIN");
        foreach (var v in injected)
        {
            sql.Append('\n').Append(Indent).Append(v.Name).Append(" = ").Append(ParamPrefix).Append(v.Name).Append(';');
        }

        sql.Append('\n').Append(Indent);
        if (request.Mode == HarnessMode.Expression)
        {
            sql.Append(resultColumn).Append(" = ").Append(request.Fragment.Trim()).Append(';');
        }
        else
        {
            sql.Append(EnsureTerminated(request.Fragment));
        }

        foreach (var wb in writeBacks)
        {
            sql.Append('\n').Append(Indent).Append(wb.Column).Append(" = ").Append(wb.Variable).Append(';');
        }
        if (hasReturns)
        {
            sql.Append('\n').Append(Indent).Append("SUSPEND;");
        }
        sql.Append('\n').Append("END");

        return new HarnessResult(sql.ToString(), parameters, writeBacks, resultColumn);
    }

    private static Dictionary<string, HarnessVariable> BuildIndex(IReadOnlyList<HarnessVariable> variables)
    {
        var byName = new Dictionary<string, HarnessVariable>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in variables)
        {
            byName[v.Name] = v; // last-wins; a well-formed frame has distinct names
        }
        return byName;
    }

    // Preserves first-seen order, drops duplicates (case-insensitive) — read/write sets may repeat a name.
    private static IEnumerable<string> Distinct(IReadOnlyList<string> names)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in names)
        {
            if (seen.Add(n)) yield return n;
        }
    }

    // A verbatim statement fragment ends with its own ';' (leaf spans include the terminator); a mid-edit
    // fragment without one gets a ';' so the harness body stays well-formed.
    private static string EnsureTerminated(string fragment)
    {
        var t = fragment.Trim();
        return t.EndsWith(';') ? t : t + ";";
    }
}
