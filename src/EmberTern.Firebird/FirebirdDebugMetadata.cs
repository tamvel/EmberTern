using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FirebirdSql.Data.FirebirdClient;
using EmberTern.Core.Sql.Debugging;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Firebird;

/// <summary>The resolved layout of a debug frame's variables: the harness variable templates (values unset),
/// the ordered <b>input</b> parameters (a step-into seeds these positionally from the call's arguments — D8),
/// and the names of the <b>output</b> parameters (a <c>SUSPEND</c> emits their current values as the output
/// row; a return binds them into the caller's <c>RETURNING_VALUES</c>). Stage X / D2 seam c + D8.
/// <para><see cref="ReturnType"/> is a local <b>function</b>'s resolved <c>RETURNS</c> base type (R2) — the
/// type the Expression Harness gives the result column that computes a stepped-into function's <c>RETURN</c>
/// value (Stage X / D9 seam c, §6.4); null for a procedure (its outputs are the named
/// <see cref="OutputParameters"/>).</para></summary>
internal sealed record DebugFrameLayout(
    IReadOnlyList<HarnessVariable> Variables,
    IReadOnlyList<string> OutputParameters,
    IReadOnlyList<HarnessVariable> InputParameters,
    string? ReturnType = null);

/// <summary>
/// Resolves a debug session's <b>frame variable templates</b> — the harness's <see cref="HarnessVariable"/>
/// list (spec §3.4): each in-scope variable's <b>verbatim declaration</b> (R3) and its <b>base type</b> (R2),
/// resolved from metadata, once at session start (Stage X / D2 seam c). "Derivation, not guessing" (§3.4): a
/// base type comes from <c>RDB$FIELDS</c> via <see cref="FirebirdDdlReader.FormatType"/> — the app's one type
/// formatter — never from string-munging a declaration.
/// <para>
/// A standalone procedure's <b>parameters</b> come from <c>RDB$PROCEDURE_PARAMETERS</c> (declared with their
/// user domain when they have one, else their base type; injected/returned as base types — R2); its
/// <b>locals</b> come from the parsed body via <see cref="PsqlDeclarationExtractor"/> (declared verbatim — R3),
/// their base type resolved from the declared type spec (a domain → <c>FormatType</c>; a parametrised builtin
/// → itself). <b>D2 boundary</b>: a <c>TYPE OF</c> variable is not yet resolved (a clear, explained stop —
/// §F — rather than a guess); the lab zoo uses domains + builtins.
/// </para>
/// </summary>
internal static class FirebirdDebugMetadata
{
    /// <summary>Builds the frame variable templates (values unset — the executor fills them per step from the
    /// frame). Parameters first (input then output), then the body's locals in source order. Also reports the
    /// <b>output</b> parameter names — a <c>SUSPEND</c> emits their current values as the routine's output row
    /// (client-side control flow, not a server round-trip).</summary>
    public static async Task<DebugFrameLayout> BuildFrameVariablesAsync(
        DebugSessionConnection session,
        string? routineName,
        BlockStatement body,
        string source,
        CancellationToken cancellationToken = default,
        string? packageName = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(source);

        var result = new List<HarnessVariable>();
        var outputs = new List<string>();
        var inputs = new List<HarnessVariable>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(routineName))
        {
            foreach (var (variable, isOutput) in await ReadProcedureParametersAsync(session, routineName!, packageName, cancellationToken).ConfigureAwait(false))
            {
                if (!seen.Add(variable.Name))
                {
                    continue;
                }
                result.Add(variable);
                if (isOutput)
                {
                    outputs.Add(variable.Name);
                }
                else
                {
                    inputs.Add(variable); // ordered input params — a step-into seeds these (D8)
                }
            }
        }

        foreach (var local in PsqlDeclarationExtractor.Extract(body, source).Locals)
        {
            if (!seen.Add(local.Name))
            {
                continue;
            }
            string baseType = await ResolveBaseTypeAsync(session, local.TypeSpec, local.Name, cancellationToken).ConfigureAwait(false);
            result.Add(new HarnessVariable(local.Name, local.Verbatim, baseType));
        }

        return new DebugFrameLayout(result, outputs, inputs);
    }

    /// <summary>Builds the frame variable templates for a standalone PSQL <b>function</b> launched as the debug
    /// ROOT (D-function). The function's input arguments <b>and</b> its <c>RETURNS</c> base type come from the
    /// SAME single <c>RDB$FUNCTION_ARGUMENTS</c> read (<see cref="ReadFunctionParametersAsync"/>) — "resolve
    /// once": each input base-typed exactly as a procedure parameter is (R2 for injection, the user domain kept
    /// for the declaration R3), and the <c>RETURNS</c> base type is the one the Expression Harness gives the
    /// <c>RETURN</c> result column (surfaced as <see cref="DebugFrameLayout.ReturnType"/> → the root
    /// <see cref="Frame.ReturnType"/>). Body locals as usual. A function has <b>no output parameters and no
    /// <c>SUSPEND</c></b>, so the outputs list is empty. FB3+ standalone (<c>RDB$PACKAGE_NAME IS NULL</c>).</summary>
    public static async Task<DebugFrameLayout> BuildFunctionFrameVariablesAsync(
        DebugSessionConnection session,
        string functionName,
        BlockStatement body,
        string source,
        CancellationToken cancellationToken = default,
        string? packageName = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(functionName);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(source);

        var result = new List<HarnessVariable>();
        var inputs = new List<HarnessVariable>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var (funcInputs, returnType) = await ReadFunctionParametersAsync(session, functionName, packageName, cancellationToken).ConfigureAwait(false);
        foreach (var v in funcInputs)
        {
            if (!seen.Add(v.Name)) continue;
            result.Add(v);
            inputs.Add(v); // ordered input args — seeded from the launch arguments (D8 seeding, reused)
        }

        foreach (var local in PsqlDeclarationExtractor.Extract(body, source).Locals)
        {
            if (!seen.Add(local.Name)) continue;
            string baseType = await ResolveBaseTypeAsync(session, local.TypeSpec, local.Name, cancellationToken).ConfigureAwait(false);
            result.Add(new HarnessVariable(local.Name, local.Verbatim, baseType));
        }

        return new DebugFrameLayout(result, new List<string>(), inputs, returnType);
    }

    /// <summary>Builds the frame variable templates for a <b>local</b> sub-routine (Stage X / D9 seam a part
    /// 2). A local <c>DECLARE PROCEDURE/FUNCTION</c> is <b>not</b> a catalog object — it has no
    /// <c>RDB$PROCEDURE_PARAMETERS</c> row — so its parameter and <c>RETURNS</c> types come from the parsed AST
    /// header (<see cref="PsqlDeclarationExtractor.ExtractSignature"/>), the one new metadata source of this
    /// milestone. Each parameter is declared <b>verbatim</b> from its written type (R3) and its <b>base type</b>
    /// derived (R2, from <c>RDB$FIELDS</c> when the written type is a domain, else the builtin itself) — exactly
    /// as a stored routine's parameters are, only sourced from the AST rather than the catalog. Locals come from
    /// the sub-routine body as usual.</summary>
    public static async Task<DebugFrameLayout> BuildLocalRoutineFrameVariablesAsync(
        DebugSessionConnection session,
        SubroutineDeclaration routine,
        string source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(routine);
        ArgumentNullException.ThrowIfNull(source);
        if (routine.Body is null)
        {
            throw new NotSupportedException(
                "Debug (D9): a forward-declared local sub-routine has no body to step into.");
        }

        var result = new List<HarnessVariable>();
        var outputs = new List<string>();
        var inputs = new List<HarnessVariable>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sig = PsqlDeclarationExtractor.ExtractSignature(routine, source);
        foreach (var p in sig.Inputs)
        {
            if (!seen.Add(p.Name)) continue;
            var v = await BuildLocalParamAsync(session, p, cancellationToken).ConfigureAwait(false);
            result.Add(v);
            inputs.Add(v); // ordered input params — a step-into seeds these (D8 mechanism, reused)
        }
        foreach (var p in sig.Outputs)
        {
            if (!seen.Add(p.Name)) continue;
            var v = await BuildLocalParamAsync(session, p, cancellationToken).ConfigureAwait(false);
            result.Add(v);
            outputs.Add(p.Name);
        }
        foreach (var local in PsqlDeclarationExtractor.Extract(routine.Body, source).Locals)
        {
            if (!seen.Add(local.Name)) continue;
            string baseType = await ResolveBaseTypeAsync(session, local.TypeSpec, local.Name, cancellationToken).ConfigureAwait(false);
            result.Add(new HarnessVariable(local.Name, local.Verbatim, baseType));
        }

        // A local FUNCTION's single RETURNS <type> (D9 seam c, §6.4): resolve its base type (R2), the type the
        // Expression Harness gives the RETURN result column. Null for a procedure (sig.ReturnType is null there).
        string? returnType = null;
        if (sig.ReturnType is { Length: > 0 } rt)
        {
            returnType = await ResolveBaseTypeAsync(session, rt, routine.Name ?? "(function)", cancellationToken).ConfigureAwait(false);
        }

        return new DebugFrameLayout(result, outputs, inputs, returnType);
    }

    // A local sub-routine parameter has no catalog row: declare it VERBATIM with its AST-written type (R3 — a
    // domain keeps its semantics), base type derived for the harness parameter / RETURNS column (R2).
    private static async Task<HarnessVariable> BuildLocalParamAsync(
        DebugSessionConnection session, SubroutineParam p, CancellationToken cancellationToken)
    {
        string baseType = await ResolveBaseTypeAsync(session, p.TypeSpec, p.Name, cancellationToken).ConfigureAwait(false);
        return new HarnessVariable(p.Name, $"DECLARE {p.Name} {p.TypeSpec.Trim()};", baseType);
    }

    // ── Trigger context columns — NEW/OLD as frame variables (Stage X / D10) ─────────────────────────

    /// <summary>Builds the harness variable templates for a trigger's <c>NEW</c>/<c>OLD</c> context columns
    /// (spec §8.1). <c>NEW</c>/<c>OLD</c> do not exist inside an <c>EXECUTE BLOCK</c>, so
    /// <see cref="ContextSubstitution"/> rewrites each <c>NEW.col</c>/<c>OLD.col</c> to a synthetic frame
    /// variable and this resolves that variable's type from the <b>table's</b> column (the one new metadata path
    /// of D10). The synthetic name comes from the <see cref="ContextColumn"/> — <see cref="ContextSubstitution"/>
    /// is the single owner of that convention. A distinct <c>NEW.col</c> and <c>OLD.col</c> of the same column
    /// share the column's type under two synthetic names.
    /// <para>
    /// <b>Declared with the BASE type, never the column's domain (R2, and deliberately NOT R3).</b> Unlike a
    /// procedure parameter or a body local — a domain-typed variable declared verbatim (R3) — a <c>NEW</c>/
    /// <c>OLD</c> field is a <b>record field</b>, not a constrained local. In a real trigger the user-supplied
    /// value can be any value of the base type, <b>including one that violates the column's domain <c>CHECK</c>/
    /// <c>NOT NULL</c></b>: a BEFORE trigger exists precisely to validate or fix such a value before the row is
    /// written, and the domain/column constraint is enforced at write time — <b>after</b> the trigger, which the
    /// debugger never performs. Declaring the context variable with the domain would re-validate the injected
    /// value on entry and fail on exactly the case the trigger is meant to catch (e.g. a negative amount injected
    /// into a <c>CHECK (VALUE &gt;= 0)</c> domain — proven live). So the base type is both the R2 rule and the
    /// faithful record-field model. Gotcha #246.</para></summary>
    public static async Task<IReadOnlyList<HarnessVariable>> BuildTriggerContextVariablesAsync(
        DebugSessionConnection session,
        string targetTable,
        IReadOnlyList<ContextColumn> columns,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(targetTable);
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0)
        {
            return System.Array.Empty<HarnessVariable>();
        }

        var baseTypeByColumn = await ReadTableColumnTypesAsync(session, targetTable, cancellationToken).ConfigureAwait(false);

        var result = new List<HarnessVariable>(columns.Count);
        foreach (var c in columns)
        {
            if (!baseTypeByColumn.TryGetValue(c.Column, out var baseType))
            {
                throw new NotSupportedException(
                    $"Debug (D10): column {targetTable}.{c.Column} was not found — cannot type the NEW/OLD context variable.");
            }
            // Base type for BOTH the declaration and the harness param/return (R2) — a NEW/OLD record field is
            // never re-validated against the column's domain inside the trigger (see the remarks above).
            result.Add(new HarnessVariable(c.Synthetic, $"DECLARE {c.Synthetic} {baseType};", baseType));
        }
        return result;
    }

    // The BASE type of every column of a table, keyed by folded column name (via FormatType — derivation, not
    // string-munging). One round-trip for the whole table (a trigger references only a few columns, but reading
    // them all in one query is simpler than one query per column and mirrors the catalog readers). The domain is
    // deliberately NOT carried — a NEW/OLD context variable is declared with its base type (R2, see the remarks
    // on BuildTriggerContextVariablesAsync).
    private static async Task<Dictionary<string, string>> ReadTableColumnTypesAsync(
        DebugSessionConnection session, string targetTable, CancellationToken cancellationToken)
    {
        const string sql =
            "SELECT rf.RDB$FIELD_NAME, " +
            "       f.RDB$FIELD_TYPE, f.RDB$FIELD_SUB_TYPE, f.RDB$FIELD_LENGTH, " +
            "       f.RDB$FIELD_PRECISION, f.RDB$FIELD_SCALE, f.RDB$CHARACTER_LENGTH " +
            "FROM RDB$RELATION_FIELDS rf " +
            "JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE " +
            "WHERE rf.RDB$RELATION_NAME = @t";

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await session.CommandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = session.Connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = session.Transaction;
            cmd.Parameters.Add(new FbParameter("@t", targetTable.ToUpperInvariant()));
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string name = TrimStr(reader, 0);
                string baseType = FirebirdDdlReader.FormatType(
                    Sh(reader, 1), Sh(reader, 2), Sh(reader, 3), Sh(reader, 4), Sh(reader, 5), Sh(reader, 6));
                map[name] = baseType;
            }
        }
        finally
        {
            session.CommandLock.Release();
        }
        return map;
    }

    // ── Parameters (RDB$PROCEDURE_PARAMETERS) ───────────────────────────────────────────────────────

    private static async Task<List<(HarnessVariable Variable, bool IsOutput)>> ReadProcedureParametersAsync(
        DebugSessionConnection session, string routineName, string? packageName, CancellationToken cancellationToken)
    {
        // A standalone routine's parameters have RDB$PACKAGE_NAME IS NULL; a package member's are keyed by the
        // package (Stage X / D11 — verified live, §15.12: both public and private members' params are here). The
        // package/standalone distinction is the ONLY difference — the same catalog, join and typing (D8) serve
        // both, so a package member reuses the stored-routine layout path rather than a parallel one.
        string packageFilter = packageName is null
            ? "AND pp.RDB$PACKAGE_NAME IS NULL "
            : "AND pp.RDB$PACKAGE_NAME = @pkg ";
        string sql =
            "SELECT pp.RDB$PARAMETER_NAME, pp.RDB$FIELD_SOURCE, pp.RDB$PARAMETER_TYPE, " +
            "       f.RDB$FIELD_TYPE, f.RDB$FIELD_SUB_TYPE, f.RDB$FIELD_LENGTH, " +
            "       f.RDB$FIELD_PRECISION, f.RDB$FIELD_SCALE, f.RDB$CHARACTER_LENGTH " +
            "FROM RDB$PROCEDURE_PARAMETERS pp " +
            "JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = pp.RDB$FIELD_SOURCE " +
            "WHERE pp.RDB$PROCEDURE_NAME = @proc " + packageFilter +
            "ORDER BY pp.RDB$PARAMETER_TYPE, pp.RDB$PARAMETER_NUMBER";

        var list = new List<(HarnessVariable, bool)>();
        await session.CommandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = session.Connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = session.Transaction;
            cmd.Parameters.Add(new FbParameter("@proc", routineName.ToUpperInvariant()));
            if (packageName is not null) cmd.Parameters.Add(new FbParameter("@pkg", packageName.ToUpperInvariant()));
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string name = TrimStr(reader, 0);
                string fieldSource = TrimStr(reader, 1);
                bool isOutput = (Sh(reader, 2) ?? 0) == 1; // RDB$PARAMETER_TYPE: 0 = input, 1 = output
                string baseType = FirebirdDdlReader.FormatType(
                    Sh(reader, 3), Sh(reader, 4), Sh(reader, 5), Sh(reader, 6), Sh(reader, 7), Sh(reader, 8));
                // Declare with the user domain when the parameter has one (R3), else the base type (R2). The
                // harness parameter and RETURNS column always use the base type (R2), so a legitimately-null
                // write-back never re-validates a domain.
                string declType = IsUserDomain(fieldSource) ? fieldSource : baseType;
                list.Add((new HarnessVariable(name, $"DECLARE {name} {declType};", baseType), isOutput));
            }
        }
        finally
        {
            session.CommandLock.Release();
        }
        return list;
    }

    // ── Function arguments (RDB$FUNCTION_ARGUMENTS) — the function-root layout source ─────────────────

    // A PSQL function's input arguments + its RETURNS base type, from ONE catalog read (D-function, "resolve
    // once"). The return argument is the one at RDB$FUNCTIONS.RDB$RETURN_ARGUMENT; every other argument is an
    // input. Types are base types via RDB$FIELDS (FormatType — derivation, R2), exactly as a procedure's
    // parameters (ReadProcedureParametersAsync); an input keeps its user domain for the declaration (R3) but is
    // injected as the base type (R2). A STANDALONE function has RDB$PACKAGE_NAME IS NULL; a PACKAGE member's
    // args are keyed by the package (Stage X / Seam D) — the SAME query/join/typing, only the package filter
    // differs (exactly the standalone-vs-packaged parity ReadProcedureParametersAsync already has). The debugger
    // is FB3+ (P2 gate), so RDB$PACKAGE_NAME always exists.
    private static async Task<(List<HarnessVariable> Inputs, string? ReturnBaseType)> ReadFunctionParametersAsync(
        DebugSessionConnection session, string functionName, string? packageName, CancellationToken cancellationToken)
    {
        string pkgFilter = packageName is null ? "IS NULL" : "= @pkg";
        string sql =
            "SELECT fa.RDB$ARGUMENT_POSITION, fa.RDB$ARGUMENT_NAME, fa.RDB$FIELD_SOURCE, " +
            "       f.RDB$FIELD_TYPE, f.RDB$FIELD_SUB_TYPE, f.RDB$FIELD_LENGTH, " +
            "       f.RDB$FIELD_PRECISION, f.RDB$FIELD_SCALE, f.RDB$CHARACTER_LENGTH, " +
            "       fn.RDB$RETURN_ARGUMENT " +
            "FROM RDB$FUNCTION_ARGUMENTS fa " +
            "JOIN RDB$FUNCTIONS fn ON fn.RDB$FUNCTION_NAME = fa.RDB$FUNCTION_NAME AND fn.RDB$PACKAGE_NAME " + pkgFilter + " " +
            "JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = fa.RDB$FIELD_SOURCE " +
            "WHERE fa.RDB$FUNCTION_NAME = @fn AND fa.RDB$PACKAGE_NAME " + pkgFilter + " " +
            "ORDER BY fa.RDB$ARGUMENT_POSITION";

        var inputs = new List<HarnessVariable>();
        string? returnType = null;
        await session.CommandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = session.Connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = session.Transaction;
            cmd.Parameters.Add(new FbParameter("@fn", functionName.ToUpperInvariant()));
            if (packageName is not null) cmd.Parameters.Add(new FbParameter("@pkg", packageName.ToUpperInvariant()));
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                short pos = Sh(reader, 0) ?? -1;
                string name = TrimStr(reader, 1);
                string fieldSource = TrimStr(reader, 2);
                string baseType = FirebirdDdlReader.FormatType(
                    Sh(reader, 3), Sh(reader, 4), Sh(reader, 5), Sh(reader, 6), Sh(reader, 7), Sh(reader, 8));
                short returnPos = Sh(reader, 9) ?? 0;
                if (pos == returnPos)
                {
                    returnType = baseType; // the RETURNS base type — the Expression Harness's RETURN result column
                }
                else if (name.Length > 0)
                {
                    string declType = IsUserDomain(fieldSource) ? fieldSource : baseType;
                    inputs.Add(new HarnessVariable(name, $"DECLARE {name} {declType};", baseType));
                }
            }
        }
        finally
        {
            session.CommandLock.Release();
        }
        return (inputs, returnType);
    }

    // ── Base-type derivation (R2) ───────────────────────────────────────────────────────────────────

    private static async Task<string> ResolveBaseTypeAsync(
        DebugSessionConnection session, string typeSpec, string variableName, CancellationToken cancellationToken)
    {
        var s = (typeSpec ?? string.Empty).Trim();
        if (s.Length == 0)
        {
            throw new NotSupportedException($"Debug: could not read the declared type of variable '{variableName}'.");
        }

        if (s.StartsWith("TYPE ", StringComparison.OrdinalIgnoreCase))
        {
            // TYPE OF [COLUMN] … — a bounded D2 boundary (§F: an explained stop, not a guess). Resolved later.
            throw new NotSupportedException(
                $"Debug (D2): TYPE OF variable '{variableName}' is not yet supported.");
        }

        if (IsBareIdentifier(s))
        {
            // A bare identifier is either a user domain (resolve from RDB$FIELDS) or a keyword builtin
            // (INTEGER, DATE, BOOLEAN, …) — in which case the base type is the keyword itself.
            var domainType = await LookupDomainBaseTypeAsync(session, s, cancellationToken).ConfigureAwait(false);
            return domainType ?? s;
        }

        // A parametrised builtin (NUMERIC(15,2), VARCHAR(80), …) — the base type is the spec itself.
        return s;
    }

    private static async Task<string?> LookupDomainBaseTypeAsync(
        DebugSessionConnection session, string domainName, CancellationToken cancellationToken)
    {
        const string sql =
            "SELECT RDB$FIELD_TYPE, RDB$FIELD_SUB_TYPE, RDB$FIELD_LENGTH, " +
            "       RDB$FIELD_PRECISION, RDB$FIELD_SCALE, RDB$CHARACTER_LENGTH " +
            "FROM RDB$FIELDS WHERE RDB$FIELD_NAME = @n";

        await session.CommandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = session.Connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = session.Transaction;
            cmd.Parameters.Add(new FbParameter("@n", domainName.ToUpperInvariant()));
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return FirebirdDdlReader.FormatType(
                    Sh(reader, 0), Sh(reader, 1), Sh(reader, 2), Sh(reader, 3), Sh(reader, 4), Sh(reader, 5));
            }
        }
        finally
        {
            session.CommandLock.Release();
        }
        return null; // not a domain — a builtin keyword
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    // ⭐ One owner: FirebirdDdlReader.IsUserDomain. This was a private copy of the same expression, and
    // a THIRD copy was about to be written for procedure parameters (S-1b) — so the predicate moved
    // beside FormatType, which answers the other half of the same question. Behaviour-identical.
    // ⚠ The debugger's use is the OPPOSITE of the DDL reader's and must stay that way: here the domain
    // is what the frame variable is DECLARED with (R3, verbatim), while the value injected into the
    // harness is typed with the BASE type (R2) — a domain-constrained parameter cannot take an injected
    // NULL. The shared predicate answers "is this a user domain"; what each caller does with the answer
    // is its own business.
    private static bool IsUserDomain(string fieldSource) => FirebirdDdlReader.IsUserDomain(fieldSource);

    private static bool IsBareIdentifier(string s)
    {
        if (s.Length == 0 || !(char.IsLetter(s[0]) || s[0] == '_'))
        {
            return false;
        }
        foreach (var c in s)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '$'))
            {
                return false;
            }
        }
        return true;
    }

    private static string TrimStr(IDataReader reader, int i)
        => reader.IsDBNull(i) ? string.Empty : reader.GetString(i).Trim();

    private static short? Sh(IDataReader reader, int i)
    {
        if (reader.IsDBNull(i))
        {
            return null;
        }
        var v = reader.GetValue(i);
        return v is short s ? s : short.Parse(Convert.ToString(v, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
    }
}
