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
/// row; a return binds them into the caller's <c>RETURNING_VALUES</c>). Stage X / D2 seam c + D8.</summary>
internal sealed record DebugFrameLayout(
    IReadOnlyList<HarnessVariable> Variables,
    IReadOnlyList<string> OutputParameters,
    IReadOnlyList<HarnessVariable> InputParameters);

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
        CancellationToken cancellationToken = default)
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
            foreach (var (variable, isOutput) in await ReadProcedureParametersAsync(session, routineName!, cancellationToken).ConfigureAwait(false))
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

    // ── Parameters (RDB$PROCEDURE_PARAMETERS) ───────────────────────────────────────────────────────

    private static async Task<List<(HarnessVariable Variable, bool IsOutput)>> ReadProcedureParametersAsync(
        DebugSessionConnection session, string routineName, CancellationToken cancellationToken)
    {
        const string sql =
            "SELECT pp.RDB$PARAMETER_NAME, pp.RDB$FIELD_SOURCE, pp.RDB$PARAMETER_TYPE, " +
            "       f.RDB$FIELD_TYPE, f.RDB$FIELD_SUB_TYPE, f.RDB$FIELD_LENGTH, " +
            "       f.RDB$FIELD_PRECISION, f.RDB$FIELD_SCALE, f.RDB$CHARACTER_LENGTH " +
            "FROM RDB$PROCEDURE_PARAMETERS pp " +
            "JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = pp.RDB$FIELD_SOURCE " +
            "WHERE pp.RDB$PROCEDURE_NAME = @proc AND pp.RDB$PACKAGE_NAME IS NULL " +
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

    private static bool IsUserDomain(string fieldSource)
        => fieldSource.Length > 0 && !fieldSource.StartsWith("RDB$", StringComparison.OrdinalIgnoreCase);

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
