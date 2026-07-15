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
    // stored-procedure CALL (<c>NAME(…)</c>) in any expression, and <c>NEXT VALUE FOR &lt;sequence&gt;</c>.
    // These appear in every statement kind — SELECT lists, DML expressions, PSQL bodies, and bare
    // expression statements (<c>SELECT F(:x) FROM RDB$DATABASE</c>, a standalone <c>NEXT VALUE FOR G</c>)
    // — so ONE flat token scan across all statements covers them uniformly, resolving each name against
    // the metadata snapshot: only a KNOWN catalog object gets a reference, so a built-in
    // (<c>MAX</c>/<c>COALESCE</c>/<c>SUBSTRING</c>/…) the catalog doesn't carry stays uncoloured — the
    // same high-precision "never guess" rule the rest of the binder follows. A token a structural binder
    // already referenced (e.g. a selectable procedure in <c>FROM</c>) is skipped, so no occurrence is
    // double-recorded. Read-only; every reference is a plain occurrence, never a definition.
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

                // NEXT VALUE FOR <sequence>. NEXT/VALUE may lex as identifiers or keywords, so match by
                // text; the sequence name resolves only when the catalog knows it as a generator.
                if (IsWordText(tok, "NEXT") && IsWordText(At(t, i + 1), "VALUE") && IsWordText(At(t, i + 2), "FOR")
                    && IsNameToken(At(t, i + 3)))
                {
                    var seqTok = t[i + 3];
                    if (referenced.Add(seqTok.Start)
                        && ResolveObject(FoldedName(seqTok)) is { Kind: SymbolKind.Sequence } seq)
                    {
                        AddReference(seqTok, seq, ReferenceRole.SchemaObject);
                    }
                    i += 3;
                    continue;
                }

                // GEN_ID(<sequence>, <increment>) — the FIRST argument is a generator name, exactly like
                // NEXT VALUE FOR. Resolved through the SAME path (ObjectMetadata → QuickInfoEngine); there
                // is no GEN_ID special case beyond spotting the argument position. GEN_ID itself is a
                // built-in the catalog doesn't carry, so it stays uncoloured.
                if (IsWordText(tok, "GEN_ID") && At(t, i + 1).Kind == TokenKind.LParen && IsNameToken(At(t, i + 2)))
                {
                    var genTok = t[i + 2];
                    if (referenced.Add(genTok.Start)
                        && ResolveObject(FoldedName(genTok)) is { Kind: SymbolKind.Sequence } seq)
                    {
                        AddReference(genTok, seq, ReferenceRole.SchemaObject);
                    }
                    i += 2;
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
