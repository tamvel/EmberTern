using System;
using System.Collections.Generic;

namespace EmberTern.Core.Import;

/// <summary>
/// One line of the readiness strip (§3.2).
/// </summary>
/// <param name="Code">What is wrong (or worth knowing) — a code, never a message (rule #6).</param>
/// <param name="Severity">How loudly it reads; App maps it through the same brush/geometry table the shared
/// <c>MessageBanner</c> uses, so the strip and a banner cannot describe the same idea differently (§9.3).</param>
/// <param name="IsBlocking">Whether it stops the import. A blocking item is always accompanied by a reason and
/// a way to reach the section that caused it — a disabled button with no explanation is a UX defect (§9.1).</param>
/// <param name="Section">Which section to expand and focus when the user clicks the item.</param>
/// <param name="Subject">The column, field or table it concerns, when it concerns exactly one.</param>
/// <param name="Count">How many things it concerns, when it is a tally. Numbers, not adjectives.</param>
public sealed record ReadinessItem(
    ImportDiagnosticCode Code,
    ImportSeverity Severity,
    bool IsBlocking,
    ImportSection Section,
    string? Subject = null,
    int? Count = null)
{
    /// <summary>The published <c>IMP####</c> code.</summary>
    public string CodeText => Code.ToCode();
}

/// <summary>
/// Everything <see cref="ImportReadiness.Evaluate"/> needs, as one value.
/// <para>
/// A record with defaults rather than a long parameter list, for the same reason
/// <c>ImportConfiguration</c> is one: adding an input later must not rewrite every call site. Facts read from
/// the world (the schema, the target, the transaction state) are inputs here precisely because they are NOT
/// part of the stored configuration — they are re-read on every load, and that re-read is what catches a
/// profile whose world has changed (§4.8.5).
/// </para>
/// </summary>
public sealed record ImportReadinessInput
{
    public ImportConfiguration Configuration { get; init; } = ImportConfiguration.Empty;

    /// <summary>The source's shape as last read; <c>null</c> when it has not been read yet.</summary>
    public SourceSchema? Schema { get; init; }

    /// <summary>False when the configured file is gone. Answered without opening anything — which is why a
    /// <c>SourceDescriptor</c> stores a path rather than a handle (§4.8.5).</summary>
    public bool SourceExists { get; init; } = true;

    /// <summary>False when the source exists but the provider could not read a schema from it.</summary>
    public bool SourceReadable { get; init; } = true;

    /// <summary>
    /// The resolved target; <c>null</c> when the configured table is not in the catalog.
    /// <para>
    /// For a NEW table this is the <see cref="ImportNewTable.Project">projection</see> of the columns the user
    /// is about to create — which is what lets the mapping, the preview and "Validate" all work before any DDL
    /// has run (etap I8). Everything below reads it the same way either way, because from here a table that
    /// will exist and a table that does are the same question.
    /// </para>
    /// </summary>
    public ImportTarget? Target { get; init; }

    /// <summary>True when the name chosen for a NEW table is already taken in this database. A fact read from
    /// the catalog, so — like the schema and the target — it is an input here rather than part of the stored
    /// configuration (§4.8.2), and it is re-read on every load.</summary>
    public bool NewTableNameTaken { get; init; }

    public bool IsConnected { get; init; } = true;


    /// <summary>Best estimate of how many rows will be written, for the long-transaction warning; <c>null</c>
    /// when unknown, in which case the warning simply is not raised (a guess would be worse than silence).</summary>
    public long? EstimatedRows { get; init; }

    /// <summary>How many sampled values the CONNECTION charset would damage — see
    /// <see cref="ImportCharsetGuard.CountUnrepresentable"/>. Supplied by the preview, because readiness reads
    /// no data of its own.</summary>
    public int ValuesNotRepresentableInCharset { get; init; }
}

/// <summary>The strip's whole answer.</summary>
public sealed record ImportReadinessReport(IReadOnlyList<ReadinessItem> Items)
{
    public static readonly ImportReadinessReport Empty = new(Array.Empty<ReadinessItem>());

    /// <summary>True when nothing blocks the import. <b>The only thing the surface ever disables</b> is the
    /// run itself (§2.2 point 5); every section stays usable so the user can fix whatever is wrong.</summary>
    public bool CanRun
    {
        get
        {
            foreach (var item in Items)
            {
                if (item.IsBlocking) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// True when nothing blocks <b>"Validate"</b> — which is a strictly weaker condition than
    /// <see cref="CanRun"/>, and deliberately so.
    /// <para>
    /// A dry run reads the file, converts, validates and writes nowhere (<see cref="DryRunImportWriter"/>), so
    /// nothing in the Transaction section bears on it — it needs no connection of its own and settles nothing.
    /// Everything else still blocks: without a readable source, a known target and a mapping there is nothing
    /// to validate.
    /// </para>
    /// <para>
    /// The rule lives here rather than in the surface because "what does this report permit" is this record's
    /// question; a view deciding it would be a second opinion on readiness.
    /// </para>
    /// </summary>
    public bool CanValidate
    {
        get
        {
            foreach (var item in Items)
            {
                if (item.IsBlocking && item.Section != ImportSection.Transaction) return false;
            }
            return true;
        }
    }

    /// <summary>The blocking items, in the order they were found.</summary>
    public IEnumerable<ReadinessItem> Blocking
    {
        get
        {
            foreach (var item in Items)
            {
                if (item.IsBlocking) yield return item;
            }
        }
    }

    /// <summary>The loudest severity recorded for <paramref name="section"/>, or <c>null</c> when the section
    /// has nothing to say — which is what the strip renders as a ✓.</summary>
    public ImportSeverity? SeverityFor(ImportSection section)
    {
        ImportSeverity? worst = null;
        foreach (var item in Items)
        {
            if (item.Section != section) continue;
            if (worst is null || item.Severity > worst) worst = item.Severity;
        }
        return worst;
    }

    /// <summary>True when nothing in <paramref name="section"/> blocks the run.</summary>
    public bool IsSectionRunnable(ImportSection section)
    {
        foreach (var item in Items)
        {
            if (item.Section == section && item.IsBlocking) return false;
        }
        return true;
    }
}

/// <summary>
/// ⭐ The readiness strip's engine (§3.2) — a <b>pure function</b> of the configuration and the facts read from
/// the world. Zero logic in the view: this is the same move that put <c>DebugPreflightItem.BannerSeverity</c>
/// in the model rather than the XAML.
/// <para>
/// <b>Why a strip rather than "Next" buttons:</b> it shows every gap at once instead of the first one, and
/// every item is clickable straight to the section that caused it. That is the one thing kept from the wizard
/// this module deliberately is not (§1.2).
/// </para>
/// <para>
/// ⭐ <b>It does not re-analyse the mapping.</b> Everything mapping-related comes from
/// <see cref="ImportMappingPlanner.Diagnose"/>, the same call the mapping panel makes — so a red strip and a
/// clean grid are impossible by construction, not by discipline.
/// </para>
/// </summary>
public static class ImportReadiness
{
    /// <summary>Rows above which a single-transaction import is worth warning about. Not about the import's
    /// speed — I0 measured a million rows at roughly eight seconds — but about how long the transaction then
    /// stays OPEN, which is what EmberTern's own Session Manager flags for other users (design R4).</summary>
    public const long LongTransactionRowThreshold = 100_000;

    /// <summary>Convenience overload for the common call.</summary>
    public static ImportReadinessReport Evaluate(
        ImportConfiguration configuration,
        SourceSchema? schema,
        ImportTarget? target,
        bool isConnected)
        => Evaluate(new ImportReadinessInput
        {
            Configuration = configuration,
            Schema = schema,
            Target = target,
            IsConnected = isConnected,
        });

    /// <summary>Evaluates the whole strip.</summary>
    public static ImportReadinessReport Evaluate(ImportReadinessInput input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));

        var items = new List<ReadinessItem>();
        var configuration = input.Configuration;

        EvaluateEnvironment(input, items);
        var sourceOk = EvaluateSource(input, items);
        EvaluateTarget(input, items);
        EvaluateMapping(input, sourceOk, items);
        EvaluateTransaction(input, items);

        if (configuration.Behavior.TrimTooLongValues)
        {
            // §0.2: trimming loses data, so enabling it is stated up front — not discovered in the report.
            items.Add(new ReadinessItem(
                ImportDiagnosticCode.TrimmingEnabled, ImportSeverity.Warning, false, ImportSection.Mapping));
        }

        return new ImportReadinessReport(items);
    }

    private static void EvaluateEnvironment(ImportReadinessInput input, List<ReadinessItem> items)
    {
        if (!input.IsConnected)
        {
            items.Add(new ReadinessItem(
                ImportDiagnosticCode.NotConnected, ImportSeverity.Error, true, ImportSection.Transaction));
        }

        // ⭐ I7.5: an open CONSOLE transaction is no longer reported at all — not as a block, not as a warning.
        // The import owns its own transaction now (§4.5 as amended), so what the SQL Editor happens to have
        // open is simply none of this module's business, and saying anything about it would be noise the user
        // cannot act on.
        //
        // It also dissolves a contradiction the design carried since I2: §3.2 listed an open working
        // transaction as BLOCKING while §4.5 had the writer auto-begin and join one. Both cannot be true. With
        // an independent transaction neither is needed.
    }

    /// <summary>Returns true when the source is usable, so later checks can stay quiet instead of piling
    /// consequences on top of a cause the user already has to fix.</summary>
    private static bool EvaluateSource(ImportReadinessInput input, List<ReadinessItem> items)
    {
        var source = input.Configuration.Source;

        if (source.IsFile && string.IsNullOrWhiteSpace(source.Path))
        {
            items.Add(new ReadinessItem(
                ImportDiagnosticCode.NoSource, ImportSeverity.Error, true, ImportSection.Source));
            return false;
        }

        if (source.IsFile && !input.SourceExists)
        {
            items.Add(new ReadinessItem(
                ImportDiagnosticCode.SourceMissing, ImportSeverity.Error, true, ImportSection.Source, source.Path));
            return false;
        }

        if (!input.Configuration.MatchesSourceKind)
        {
            items.Add(new ReadinessItem(
                ImportDiagnosticCode.SourceOptionsMismatch, ImportSeverity.Error, true, ImportSection.Format));
            return false;
        }

        if (!input.SourceReadable)
        {
            items.Add(new ReadinessItem(
                ImportDiagnosticCode.SourceUnreadable, ImportSeverity.Error, true, ImportSection.Source));
            return false;
        }

        if (input.Schema is null)
        {
            // Not read yet — a state, not a fault. It still blocks, because there is nothing to map.
            items.Add(new ReadinessItem(
                ImportDiagnosticCode.SourceHasNoFields, ImportSeverity.Error, true, ImportSection.Source));
            return false;
        }

        if (input.Schema.Fields.Count == 0)
        {
            items.Add(new ReadinessItem(
                ImportDiagnosticCode.SourceHasNoFields, ImportSeverity.Error, true, ImportSection.Source));
            return false;
        }

        if (input.ValuesNotRepresentableInCharset > 0)
        {
            // ⭐ R1. These values would be written as '?' with no error at all — including into a UTF8
            // column, because the CONNECTION charset decides. A warning, not a block: the user may legitimately
            // want to reconnect in UTF8, and the row validator will refuse each affected value anyway.
            items.Add(new ReadinessItem(
                ImportDiagnosticCode.NotRepresentableInConnectionCharset, ImportSeverity.Warning, false,
                ImportSection.Format, Count: input.ValuesNotRepresentableInCharset));
        }

        return true;
    }

    private static void EvaluateTarget(ImportReadinessInput input, List<ReadinessItem> items)
    {
        var target = input.Configuration.Target;

        if (string.IsNullOrWhiteSpace(target.TableName))
        {
            items.Add(new ReadinessItem(
                ImportDiagnosticCode.NoTarget, ImportSeverity.Error, true, ImportSection.Target));
            return;
        }

        if (target.Kind == ImportTargetKind.NewTable)
        {
            if (target.NewTableColumns.Count == 0)
            {
                items.Add(new ReadinessItem(
                    ImportDiagnosticCode.NewTableHasNoColumns, ImportSeverity.Error, true, ImportSection.Target));
                return;
            }

            if (input.NewTableNameTaken)
            {
                // Refused here rather than by the engine at run time: the CREATE is the very first thing an
                // import does, so letting this through would mean a green strip followed immediately by a raw
                // server error (§0).
                items.Add(new ReadinessItem(
                    ImportDiagnosticCode.NewTableAlreadyExists, ImportSeverity.Error, true,
                    ImportSection.Target, target.TableName));
                return;
            }

            // ⚠ §0.5 / gotcha #213: the CREATE runs on the Ddl lane and is COMMITTED before the first row,
            // because a Firebird transaction cannot use an object whose DDL it has not committed. So Rollback
            // will not remove this table, and the user is told so where the decision is made.
            items.Add(new ReadinessItem(
                ImportDiagnosticCode.NewTableWillBeCommitted, ImportSeverity.Warning, false,
                ImportSection.Target, target.TableName));
            return;
        }

        if (input.Target is null)
        {
            items.Add(new ReadinessItem(
                ImportDiagnosticCode.TargetNotFound, ImportSeverity.Error, true,
                ImportSection.Target, target.TableName));
            return;
        }

        if (input.Target.BeforeInsertTriggers.Count > 0)
        {
            // A BEFORE INSERT trigger can overwrite an imported value; a user who does not know that cannot
            // understand the result (design R6). It never changes what the import does.
            items.Add(new ReadinessItem(
                ImportDiagnosticCode.TargetHasBeforeInsertTriggers, ImportSeverity.Warning, false,
                ImportSection.Target, Count: input.Target.BeforeInsertTriggers.Count));
        }

        if (input.Configuration.Behavior.EmptyTargetBeforeImport)
        {
            items.Add(new ReadinessItem(
                ImportDiagnosticCode.TargetWillBeEmptied, ImportSeverity.Warning, false,
                ImportSection.Target, input.Target.TableName));
        }
    }

    private static void EvaluateMapping(ImportReadinessInput input, bool sourceOk, List<ReadinessItem> items)
    {
        var target = input.Target;
        if (!sourceOk || target is null || input.Schema is null) return;

        var mapping = input.Configuration.Mapping;

        var mappedCount = 0;
        foreach (var _ in input.Configuration.MappedColumns()) mappedCount++;

        if (mappedCount == 0)
        {
            items.Add(new ReadinessItem(
                ImportDiagnosticCode.NothingMapped, ImportSeverity.Error, true, ImportSection.Mapping));
            return;
        }

        // ⭐ One owner: the strip reads the planner's findings rather than re-deriving them.
        foreach (var diagnostic in ImportMappingPlanner.Diagnose(target, input.Schema, mapping))
        {
            var blocking = diagnostic.Code is ImportDiagnosticCode.RequiredColumnNotMapped
                or ImportDiagnosticCode.UnsupportedColumnType;

            items.Add(new ReadinessItem(
                diagnostic.Code, diagnostic.Severity, blocking, ImportSection.Mapping,
                diagnostic.Subject, diagnostic.Count));
        }
    }

    private static void EvaluateTransaction(ImportReadinessInput input, List<ReadinessItem> items)
    {
        var configuration = input.Configuration;

        if (configuration.Transaction == ImportTransactionMode.Batched)
        {
            // I0 measured commit frequency as nearly free, so this mode's only price is atomicity — which is
            // exactly the thing §0.5 says must be stated where the choice is made.
            items.Add(new ReadinessItem(
                ImportDiagnosticCode.BatchedIsNotAtomic, ImportSeverity.Warning, false,
                ImportSection.Transaction, Count: configuration.CommitEveryRows));
            return;
        }

        if (input.EstimatedRows is > LongTransactionRowThreshold)
        {
            items.Add(new ReadinessItem(
                ImportDiagnosticCode.LongTransactionRisk, ImportSeverity.Warning, false,
                ImportSection.Transaction, Count: (int)Math.Min(input.EstimatedRows.Value, int.MaxValue)));
        }
    }
}
