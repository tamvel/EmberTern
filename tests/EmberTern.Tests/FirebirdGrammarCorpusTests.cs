using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// ⭐⭐ <b>The Firebird grammar conformance guard</b> (2026-08-07) — the permanent answer to a defect class
/// that had been patched four times, one reported syntax at a time.
/// <para>
/// <b>The class of defect.</b> Firebird has very few reserved words. Most of its vocabulary —
/// <c>MONTH</c>, <c>PLACING</c>, <c>UNBOUNDED</c>, <c>AUTONOMOUS</c>, … — is <em>non-reserved</em>, which is
/// deliberate: a user may legitimately name a column or a variable <c>MONTH</c>. Those words therefore lex
/// as ordinary IDENTIFIERS, and the binder's expression walkers, seeing an identifier in a value position,
/// read them as a variable (PSQL) or a column (query). The visible result was a squiggle — ET0003 on
/// <c>EXTRACT(YEAR FROM …)</c>, on <c>DATEADD(MONTH, …)</c>, on <c>OVERLAY(… PLACING …)</c> — on code the
/// engine compiles without complaint.
/// </para>
/// <para>
/// ⛔ <b>Why the previous fixes could not converge.</b> Each added a POSITIONAL predicate for the one
/// construct reported (<c>NEXT VALUE FOR</c>, then <c>GEN_ID</c>'s first argument, then <c>EXTRACT</c>'s
/// first argument). A positional predicate is precise, and for a name the catalog must RESOLVE — a
/// generator — it is the only correct tool. But as a strategy for the whole vocabulary it is an
/// allowlist of exceptions, so its completeness is bounded by the bug reports that produced it: every
/// construct nobody had used yet was still a false positive in waiting.
/// </para>
/// <para>
/// ⭐ <b>What replaced it</b> is stated in <see cref="FirebirdSyntax.IsNonReservedWord"/>: an identifier that
/// spells one of Firebird's own non-reserved words and resolves to nothing is not PROVABLY an unknown
/// variable, so the conservatism rule says stay silent. That is a rule about the vocabulary, not about a
/// list of constructs — which is why this corpus walks the Language Reference chapter by chapter instead of
/// collecting reports.
/// </para>
/// <para>
/// ⚠ <b>Two halves, and the second is the one that is easy to forget.</b> ET0003 comes from the PSQL
/// expression walker; the QUERY walker has the same blind spot with a different symptom — a Firebird word
/// that happens to match a column on one in-scope table binds SILENTLY as that column (wrong colour, wrong
/// Quick Info), and on two tables it reports ET0005 <i>Ambiguous column</i>. <see cref="NoWordIsBoundAsAColumn"/>
/// pins that half against a snapshot seeded to collide on purpose.
/// </para>
/// </summary>
public class FirebirdGrammarCorpusTests
{
    // ── A metadata snapshot built to make a mis-binding VISIBLE ───────────────────────────────
    //
    // ⚠ The point of the column list is the COLLISIONS. Two tables both carry MONTH / YEAR / DAY /
    // PLACING / UNBOUNDED, so a walker that reads one of those words as a column cannot do it quietly:
    // with two candidates the binder records an unresolved Column reference and the engine reports
    // ET0005. A snapshot without the collision would let the same defect pass as a silent mis-colour.
    private sealed class Snapshot : ISqlMetadataProvider
    {
        private readonly Dictionary<string, ObjectMetadata> _objects = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ColumnMetadata>> _cols = new(StringComparer.OrdinalIgnoreCase);

        public Snapshot Object(string name, SymbolKind kind = SymbolKind.Table)
        {
            _objects[name] = new ObjectMetadata(name, kind);
            return this;
        }

        public Snapshot Table(string name, params string[] columns)
        {
            Object(name);
            _cols[name] = columns.Select(c => new ColumnMetadata(c, "INTEGER")).ToList();
            return this;
        }

        public bool KnowsColumns(string tableOrView) => _cols.ContainsKey(tableOrView);
        public ObjectMetadata? FindObject(string name) => _objects.TryGetValue(name, out var o) ? o : null;
        public IReadOnlyList<ColumnMetadata> GetColumns(string t)
            => _cols.TryGetValue(t, out var c) ? c : Array.Empty<ColumnMetadata>();
        public IReadOnlyList<RoutineParameterMetadata> GetRoutineParameters(string r)
            => Array.Empty<RoutineParameterMetadata>();
        public IReadOnlyList<ObjectMetadata> AllObjects() => _objects.Values.ToList();
    }

    // Every word in this list is a legal Firebird column name AND a syntax word somewhere in the corpus.
    private static readonly string[] CollidingColumns =
    {
        "MONTH", "YEAR", "DAY", "WEEK", "HOUR", "MINUTE", "SECOND", "QUARTER",
        "PLACING", "UNBOUNDED", "PRECEDING", "FOLLOWING", "AUTONOMOUS", "PRIVILEGES",
        "OF", "MODE", "IV", "LOCAL", "ZONE", "TIES", "OTHERS", "LATERAL", "IDENTITY",
    };

    private static Snapshot CollidingSnapshot() => new Snapshot()
        .Table("ORDERS", CollidingColumns.Concat(new[] { "ID", "A", "D", "K", "AMOUNT", "SUB", "S", "P" }).ToArray())
        .Table("SALES", CollidingColumns.Concat(new[] { "ID", "A", "D", "K", "AMOUNT" }).ToArray());

    // ⚠ The corpus's own placeholder operands (v, d, s, …) are DECLARED, through the same ambient-symbol
    // seam the Easy-mode routine editors use for parameters that live outside the text. Without it the
    // guard would spend most of its assertions reporting the fixture's undeclared variables — a finding
    // about the corpus, not about Firebird — and the real signal would be buried in it (measured: 47 of
    // the first run's 63 findings were exactly that).
    private static readonly IReadOnlyList<Symbol> AmbientOperands =
        new[] { "v", "d", "t", "s", "r", "a", "b", "i", "x", "y", "z", "k", "n", "p", "sub", "msg", "e" }
            .Select(nm => (Symbol)new VariableSymbol(nm.ToUpperInvariant()))
            .ToList();

    // ══ Half one: no false ET0003 anywhere in the Language-Reference corpus ═══════════════════
    //
    // ⚠ Run WITHOUT metadata on purpose. The local-scope categories (unresolved variable / parameter,
    // unknown cursor, SUSPEND context, INSERT count) need no snapshot, while the metadata-gated ones
    // would otherwise report every ad-hoc table name in the corpus as ET0001 — a finding about the
    // fixture, not about the grammar. The colliding-snapshot half below covers the other categories
    // against tables that actually exist.
    [Theory]
    [MemberData(nameof(SqlTestCorpus.LanguageReferenceData), MemberType = typeof(SqlTestCorpus))]
    public void NoFirebirdConstruct_ProducesALocalScopeDiagnostic(string sql)
    {
        var found = DiagnosticsEngine.Analyze(SemanticModel.Build(sql, metadata: null, AmbientOperands));

        Assert.True(
            found.Count == 0,
            $"Firebird's own grammar produced {found.Count} diagnostic(s):\n  {sql}\n" +
            string.Join("\n", found.Select(d =>
                $"    {d.Code} @{d.Start}+{d.Length} \"{Excerpt(sql, d)}\" — {d.Message}")));
    }

    // ══ Half two: a Firebird syntax word is never claimed as a COLUMN ═════════════════════════
    //
    // The query walker's version of the same blind spot. Every case here puts a non-reserved Firebird
    // word in a syntactic position while TWO in-scope tables carry a column of that name, so a walker
    // that treats it as an expression cannot fail quietly.
    [Theory]
    [InlineData("select dateadd(month, 1, o.d) from orders o join sales s on s.id = o.id")]
    [InlineData("select extract(year from o.d) from orders o join sales s on s.id = o.id")]
    [InlineData("select datediff(day from o.d to s.d) from orders o join sales s on s.id = o.id")]
    [InlineData("select first_day(of month from o.d) from orders o join sales s on s.id = o.id")]
    [InlineData("select overlay(o.a placing s.a from 1) from orders o join sales s on s.id = o.id")]
    [InlineData("select sum(o.a) over (order by o.d rows between unbounded preceding and current row) " +
                "from orders o join sales s on s.id = o.id")]
    [InlineData("select sum(o.a) over (order by o.d range between 1 preceding and 1 following) " +
                "from orders o join sales s on s.id = o.id")]
    public void NoWordIsBoundAsAColumn(string sql)
    {
        var model = SemanticModel.Build(sql, CollidingSnapshot());

        var claimed = model.References
            .Where(r => r.Role == ReferenceRole.Column && FirebirdSyntax.IsNonReservedWord(r.Text))
            .Select(r => r.Text)
            .ToList();

        Assert.True(claimed.Count == 0,
            $"Firebird syntax word(s) bound as a column: {string.Join(", ", claimed)}\n  {sql}");

        var found = DiagnosticsEngine.Analyze(model);
        Assert.True(found.Count == 0,
            $"{found.Count} diagnostic(s) on valid Firebird:\n  {sql}\n" +
            string.Join("\n", found.Select(d => $"    {d.Code} \"{Excerpt(sql, d)}\" — {d.Message}")));
    }

    // ══ The guard's own premise ═══════════════════════════════════════════════════════════════

    // ⭐ A DECLARED variable whose name happens to be a Firebird word still resolves and still binds —
    // the non-reserved set only ever suppresses a finding about a name that resolved to NOTHING. Without
    // this the "stay silent" rule could be implemented as "ignore the word", which would take the
    // colour, the Quick Info and the find-references of a legitimately-named variable with it.
    [Fact]
    public void ADeclaredVariableNamedLikeAFirebirdWord_StillBinds()
    {
        const string sql = "create procedure p as declare variable month integer; begin month = 1; end";
        var model = SemanticModel.Build(sql);

        var uses = model.References
            .Where(r => string.Equals(r.Text, "month", StringComparison.OrdinalIgnoreCase) && !r.IsDefinition)
            .ToList();

        Assert.NotEmpty(uses);
        Assert.All(uses, r => Assert.True(r.IsResolved, "a declared variable named MONTH must still resolve"));
        Assert.Empty(DiagnosticsEngine.Analyze(model));
    }

    // ⚠ The set must not silence a genuine typo that is NOT Firebird vocabulary — the guard above only
    // has value if this one still fires. (The pair is the whole contract: silence on Firebird's words,
    // a finding on everything else.)
    [Fact]
    public void AGenuineUnknownVariable_IsStillFlagged()
    {
        var found = DiagnosticsEngine.Analyze(SemanticModel.Build(
            "create procedure p as begin v_total = v_amonut + 1; end"));

        Assert.Contains(found, d => d.Code == "ET0003" &&
                                    d.Message.Contains("v_amonut", StringComparison.OrdinalIgnoreCase));
    }

    private static string Excerpt(string sql, Diagnostic d)
    {
        int start = Math.Max(0, Math.Min(d.Start, sql.Length));
        int len = Math.Max(0, Math.Min(d.Length, sql.Length - start));
        return sql.Substring(start, len);
    }
}
