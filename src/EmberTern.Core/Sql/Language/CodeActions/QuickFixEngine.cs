using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Core.Sql.Language.CodeActions;

/// <summary>
/// The <b>diagnostic-driven</b> producer of <see cref="CodeAction"/>s — Quick Fixes (Stage Q, design
/// <see href="../../docs/design/editor-quick-fixes.md">editor-quick-fixes.md</see>).
/// <para>
/// <b>One source of truth.</b> This engine cannot decide that something is wrong: it is handed a
/// <see cref="Diagnostic"/> the <see cref="DiagnosticsEngine"/> already produced and can only answer
/// "how would I repair THIS". Diagnostics and fixes therefore cannot disagree — there is nothing for
/// them to disagree about.
/// </para>
/// <para>
/// <b>On demand, never stored.</b> Fixes are computed when the user asks (light bulb / Ctrl+.), not on
/// the background pass, and deliberately do NOT hang off <see cref="Diagnostic"/> — that is a record
/// struct whose VALUE equality the Diagnostics panel relies on to skip a rebuild, and a list member
/// would degrade it to reference equality (design §2.1).
/// </para>
/// <para>
/// <b>Conservative, like the engine it serves.</b> A fix edits the user's code, so the bar is higher
/// than for a diagnostic: unless a repair can be named EXACTLY — this span, that replacement, derived
/// from the model rather than from a text scan — nothing is offered. Returning empty is always a valid
/// answer (Architecture rule #11).
/// </para>
/// <para>Pure Core: zero Avalonia, deterministic, unit-testable offline. It performs no parsing and no
/// analysis; it reads the model the caller already has.</para>
/// </summary>
public static class QuickFixEngine
{
    /// <summary>
    /// The fixes available for <paramref name="diagnostic"/>, in a stable order, or an empty list when
    /// none can be named exactly. Never throws on an incomplete/stale model — it returns nothing.
    /// </summary>
    /// <param name="model">The cached semantic model the diagnostic was produced from.</param>
    /// <param name="diagnostic">The finding to repair.</param>
    public static IReadOnlyList<CodeAction> GetFixes(SemanticModel model, Diagnostic diagnostic)
    {
        if (model is null) return Array.Empty<CodeAction>();

        // Dispatch on the category — NOT on the code string, which is a display/filter anchor. Adding a
        // fix is one producer method plus one arm here (design §10); nothing outside this file changes.
        return diagnostic.Category switch
        {
            DiagnosticCategory.AmbiguousColumn => QualifyAmbiguousColumn(model, diagnostic),
            _ => Array.Empty<CodeAction>(),
        };
    }

    // ── ET0005 AmbiguousColumn → "Qualify as '<alias>.<col>'" ─────────────────────────────────
    //
    // The binder records a bare unresolved column ONLY when the name matched a column on ≥2 in-scope
    // tables, so the repair is exactly "say which one you meant". One action per candidate; the user
    // picks the meaning — EmberTern never guesses which table was intended.
    private static IReadOnlyList<CodeAction> QualifyAmbiguousColumn(SemanticModel model, Diagnostic diagnostic)
    {
        // The reference gives the column name AS THE USER WROTE IT, which is both the text to preserve
        // in the qualified form and the drift guard. Matched by exact span: the diagnostic was built
        // from this reference, so anything else means the model is not the one that produced it.
        var reference = FindReferenceAt(model, diagnostic.Start, diagnostic.Length);
        if (reference is null) return Array.Empty<CodeAction>();

        var columnText = reference.Text;
        if (string.IsNullOrEmpty(columnText)) return Array.Empty<CodeAction>();

        var actions = new List<CodeAction>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in TablesInScope(model, diagnostic.Start))
        {
            if (!ExposesColumn(model, table, columnText)) continue;

            var qualifier = QualifierTextFor(model, table);
            if (qualifier.Length == 0) continue;
            if (!seen.Add(qualifier)) continue; // the same alias twice cannot happen, but never offer a duplicate

            var qualified = qualifier + "." + columnText;
            actions.Add(new CodeAction(
                string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    Resources.QualifyColumnTitleFormat, qualified),
                new[] { new TextEdit(diagnostic.Start, diagnostic.Length, qualified, columnText) }));
        }

        return actions;
    }

    // The FROM/JOIN entries visible at the offset, in declaration order so the menu is deterministic
    // (a symbol with no declaration span sorts last, stably).
    private static List<TableReferenceSymbol> TablesInScope(SemanticModel model, int offset)
    {
        var tables = new List<TableReferenceSymbol>();
        foreach (var symbol in model.SymbolsInScope(offset))
        {
            if (symbol is TableReferenceSymbol t) tables.Add(t);
        }
        tables.Sort(static (a, b) =>
            (a.DeclarationSpan?.Start ?? int.MaxValue).CompareTo(b.DeclarationSpan?.Start ?? int.MaxValue));
        return tables;
    }

    // Does this FROM entry certainly expose that column? "Certainly" is the whole point: a derived table
    // (an aliased subquery) and a CTE whose projection could not be enumerated are NOT verifiable, so
    // they are skipped rather than offered on a guess — the same rule that keeps DiagnosticsEngine
    // silent on an incomplete CTE (§0).
    private static bool ExposesColumn(SemanticModel model, TableReferenceSymbol table, string column)
    {
        if (table.IsDerived) return false;

        if (table.Target is CteSymbol cte)
        {
            if (!cte.ColumnsComplete) return false;
            foreach (var c in cte.OutputColumns)
            {
                if (string.Equals(c, column, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        if (string.IsNullOrEmpty(table.TargetName)) return false;
        foreach (var c in model.Metadata.GetColumns(table.TargetName!))
        {
            if (string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // The qualifier to insert, preferring the casing the user actually typed: Symbol.Name is folded, so
    // an alias written `k` would come back `K` and the fix would impose a casing the user did not choose.
    // The binder records every identifier occurrence, so the symbol's own first reference carries the
    // source text; the folded name is the fallback when no occurrence was recorded (semantically
    // identical — Firebird folds unquoted identifiers).
    private static string QualifierTextFor(SemanticModel model, TableReferenceSymbol table)
    {
        foreach (var r in model.ReferencesTo(table))
        {
            if (!string.IsNullOrEmpty(r.Text)) return r.Text;
        }
        return table.Name ?? string.Empty;
    }

    // The reference the diagnostic was built from — matched on the EXACT span, so a model that has moved
    // on (a stale diagnostic against a newer model) yields nothing instead of a misplaced edit.
    private static SymbolReference? FindReferenceAt(SemanticModel model, int start, int length)
    {
        foreach (var r in model.References)
        {
            if (r.Span.Start == start && r.Span.Length == length) return r;
        }
        return null;
    }

    // Fix titles. Core has no UiStrings (that is an App type), and these are user-visible, so they live
    // beside the producer that owns them — the same reasoning that keeps Firebird's connection-failure
    // messages in the Firebird layer.
    internal static class Resources
    {
        public const string QualifyColumnTitleFormat = "Qualify as '{0}'";
    }
}
