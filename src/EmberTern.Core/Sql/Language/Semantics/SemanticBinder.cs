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
    {
        var binder = new SemanticBinder(script, metadata);
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
    }

    private void BindStatement(SqlStatement stmt)
    {
        switch (stmt)
        {
            case SelectStatement:
                BindQuery(stmt.Tokens, 0, stmt.Tokens.Count, _root, stmt);
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

            case ExecuteBlockStatement:
                BindExecuteBlock(stmt);
                break;

            case AnonymousBlockStatement:
                BindAnonymousBlock(stmt);
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
