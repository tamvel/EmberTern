using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Language.Semantics;

/// <summary>
/// Builds a <see cref="SemanticModel"/> from a parsed <see cref="SqlScript"/> and a metadata
/// snapshot — the <b>binder</b>, Etap 4 of the editor rebuild. It is the sole implementation detail
/// behind the model's stable public API (§5.5): it walks each statement's <em>token stream</em>
/// (the Etap-2 "statement skeleton" keeps interiors as tokens, not deep AST nodes), builds the
/// nested <see cref="Scope"/> tree, declares <see cref="Symbol"/>s, and records
/// <see cref="SymbolReference"/>s. When the parser deepens in a future etap this class swaps to
/// walking the deeper tree — no public type or consumer changes.
/// <para>
/// Error-tolerant by construction (mirrors the parser): it never throws on incomplete/invalid
/// input, binds as much as it can understand, and leaves the rest unresolved. Metadata-optional:
/// with <see cref="EmptyMetadataProvider"/> it still binds every local scope and records references;
/// schema symbols simply stay unresolved.
/// </para>
/// </summary>
internal sealed partial class SemanticBinder
{
    private readonly SqlScript _script;
    private readonly ISqlMetadataProvider _metadata;
    private readonly Scope _root;
    private readonly List<Symbol> _symbols = new();
    private readonly List<SymbolReference> _references = new();

    private SemanticBinder(SqlScript script, ISqlMetadataProvider metadata)
    {
        _script = script;
        _metadata = metadata;
        var span = script.Statements.Count > 0
            ? TextSpan.FromBounds(0, script.Text.Length)
            : new TextSpan(0, script.Text.Length);
        _root = new Scope(ScopeKind.Script, parent: null, span, statement: null);
    }

    public static SemanticModel Bind(SqlScript script, ISqlMetadataProvider metadata)
        => Bind(script, metadata, ambientSymbols: null);

    /// <summary>
    /// Binds <paramref name="script"/>, first seeding the root scope with
    /// <paramref name="ambientSymbols"/> — declarations that are real but live OUTSIDE this text.
    /// <para>The Easy-mode routine editors are the reason this exists: their editor holds only the
    /// BODY (the text after <c>AS</c>), while the routine's parameters and DECLAREd variables live
    /// in the surrounding grids. A text-only model therefore cannot see them, so Ctrl+Space offered
    /// no parameters or locals. Seeding them into the root scope makes them visible to EVERY client
    /// of the model (completion, Quick Info, navigation, highlighting) at once, with no offset
    /// translation and no second code path. A real declaration in the text SHADOWS an ambient one
    /// of the same name, because inner scopes are searched first.</para>
    /// </summary>
    public static SemanticModel Bind(
        SqlScript script, ISqlMetadataProvider metadata, IReadOnlyList<Symbol>? ambientSymbols)
    {
        var binder = new SemanticBinder(script, metadata);
        if (ambientSymbols is { Count: > 0 })
        {
            foreach (var sym in ambientSymbols)
            {
                if (sym is null) continue;
                binder._root.Declare(sym);
                binder._symbols.Add(sym);
            }
        }
        binder.Run();
        return new SemanticModel(script, metadata, binder._root, binder._symbols, binder._references);
    }

    private void Run()
    {
        foreach (var stmt in _script.Statements)
        {
            try
            {
                BindStatement(stmt);
            }
            catch
            {
                // The binder must never fail the whole model on one pathological statement; a
                // statement it cannot handle simply contributes no symbols/references. (§4.2 #1.)
            }
        }

        try
        {
            BindGlobalCatalogReferences();
        }
        catch
        {
            // Same error-tolerance contract — a bad token stream must never break the model.
        }
    }

    // Records the catalog references the structural (scope-based) binders don't cover: a FUNCTION or
    // stored-procedure CALL (<c>NAME(…)</c>) in any expression, and a GENERATOR NAME
    // (<c>NEXT VALUE FOR &lt;sequence&gt;</c> / <c>GEN_ID(&lt;sequence&gt;, …)</c>). These appear in every
    // statement kind — SELECT lists, DML expressions, PSQL bodies, and bare expression statements
    // (<c>SELECT F(:x) FROM RDB$DATABASE</c>, a standalone <c>NEXT VALUE FOR G</c>) — so ONE flat token scan
    // across all statements covers them uniformly, resolving each name against the metadata snapshot.
    //
    // A CALL is recorded only when the name is a KNOWN catalog object, so a built-in
    // (<c>MAX</c>/<c>COALESCE</c>/<c>SUBSTRING</c>/…) the catalog doesn't carry stays uncoloured — the
    // same high-precision "never guess" rule the rest of the binder follows. A GENERATOR NAME is the one
    // deliberate exception, and it does not weaken that rule: there the grammar — not a guess — fixes what
    // the identifier means, so an unknown one is recorded UNRESOLVED (it is provably a missing object, and
    // dropping the reference would lose the finding rather than be conservative about it).
    //
    // A token a structural binder already referenced (e.g. a selectable procedure in <c>FROM</c>) is
    // skipped, so no occurrence is double-recorded — which also means a binder that claims an occurrence
    // this scan owns SILENTLY WINS it (see IsGeneratorNamePosition). Read-only; every reference is a plain
    // occurrence, never a definition.
    private void BindGlobalCatalogReferences()
    {
        var referenced = new HashSet<int>();
        foreach (var r in _references) referenced.Add(r.Span.Start);

        foreach (var stmt in _script.Statements)
        {
            var t = stmt.Tokens;
            for (int i = 0; i < t.Count; i++)
            {
                var tok = t[i];

                // A GENERATOR NAME — the operand of NEXT VALUE FOR, or GEN_ID's first argument. The
                // grammar admits nothing else there (IsGeneratorNamePosition), so the occurrence is
                // recorded as a SCHEMA OBJECT reference either way: bound to the sequence when the catalog
                // knows it, and deliberately UNRESOLVED when it does not — a mistyped generator then reads
                // as an unknown OBJECT (ET0001, itself metadata-gated) instead of disappearing. GEN_ID and
                // NEXT VALUE FOR are built-in syntax the catalog doesn't carry, so they stay uncoloured.
                if (IsNameToken(tok) && IsGeneratorNamePosition(t, i))
                {
                    if (referenced.Add(tok.Start))
                    {
                        var seq = ResolveObject(FoldedName(tok)) is { Kind: SymbolKind.Sequence } s ? s : null;
                        AddReference(tok, seq, ReferenceRole.SchemaObject);
                    }
                    continue;
                }

                // NAME '(' … — a function / selectable-procedure call. The name must be a bare identifier
                // (not dot-qualified, so a package member isn't mis-resolved to a same-named standalone)
                // and resolve to a known Function or Procedure.
                if (IsNameToken(tok) && At(t, i + 1).Kind == TokenKind.LParen
                    && !(i - 1 >= 0 && t[i - 1].Kind == TokenKind.Dot)
                    && !referenced.Contains(tok.Start)
                    && ResolveObject(FoldedName(tok)) is { Kind: SymbolKind.Function or SymbolKind.Procedure } routine)
                {
                    AddReference(tok, routine, ReferenceRole.SchemaObject);
                    referenced.Add(tok.Start);
                }
            }
        }
    }

    /// <summary>A word token (identifier or keyword) whose text equals <paramref name="text"/>
    /// (case-insensitive) — for matching multi-word constructs whose words may lex either way.</summary>
    private static bool IsWordText(SqlToken t, string text)
        => t.Kind is TokenKind.Identifier or TokenKind.Keyword
           && string.Equals(t.Text, text, StringComparison.OrdinalIgnoreCase);

    // Does Firebird's grammar say the name token at <paramref name="k"/> is a GENERATOR (sequence) NAME
    // rather than an ordinary expression? True in exactly two positions: the operand of
    // <c>NEXT VALUE FOR</c>, and the FIRST argument of <c>GEN_ID(…)</c>.
    //
    // Those two are the whole list, measured on FB5 (2026-08-01) rather than assumed: GEN_ID takes a bare
    // identifier (`GEN_ID(GEN_ORDER_ID, 0)` → 999), while MAKE_DBKEY's first argument is an ordinary
    // expression — a bare name there is rejected by the engine with "-206 Column unknown" — and
    // RDB$GET_CONTEXT / RDB$SET_CONTEXT take string literals, which never lex as identifiers. So no other
    // built-in can put an object name where a column or variable would otherwise be read.
    //
    // ONE owner for that question. It is asked by two binders with OPPOSITE jobs — the global catalog scan
    // RESOLVES the name, while the PSQL expression walker must leave the occurrence unclaimed instead of
    // treating it as a local variable — and a partial second copy of it (a bare "is the previous token FOR"
    // test, which covered NEXT VALUE FOR but not GEN_ID) is exactly how GEN_ID's argument came to be
    // reported as an unresolved variable.
    private static bool IsGeneratorNamePosition(IReadOnlyList<SqlToken> t, int k)
    {
        // NEXT VALUE FOR <name> — each word may lex as a keyword or an identifier, so match by text.
        if (k >= 3 && IsWordText(t[k - 3], "NEXT") && IsWordText(t[k - 2], "VALUE") && IsWordText(t[k - 1], "FOR"))
        {
            return true;
        }

        // GEN_ID( <name> , … ) — the first argument only; every later one is an ordinary expression.
        return k >= 2 && t[k - 1].Kind == TokenKind.LParen && IsWordText(t[k - 2], "GEN_ID");
    }

    private void BindStatement(SqlStatement stmt)
    {
        switch (stmt)
        {
            case SelectStatement sel:
                // The parser's QueryNode is the structural source (Etap 6.9 convergence). Null only for a
                // malformed WITH the parser couldn't model — then bind the tokens as a flat expression.
                if (sel.Query is not null) BindQueryNode(sel.Query, _root, sel);
                else BindQueryFallback(sel.Tokens, _root, sel);
                break;

            case InsertStatement:
            case UpdateStatement:
            case UpdateOrInsertStatement:
            case DeleteStatement:
            case MergeStatement:
                BindDml(stmt);
                break;

            case DdlStatement ddl:
                BindDdl(ddl);
                break;

            case ExecuteBlockStatement eb:
                BindExecuteBlock(eb);
                break;

            case AnonymousBlockStatement ab:
                BindAnonymousBlock(ab);
                break;

            case ExecuteProcedureStatement exec:
                BindExecuteProcedure(exec);
                break;

            // COMMENT / SET / GRANT / REVOKE / DECLARE(external) / EXECUTE STATEMENT / Raw / Empty:
            // no scope or local declarations to bind in Etap 4. (Schema-object references inside
            // GRANT/COMMENT are a later-etap enrichment; the model stays error-tolerant meanwhile.)
            default:
                break;
        }
    }

    // ── Shared symbol/reference plumbing ─────────────────────────────────────────────────────

    private void AddSymbol(Symbol s) => _symbols.Add(s);

    private void AddReference(SymbolReference r) => _references.Add(r);

    private void AddReference(SqlToken token, Symbol? symbol, ReferenceRole role, bool isDefinition = false)
        => _references.Add(new SymbolReference(TextSpan.Of(token), token.Text, symbol, role, isDefinition));

    /// <summary>Resolves an object name against the metadata snapshot, returning a
    /// <see cref="SchemaObjectSymbol"/> when known (catalog-cased name + rich facts), else
    /// <c>null</c>. Cached per name so repeated references to the same object share one symbol
    /// (so <see cref="SemanticModel.ReferencesTo"/> groups them).</summary>
    private readonly Dictionary<string, SchemaObjectSymbol?> _objectCache =
        new(StringComparer.OrdinalIgnoreCase);

    private SchemaObjectSymbol? ResolveObject(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (_objectCache.TryGetValue(name!, out var cached)) return cached;

        var meta = _metadata.FindObject(name!);
        SchemaObjectSymbol? sym = meta is null
            ? null
            : new SchemaObjectSymbol(meta.Kind, meta.Name)
            {
                Description = meta.Description,
                Owner = meta.Owner,
            };
        if (sym is not null) AddSymbol(sym);
        _objectCache[name!] = sym;
        return sym;
    }

    // ── Token / name helpers (shared by all binders) ─────────────────────────────────────────

    private static readonly SqlToken NoToken =
        new(TokenKind.EndOfFile, 0, 0, string.Empty, Array.Empty<SqlTrivia>());

    private static SqlToken At(IReadOnlyList<SqlToken> t, int i)
        => i >= 0 && i < t.Count ? t[i] : NoToken;

    /// <summary>A word-like token (unquoted identifier, quoted identifier, or catalogued keyword).</summary>
    private static bool IsWord(SqlToken t)
        => t.Kind is TokenKind.Identifier or TokenKind.QuotedIdentifier or TokenKind.Keyword;

    /// <summary>An identifier (name) token — excludes keywords, so it won't misread a clause word.
    /// A quoted identifier always qualifies.</summary>
    private static bool IsNameToken(SqlToken t)
        => t.Kind is TokenKind.Identifier or TokenKind.QuotedIdentifier;

    private static bool IsKeyword(SqlToken t, string keyword)
        => t.Kind == TokenKind.Keyword && string.Equals(t.Text, keyword, StringComparison.OrdinalIgnoreCase);

    /// <summary>The folded name a word token denotes: an unquoted identifier/keyword upper-cased
    /// (Firebird's catalog convention), a quoted identifier decoded in its literal case. <c>null</c>
    /// when the token is not word-like.</summary>
    private static string? FoldedName(SqlToken t) => t.Kind switch
    {
        TokenKind.QuotedIdentifier => t.Value,
        TokenKind.Identifier or TokenKind.Keyword => t.Text.ToUpperInvariant(),
        _ => null,
    };

    /// <summary>With <paramref name="i"/> on an <c>(</c> token, returns the index just past the
    /// matching <c>)</c> (nesting-aware); or <paramref name="hi"/> when unbalanced.</summary>
    private static int SkipParens(IReadOnlyList<SqlToken> t, int i, int hi)
    {
        int depth = 0;
        while (i < hi)
        {
            var k = t[i].Kind;
            if (k == TokenKind.LParen) depth++;
            else if (k == TokenKind.RParen) { depth--; i++; if (depth == 0) return i; continue; }
            i++;
        }
        return hi;
    }
}
