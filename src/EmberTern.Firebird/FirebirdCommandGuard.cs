using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using EmberTern.Core.Connections;
using EmberTern.Core.Localization;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>
/// The message keys the charset guard produces. Resolved by the App (decision D‑3); Core and Firebird hand up
/// a key plus data and never a sentence.
/// </summary>
public static class CharsetGuardMessages
{
    /// <summary>Statement text. <c>{0}</c> character, <c>{1}</c> code point, <c>{2}</c> index, <c>{3}</c> charset.</summary>
    public static readonly MessageKey UnrepresentableInStatement = new("Charset.Unrepresentable.Statement");

    /// <summary>A bound parameter. <c>{0}</c> parameter name, <c>{1}</c> character, <c>{2}</c> code point,
    /// <c>{3}</c> index, <c>{4}</c> charset.</summary>
    public static readonly MessageKey UnrepresentableInParameter = new("Charset.Unrepresentable.Parameter");
}

/// <summary>
/// Text that the connection's charset cannot carry unchanged, refused <b>before</b> it reached the driver.
///
/// <para>⭐ Carries both a <see cref="Localized"/> description and an English <see cref="Exception.Message"/>,
/// for the reason <see cref="ConnectionFailedException"/> gives: a path nobody enumerated may read
/// <c>Message</c>, and an unmigrated path must degrade to today's behaviour rather than to a raw key.</para>
///
/// <para>⚠ Most call sites translate this into their own domain exception (<see cref="QueryExecutionException"/>,
/// <see cref="DdlExecutionException"/>, …) so the existing UI error paths keep working unchanged; the original
/// stays reachable as <c>InnerException</c>, so a surface that later wants the localized form can take it
/// without a new mechanism.</para>
/// </summary>
public sealed class CharsetRepresentationException : Exception
{
    public CharsetRepresentationException(LocalizableMessage localized, string message)
        : base(message)
    {
        Localized = localized ?? throw new ArgumentNullException(nameof(localized));
    }

    /// <summary>The user-facing description, unresolved. Resolve at the moment of display, never earlier.</summary>
    public LocalizableMessage Localized { get; }
}

/// <summary>
/// ⭐⭐ <b>THE seam between EmberTern and the Firebird driver.</b> Every command this product runs is built
/// here, and nothing text-bearing reaches <c>FbCommand</c> without passing the charset check first.
///
/// <para>
/// <b>Why a seam at all, rather than a check per feature.</b> The loss is not a property of a feature — it is a
/// property of the connection: the driver encodes <i>statement text</i> and <i>string parameters</i> with the
/// connection charset and destroys anything that charset cannot hold, client-side, before the server sees it
/// (see <see cref="CharsetRepresentation"/> for the measurement). Every path that sends text is therefore
/// equally exposed: F5, DDL compile, the debugger's <c>EXECUTE BLOCK</c> harness, import, grid edits, metadata
/// search. Three patches for the three paths the audit happened to measure would have left the other three
/// open, which is why the mechanism is one.
/// </para>
///
/// <para>
/// ⭐ <b>The charset comes from the CONNECTION, not from an ambient profile.</b> It is read off
/// <see cref="FbConnection.ConnectionString"/> — the charset of the very attachment that will do the encoding.
/// EmberTern runs three lanes plus debugger and import sessions, so "the active profile's charset" would be
/// the wrong answer on any command that is not on the Data lane, and wrong in a way no test would notice.
/// </para>
///
/// <para>
/// ⛔ <b>The check is applied UNIFORMLY, and deliberately not only where text "looks user-supplied".</b>
/// Constant ASCII catalog SQL passes in ~1.8 µs, so classifying 96 call sites into risky and safe would buy
/// nothing and cost the one question the seam exists to end: <i>did we miss one?</i>
/// <c>CharsetGuardSeamTests</c> fails the build when a raw <c>CreateCommand()</c> / <c>new FbCommand</c> /
/// <c>new FbBatchCommand</c> appears outside this file.
/// </para>
/// </summary>
internal static class FirebirdCommandGuard
{
    /// <summary>
    /// Creates a command and verifies <paramref name="sql"/> against the connection's charset.
    /// ⛔ The ONE way this codebase may create an <see cref="FbCommand"/>.
    /// </summary>
    /// <exception cref="CharsetRepresentationException">
    /// <paramref name="sql"/> contains a character the connection charset would silently alter.
    /// </exception>
    public static FbCommand CreateGuardedCommand(this FbConnection connection, string sql)
    {
        ArgumentNullException.ThrowIfNull(connection);

        VerifyStatement(connection, sql);

        var command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    // ⛔ There is deliberately NO "create now, set the text later" overload. Every one of the 96 migrated call
    // sites knows its SQL at creation time, and an overload with no caller is an invitation to a call site that
    // sets CommandText itself — the exact bypass this seam exists to prevent. If a future site genuinely needs
    // deferred text, that is a design conversation, not a missing helper.

    /// <summary>
    /// Binds a parameter, verifying it first when it carries text.
    /// <para>⚠ Only <see cref="string"/> values are inspected — an <c>int</c>, a <c>DateTime</c> or a
    /// <c>byte[]</c> is not encoded through the charset, and checking them would be theatre.</para>
    /// </summary>
    /// <exception cref="CharsetRepresentationException">
    /// <paramref name="value"/> is text containing a character the connection charset would silently alter.
    /// </exception>
    public static FbParameter AddGuardedParameter(this FbCommand command, string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Connection is { } connection) VerifyParameter(connection, name, value);
        return command.Parameters.AddWithValue(name, value);
    }

    /// <summary>
    /// The POSITIONAL form — the debugger's <c>EXECUTE BLOCK</c> harness binds its frame values without names.
    /// <para>⭐ This path matters more than its size suggests: the harness carries the user's own PSQL, so a
    /// character the connection cannot represent would make the debugger execute <b>different code than the one
    /// on screen</b>, which is precisely what the fidelity law (§F) forbids. Refusing here turns a silent
    /// divergence into a refusal to start.</para>
    /// </summary>
    public static FbParameter AddGuardedParameter(this FbCommand command, object? value)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Connection is { } connection)
        {
            VerifyParameter(
                connection,
                "#" + command.Parameters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                value);
        }

        var parameter = new FbParameter { Value = value ?? DBNull.Value };
        command.Parameters.Add(parameter);
        return parameter;
    }

    /// <summary>The <see cref="FbBatchCommand"/> form — import's batched INSERT path (measured at ~121 000
    /// rows/s, so it stays a batch; only the values it binds move through the guard).</summary>
    public static FbBatchCommand CreateGuardedBatchCommand(
        this FbConnection connection, string sql, FbTransaction? transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);

        VerifyStatement(connection, sql);
        return new FbBatchCommand(sql, connection, transaction);
    }

    /// <summary>Verifies one value destined for a batch parameter set, whose collection has no command to
    /// reach the connection through.</summary>
    public static void VerifyBatchValue(FbConnection connection, string name, object? value)
        => VerifyParameter(connection, name, value);

    /// <summary>
    /// ⭐ Verifies an entire batch of statements <b>up front</b> — before a transaction is opened and before a
    /// single statement runs.
    /// <para>
    /// The per-command check would already refuse the offending statement, but it would refuse it <i>partway
    /// through the batch</i>: statement 1 executed, statement 3 rejected, the whole thing rolled back. Correct,
    /// and still not what rule #11 asks for. Checking first means a DDL compile that carries an unrepresentable
    /// character <b>never reaches the server at all</b> — no transaction, no statement, no rollback to trust.
    /// That is the difference between "we undid it" and "we never did it".
    /// </para>
    /// </summary>
    public static void VerifyStatements(FbConnection connection, IReadOnlyList<string> statements)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (statements is null) return;

        for (var i = 0; i < statements.Count; i++) VerifyStatement(connection, statements[i]);
    }

    // ── the check itself ──────────────────────────────────────────────────────────────────────────────

    private static void VerifyStatement(FbConnection connection, string? sql)
    {
        if (string.IsNullOrEmpty(sql)) return;

        var (strict, charsetName) = StrictFor(connection);
        var violation = CharsetRepresentation.FindFirstUnrepresentable(sql, strict);
        if (violation is null) return;

        var v = violation.Value;
        throw new CharsetRepresentationException(
            LocalizableMessage.Of(
                CharsetGuardMessages.UnrepresentableInStatement,
                v.Text, v.CodePoint, v.Index, charsetName),
            $"The statement contains the character '{v.Text}' ({v.CodePoint}) at position {v.Index}, which the "
            + $"connection character set {charsetName} cannot represent. Sending it would have changed it "
            + "silently, so nothing was sent. Use a UTF8 connection character set, or remove the character.");
    }

    private static void VerifyParameter(FbConnection connection, string name, object? value)
    {
        if (value is not string text || text.Length == 0) return;

        var (strict, charsetName) = StrictFor(connection);
        var violation = CharsetRepresentation.FindFirstUnrepresentable(text, strict);
        if (violation is null) return;

        var v = violation.Value;
        throw new CharsetRepresentationException(
            LocalizableMessage.Of(
                CharsetGuardMessages.UnrepresentableInParameter,
                name, v.Text, v.CodePoint, v.Index, charsetName),
            $"Parameter {name} contains the character '{v.Text}' ({v.CodePoint}) at position {v.Index}, which "
            + $"the connection character set {charsetName} cannot represent. Sending it would have changed it "
            + "silently, so nothing was sent. Use a UTF8 connection character set, or remove the character.");
    }

    // ── resolving the connection's charset, once per connection ───────────────────────────────────────

    private sealed class StrictEntry
    {
        public string ConnectionString = string.Empty;
        public Encoding? Strict;
        public string CharsetName = string.Empty;
    }

    // Keyed on the connection OBJECT rather than on its string: the connection string carries the password, and
    // a static dictionary keyed by it would keep credentials alive for the process lifetime. The table also
    // evicts itself when a connection is collected.
    private static readonly ConditionalWeakTable<FbConnection, StrictEntry> Cache = new();

    private static (Encoding? Strict, string CharsetName) StrictFor(FbConnection connection)
    {
        var connectionString = connection.ConnectionString ?? string.Empty;
        var entry = Cache.GetOrCreateValue(connection);

        // Re-parse only when the connection string actually changed (a reused FbConnection reopened against a
        // different profile). The common path is one reference comparison.
        if (!ReferenceEquals(entry.ConnectionString, connectionString)
            && !string.Equals(entry.ConnectionString, connectionString, StringComparison.Ordinal))
        {
            var charset = ReadCharset(connectionString);
            entry.CharsetName = charset;
            entry.Strict = CharsetRepresentation.Strict(charset);
            entry.ConnectionString = connectionString;
        }

        return (entry.Strict, entry.CharsetName);
    }

    private static string ReadCharset(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return CharsetCatalog.Default;

        try
        {
            var charset = new FbConnectionStringBuilder(connectionString).Charset;
            return string.IsNullOrWhiteSpace(charset) ? CharsetCatalog.Default : charset;
        }
        catch (ArgumentException)
        {
            // An unparseable connection string is not this guard's problem to report — the connection itself
            // will fail. Fall back to the product default so the check stays armed rather than silently off.
            return CharsetCatalog.Default;
        }
    }
}
