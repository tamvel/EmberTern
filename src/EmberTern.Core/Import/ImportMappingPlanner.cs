using System;
using System.Collections.Generic;
using System.Text;
using EmberTern.Core.Metadata;

namespace EmberTern.Core.Import;

/// <summary>The result of planning a mapping: the mapping itself, what is worth telling the user about it, and
/// which source fields nobody is using.</summary>
/// <param name="Mapping">One entry per target column, in catalog order — including the columns that can never
/// be written, so the grid is a direct projection and a blocked column is shown WITH its reason rather than
/// silently missing (§3.5).</param>
/// <param name="Diagnostics">Structured findings; App turns them into sentences.</param>
/// <param name="UnusedSourceFields">Fields no column consumes. Listing them is how "I forgot a column" becomes
/// visible instead of being discovered after the import.</param>
public sealed record ImportMappingPlan(
    IReadOnlyList<ColumnMapping> Mapping,
    IReadOnlyList<ImportDiagnostic> Diagnostics,
    IReadOnlyList<SourceField> UnusedSourceFields)
{
    public static readonly ImportMappingPlan Empty = new(
        Array.Empty<ColumnMapping>(), Array.Empty<ImportDiagnostic>(), Array.Empty<SourceField>());
}

/// <summary>
/// ⭐ Pairs target columns with source fields, and re-pairs them when either side changes.
/// <para>
/// <b>The governing rule (§4.7), carried over unchanged from the debugger's launch configuration (C3) because
/// the problem is identical:</b> <i>keep everything that can be PROVEN still correct, hand back everything that
/// cannot, and never guess.</i>
/// </para>
/// <list type="bullet">
/// <item><b>Proof is name equality.</b> A field still called <c>Kod fantomu</c> still maps to
/// <c>KOD_FANTOMU</c> — <see cref="MappingOrigin.Restored"/>.</item>
/// <item><b>The sole-remaining-pair rule</b> fires only when exactly ONE mappable column and exactly ONE unused
/// field remain — <see cref="MappingOrigin.Assumed"/>, rendered distinctly because it rests on position rather
/// than identity. Two or more on either side ⇒ nothing is paired.</item>
/// <item><b>A user's decision outranks the planner.</b> A manual mapping stays <see cref="MappingOrigin.Manual"/>
/// as long as its field still exists, and a deliberate skip survives a re-read — a skip is a decision, not an
/// absence.</item>
/// <item><b>Changing the target table clears the mapping entirely</b> — a different table is a different
/// identity. That is the caller's call to make (it passes no previous mapping), and it is never done silently:
/// see <see cref="Clear"/>.</item>
/// </list>
/// <para>
/// ⭐ <b>One owner for "is this mapping any good".</b> <see cref="Diagnose"/> is called by this class AND by
/// <see cref="ImportReadiness"/>, and loading a saved profile runs the very same <see cref="Plan"/> that a
/// manual source change runs (§4.8.5). There is exactly one answer to "is this column required and unmapped",
/// in one place.
/// </para>
/// </summary>
public static class ImportMappingPlanner
{
    /// <summary>
    /// Plans a mapping, preserving whatever <paramref name="previous"/> proves is still correct. Pass
    /// <c>null</c> for a fresh automatic mapping (a newly chosen target).
    /// </summary>
    public static ImportMappingPlan Plan(
        ImportTarget target,
        SourceSchema schema,
        IReadOnlyList<ColumnMapping>? previous = null)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (schema is null) throw new ArgumentNullException(nameof(schema));

        var diagnostics = new List<ImportDiagnostic>();
        var result = new List<ColumnMapping>(target.Columns.Count);
        var takenFields = new HashSet<int>();

        var previousByColumn = IndexPrevious(previous);
        var fieldsByNormalizedName = IndexFields(schema);

        // ── Pass 1: carry over everything that is provably still valid ──────────────────────────────────
        foreach (var column in target.Columns)
        {
            if (!IsMappable(column, out var blockingCode))
            {
                if (blockingCode != ImportDiagnosticCode.None)
                {
                    diagnostics.Add(new ImportDiagnostic(
                        blockingCode,
                        blockingCode == ImportDiagnosticCode.UnsupportedColumnType
                            ? ImportSeverity.Warning
                            : ImportSeverity.Info,
                        column.Name));
                }
                result.Add(ColumnMapping.Unmapped(column.Name));
                continue;
            }

            if (!previousByColumn.TryGetValue(column.Name, out var prior))
            {
                result.Add(ColumnMapping.Unmapped(column.Name));
                continue;
            }

            if (prior.IsSkipped)
            {
                // A skip is a decision; it must survive a re-read of the source.
                result.Add(ColumnMapping.Skipped(column.Name));
                continue;
            }

            var carried = TryCarry(prior, schema, takenFields);
            if (carried is not null)
            {
                result.Add(carried);
                continue;
            }

            if (prior.IsMapped)
            {
                // The field it pointed at is gone. §0.7: never let a re-read quietly change the import.
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticCode.MappingDropped, ImportSeverity.Warning,
                    prior.SourceFieldName ?? column.Name));
            }

            result.Add(ColumnMapping.Unmapped(column.Name));
        }

        // ── Pass 2: automatic matching by name, for whatever is still unmapped ──────────────────────────
        for (var i = 0; i < result.Count; i++)
        {
            var mapping = result[i];
            if (mapping.IsMapped || mapping.IsSkipped) continue;

            var column = target.FindColumn(mapping.TargetColumnName);
            if (column is null || !IsMappable(column, out _)) continue;

            var key = NormalizeName(mapping.TargetColumnName);
            if (!fieldsByNormalizedName.TryGetValue(key, out var matches)) continue;

            if (matches.Count > 1)
            {
                // Two fields answer to the same name. Handing the ambiguity back beats picking the first (§0).
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticCode.AmbiguousNameMatch, ImportSeverity.Warning,
                    mapping.TargetColumnName, matches.Count));
                continue;
            }

            var field = matches[0];
            if (!takenFields.Add(field.Index)) continue;

            result[i] = mapping with
            {
                SourceFieldName = field.HasRealName ? field.Name : null,
                SourceFieldIndex = field.Index,
                Origin = MappingOrigin.Restored,
            };
        }

        // ── Pass 3: the sole-remaining-pair rule ────────────────────────────────────────────────────────
        ApplySoleRemainingPair(target, schema, result, takenFields, diagnostics);

        var unused = CollectUnused(schema, takenFields);
        diagnostics.AddRange(Diagnose(target, schema, result));

        return new ImportMappingPlan(result, diagnostics, unused);
    }

    /// <summary>Maps columns to fields by POSITION — the explicit "Match by position" gesture (§3.5). Every
    /// pairing is <see cref="MappingOrigin.Manual"/>, because the user asked for it by name.</summary>
    public static ImportMappingPlan MatchByPosition(ImportTarget target, SourceSchema schema)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (schema is null) throw new ArgumentNullException(nameof(schema));

        var diagnostics = new List<ImportDiagnostic>();
        var result = new List<ColumnMapping>(target.Columns.Count);
        var takenFields = new HashSet<int>();
        var next = 0;

        foreach (var column in target.Columns)
        {
            if (!IsMappable(column, out _) || next >= schema.Fields.Count)
            {
                result.Add(ColumnMapping.Unmapped(column.Name));
                continue;
            }

            var field = schema.Fields[next++];
            takenFields.Add(field.Index);
            result.Add(new ColumnMapping
            {
                TargetColumnName = column.Name,
                SourceFieldName = field.HasRealName ? field.Name : null,
                SourceFieldIndex = field.Index,
                Origin = MappingOrigin.Manual,
            });
        }

        diagnostics.AddRange(Diagnose(target, schema, result));
        return new ImportMappingPlan(result, diagnostics, CollectUnused(schema, takenFields));
    }

    /// <summary>Every column unmapped — the "Clear" gesture, and the state a target-table change produces
    /// (§4.7: a different table is a different identity, so nothing carries over).</summary>
    public static ImportMappingPlan Clear(ImportTarget target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));

        var result = new List<ColumnMapping>(target.Columns.Count);
        foreach (var column in target.Columns) result.Add(ColumnMapping.Unmapped(column.Name));

        return new ImportMappingPlan(result, Diagnose(target, SourceSchema.Empty, result), Array.Empty<SourceField>());
    }

    /// <summary>
    /// ⭐ The ONE analysis of "is this mapping good enough to run". Called by <see cref="Plan"/> and by
    /// <see cref="ImportReadiness"/>, so the mapping panel and the readiness strip cannot disagree.
    /// </summary>
    public static IReadOnlyList<ImportDiagnostic> Diagnose(
        ImportTarget target,
        SourceSchema schema,
        IReadOnlyList<ColumnMapping> mapping)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (schema is null) throw new ArgumentNullException(nameof(schema));
        if (mapping is null) throw new ArgumentNullException(nameof(mapping));

        var diagnostics = new List<ImportDiagnostic>();
        var unmappedCount = 0;
        var takenFields = new HashSet<int>();

        foreach (var entry in mapping)
        {
            var column = target.FindColumn(entry.TargetColumnName);
            if (column is null) continue;

            if (entry.IsMapped) takenFields.Add(entry.SourceFieldIndex);

            var type = ImportTargetType.Resolve(column);

            if (entry.IsMapped)
            {
                if (!type.IsSupported)
                {
                    diagnostics.Add(new ImportDiagnostic(
                        ImportDiagnosticCode.UnsupportedColumnType, ImportSeverity.Error, column.Name));
                }

                if (ImportTarget.RequiresOverridingSystemValue(column))
                {
                    diagnostics.Add(new ImportDiagnostic(
                        ImportDiagnosticCode.IdentityOverrideRequired, ImportSeverity.Info, column.Name));
                }

                continue;
            }

            if (!IsMappable(column, out _)) continue;

            unmappedCount++;

            // A column the INSERT cannot leave out: NOT NULL, no DEFAULT, and nothing else will fill it.
            // Every row would fail, so this blocks rather than warns.
            if (column.NotNull && string.IsNullOrWhiteSpace(column.DefaultValue))
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticCode.RequiredColumnNotMapped, ImportSeverity.Error, column.Name));
            }
        }

        if (unmappedCount > 0)
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticCode.TargetColumnNotMapped, ImportSeverity.Warning, Count: unmappedCount));
        }

        var unusedCount = 0;
        foreach (var field in schema.Fields)
        {
            if (!takenFields.Contains(field.Index)) unusedCount++;
        }
        if (unusedCount > 0)
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticCode.SourceFieldUnused, ImportSeverity.Info, Count: unusedCount));
        }

        return diagnostics;
    }

    /// <summary>
    /// Pipeline step 2 (§4.4): pulls one record's raw values into target-column order.
    /// <para>
    /// Takes the ALREADY-FILTERED mapped columns (see <c>ImportConfiguration.MappedColumns</c>) because this
    /// runs once per row: filtering a million times would be a million pointless passes. A field the record
    /// does not reach yields <c>null</c> — a ragged record is legal, and the absent field is simply absent
    /// rather than shifting its neighbours.
    /// </para>
    /// </summary>
    public static object?[] Project(RawRecord record, IReadOnlyList<ColumnMapping> mappedColumns)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));
        if (mappedColumns is null) throw new ArgumentNullException(nameof(mappedColumns));

        var values = new object?[mappedColumns.Count];
        for (var i = 0; i < mappedColumns.Count; i++)
        {
            values[i] = record.ValueAt(mappedColumns[i].SourceFieldIndex);
        }
        return values;
    }

    /// <summary>
    /// ⭐ The normalization behind "proof is name equality". Case is folded, and space / underscore / hyphen /
    /// dot are treated as the same word break — so a spreadsheet's <c>Nr technologii</c> matches a column
    /// called <c>NR_TECHNOLOGII</c>, which is the everyday case the whole feature exists for (§3.5).
    /// <para>
    /// It deliberately stops there. Diacritics are NOT stripped and nothing is stemmed: those would conflate
    /// genuinely different names, and a wrong automatic match writes the wrong column's data — the worst class
    /// of defect this project recognises (§0.1). Where normalization does make two fields collide, the planner
    /// reports the ambiguity and matches neither.
    /// </para>
    /// </summary>
    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var builder = new StringBuilder(name.Length);
        var pendingBreak = false;

        foreach (var ch in name.Trim())
        {
            if (ch is ' ' or '_' or '-' or '.' || char.IsWhiteSpace(ch))
            {
                pendingBreak = builder.Length > 0;
                continue;
            }

            if (pendingBreak)
            {
                builder.Append('_');
                pendingBreak = false;
            }
            builder.Append(char.ToUpperInvariant(ch));
        }

        return builder.ToString();
    }

    // ── Internals ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Whether a column may carry imported data at all, and why not when it may not.</summary>
    private static bool IsMappable(ColumnSpec column, out ImportDiagnosticCode reason)
    {
        if (ImportTarget.IsNeverWritable(column))
        {
            // COMPUTED BY: Firebird rejects an INSERT naming it. Shown with its reason, never hidden (§3.5).
            reason = ImportDiagnosticCode.ColumnNotWritable;
            return false;
        }

        if (!ImportTargetType.Resolve(column).IsSupported)
        {
            reason = ImportDiagnosticCode.UnsupportedColumnType;
            return false;
        }

        reason = ImportDiagnosticCode.None;
        return true;
    }

    private static Dictionary<string, ColumnMapping> IndexPrevious(IReadOnlyList<ColumnMapping>? previous)
    {
        var map = new Dictionary<string, ColumnMapping>(StringComparer.OrdinalIgnoreCase);
        if (previous is null) return map;

        foreach (var entry in previous) map[entry.TargetColumnName] = entry;
        return map;
    }

    /// <summary>Groups source fields by their normalized name. A name shared by two fields keeps BOTH in the
    /// bucket rather than the last one winning — the ambiguity is raised where a target column actually asks
    /// for that name, because reporting every duplicate header would be noise.</summary>
    private static Dictionary<string, List<SourceField>> IndexFields(SourceSchema schema)
    {
        var map = new Dictionary<string, List<SourceField>>(StringComparer.Ordinal);
        foreach (var field in schema.Fields)
        {
            var key = NormalizeName(field.Name);
            if (key.Length == 0) continue;

            if (!map.TryGetValue(key, out var bucket))
            {
                bucket = new List<SourceField>(1);
                map[key] = bucket;
            }
            bucket.Add(field);
        }

        return map;
    }

    /// <summary>Carries a previous mapping forward when — and only when — it can be proven still valid.</summary>
    private static ColumnMapping? TryCarry(ColumnMapping prior, SourceSchema schema, HashSet<int> takenFields)
    {
        if (!prior.IsMapped) return null;

        if (prior.SourceFieldName is { Length: > 0 })
        {
            var byName = schema.FindByName(prior.SourceFieldName);
            if (byName is null || !takenFields.Add(byName.Index)) return null;

            // The name is the identity, so a moved column follows its data. The origin is preserved: a manual
            // choice that is still valid is still the user's, not the planner's.
            return prior with
            {
                SourceFieldIndex = byName.Index,
                Origin = prior.Origin == MappingOrigin.Manual ? MappingOrigin.Manual : MappingOrigin.Restored,
            };
        }

        // No name recorded ⇒ the source had no header when this mapping was made. Position is the only
        // identity such a source ever had, so it may be carried — but only while the source is STILL
        // headerless. Once names exist they are the better identity, and pass 2 will use them.
        if (schema.HasHeader) return null;
        if (prior.SourceFieldIndex < 0 || prior.SourceFieldIndex >= schema.Fields.Count) return null;
        if (!takenFields.Add(prior.SourceFieldIndex)) return null;

        return prior;
    }

    private static void ApplySoleRemainingPair(
        ImportTarget target,
        SourceSchema schema,
        List<ColumnMapping> result,
        HashSet<int> takenFields,
        List<ImportDiagnostic> diagnostics)
    {
        var candidateIndex = -1;
        for (var i = 0; i < result.Count; i++)
        {
            var mapping = result[i];
            if (mapping.IsMapped || mapping.IsSkipped) continue;

            var column = target.FindColumn(mapping.TargetColumnName);
            if (column is null || !IsMappable(column, out _)) continue;

            // Two or more unmatched columns ⇒ the rule does not fire at all. It is not "pair the first one".
            if (candidateIndex >= 0) return;
            candidateIndex = i;
        }
        if (candidateIndex < 0) return;

        SourceField? candidateField = null;
        foreach (var field in schema.Fields)
        {
            if (takenFields.Contains(field.Index)) continue;
            if (candidateField is not null) return;
            candidateField = field;
        }
        if (candidateField is null) return;

        takenFields.Add(candidateField.Index);
        result[candidateIndex] = result[candidateIndex] with
        {
            SourceFieldName = candidateField.HasRealName ? candidateField.Name : null,
            SourceFieldIndex = candidateField.Index,
            Origin = MappingOrigin.Assumed,
        };

        diagnostics.Add(new ImportDiagnostic(
            ImportDiagnosticCode.PairingAssumed, ImportSeverity.Info, result[candidateIndex].TargetColumnName));
    }

    private static IReadOnlyList<SourceField> CollectUnused(SourceSchema schema, HashSet<int> takenFields)
    {
        var unused = new List<SourceField>();
        foreach (var field in schema.Fields)
        {
            if (!takenFields.Contains(field.Index)) unused.Add(field);
        }
        return unused;
    }
}
