using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.App.Completion;

/// <summary>
/// An immutable <see cref="ISqlMetadataProvider"/> built from the App's metadata caches on the UI
/// thread (Etap 5 / M1, design §22.1). The semantic model is built off-thread and the interface is
/// documented as a <b>snapshot</b>, so this must not read live App state after construction: it
/// captures the loaded schema objects and the currently-cached columns at build time and never
/// touches the VM again. Columns/params are lazily loaded by the App, so a snapshot only carries
/// what is already cached — a completion that needs an uncached table's columns warms the App cache
/// and the next snapshot picks it up (the same warm-then-rebuild dance the controller already does).
/// </summary>
internal sealed class AppMetadataSnapshot : ISqlMetadataProvider
{
    private static readonly IReadOnlyList<ColumnMetadata> NoColumns = Array.Empty<ColumnMetadata>();
    private static readonly IReadOnlyList<RoutineParameterMetadata> NoParameters =
        Array.Empty<RoutineParameterMetadata>();

    private readonly IReadOnlyList<ObjectMetadata> _allObjects;
    private readonly IReadOnlyDictionary<string, ObjectMetadata> _byName;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ColumnSpec>> _columns;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<RoutineParameterMetadata>> _routineParameters;
    private readonly IReadOnlyDictionary<string, ObjectDetail> _detail;

    private AppMetadataSnapshot(
        IReadOnlyList<ObjectMetadata> allObjects,
        IReadOnlyDictionary<string, ObjectMetadata> byName,
        IReadOnlyDictionary<string, IReadOnlyList<ColumnSpec>> columns,
        IReadOnlyDictionary<string, IReadOnlyList<RoutineParameterMetadata>> routineParameters,
        IReadOnlyDictionary<string, ObjectDetail> detail)
    {
        _allObjects = allObjects;
        _byName = byName;
        _columns = columns;
        _routineParameters = routineParameters;
        _detail = detail;
    }

    /// <summary>Builds a snapshot from the loaded metadata objects and the current column /
    /// routine-parameter caches. All inputs are read on the UI thread; the snapshot copies what it
    /// needs so the caller may keep mutating its caches afterward. First-name-wins for
    /// <see cref="FindObject"/> mirrors the metadata-tree category order (Tables before Triggers, …),
    /// so a name shared across kinds resolves to the table.</summary>
    public static AppMetadataSnapshot Build(
        IReadOnlyList<MetadataObject> objects,
        IReadOnlyDictionary<string, IReadOnlyList<ColumnSpec>> columnCache,
        IReadOnlyDictionary<string, IReadOnlyList<RoutineParameterMetadata>>? routineParameterCache = null,
        IReadOnlyDictionary<string, ObjectDetail>? objectDetailCache = null)
    {
        var all = new List<ObjectMetadata>(objects.Count);
        var byName = new Dictionary<string, ObjectMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in objects)
        {
            var meta = new ObjectMetadata(o.Name, o.Kind.ToSymbolKind());
            all.Add(meta);
            byName.TryAdd(o.Name, meta);
        }

        // Shallow copy the caches: the value lists are already immutable, only the dictionaries need
        // to be detached from the live VM caches so an off-thread lookup can't race a mutation.
        var cols = new Dictionary<string, IReadOnlyList<ColumnSpec>>(columnCache, StringComparer.OrdinalIgnoreCase);
        var routines = routineParameterCache is null
            ? EmptyRoutines
            : new Dictionary<string, IReadOnlyList<RoutineParameterMetadata>>(routineParameterCache, StringComparer.OrdinalIgnoreCase);
        var detail = objectDetailCache is null
            ? EmptyDetail
            : new Dictionary<string, ObjectDetail>(objectDetailCache, StringComparer.OrdinalIgnoreCase);
        return new AppMetadataSnapshot(all, byName, cols, routines, detail);
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<RoutineParameterMetadata>> EmptyRoutines =
        new Dictionary<string, IReadOnlyList<RoutineParameterMetadata>>();

    private static readonly IReadOnlyDictionary<string, ObjectDetail> EmptyDetail =
        new Dictionary<string, ObjectDetail>();

    // Merges the warmed rich facts (description / function return type / trigger header) onto the
    // always-present name+kind, so Quick Info reads one enriched ObjectMetadata from the in-memory
    // snapshot — never a DB query at display time (Package 5, Stage B/C).
    public ObjectMetadata? FindObject(string name)
    {
        if (name is null || !_byName.TryGetValue(name, out var m)) return null;
        return _detail.TryGetValue(name, out var d)
            ? m with { Description = d.Description, ReturnType = d.ReturnType, Trigger = d.Trigger, Generator = d.Generator }
            : m;
    }

    public IReadOnlyList<ColumnMetadata> GetColumns(string tableOrView)
        => tableOrView is not null && _columns.TryGetValue(tableOrView, out var c) && c.Count > 0
            ? c.Select(ToColumnMetadata).ToList()
            : NoColumns;

    /// <summary>
    /// ⭐ The one place the App can answer "not loaded yet" honestly: the cache DICTIONARY distinguishes a
    /// missing key from a present-but-empty entry, while <see cref="GetColumns"/> collapses both to an empty
    /// list. So the information existed all along and was being thrown away one layer too early — which is
    /// what let <c>DiagnosticsEngine</c> report every unwarmed column as unknown (S-2, 2026-08-05).
    /// <para>
    /// ⚠ A present-but-EMPTY entry counts as KNOWN: the warm pass caches what it read, so an object with no
    /// columns (or one whose read legitimately returned none) has been answered and must not silence a
    /// genuine typo forever.
    /// </para>
    /// </summary>
    public bool KnowsColumns(string tableOrView)
        => tableOrView is not null && _columns.ContainsKey(tableOrView);

    // Maps the enriched ColumnSpec (Package 5, Stage A) onto the semantic ColumnMetadata
    // the language front-end already renders. Every field the Firebird reader now fills
    // (default/computed/description + PK/FK/FK-target/identity) flows straight through to
    // QuickInfoEngine.ForColumn and CompletionEngine — one column model, many consumers.
    private static ColumnMetadata ToColumnMetadata(ColumnSpec s)
        => new(s.Name, s.Type)
        {
            Domain = s.Domain,
            Nullable = !s.NotNull,
            DefaultValue = s.DefaultValue,
            Description = s.Description,
            IsPrimaryKey = s.IsPrimaryKey,
            IsForeignKey = s.IsForeignKey,
            ForeignKeyTable = s.ForeignKeyTable,
            IsComputed = s.IsComputed,
            Identity = s.Identity,
        };

    // Routine parameters from the VM's routine-param cache captured at build time (M6). Lazily
    // warmed by the App on a signature-help miss (the warm-then-rebuild dance, like columns).
    public IReadOnlyList<RoutineParameterMetadata> GetRoutineParameters(string routine)
        => routine is not null && _routineParameters.TryGetValue(routine, out var p) && p.Count > 0
            ? p
            : NoParameters;

    /// <summary>
    /// ⭐ The same honest "not loaded yet" the column cache can give, for parameters — and it became
    /// load-bearing when a selectable procedure's OUTPUT parameters became the column set of a
    /// <c>FROM MY_PROC(…) alias</c> entry (2026-08-12). The DICTIONARY distinguishes a missing key from a
    /// present-but-empty entry; <see cref="GetRoutineParameters"/> collapses both, exactly as
    /// <see cref="GetColumns"/> does.
    /// <para>⚠ A present-but-EMPTY entry counts as KNOWN, for the same reason as columns: the warm pass caches
    /// what it read, so a routine with no parameters has been answered and must not silence a genuine typo
    /// forever.</para>
    /// </summary>
    public bool KnowsRoutineParameters(string routine)
        => routine is not null && _routineParameters.ContainsKey(routine);

    public IReadOnlyList<ObjectMetadata> AllObjects() => _allObjects;
}

/// <summary>The warmed rich facts for one schema object (Package 5, Stage B/C) — a description, a
/// function's return type, a trigger's header — kept apart from the always-cheap name+kind so the VM
/// warms them lazily per referenced object and the snapshot merges them into
/// <see cref="AppMetadataSnapshot.FindObject"/>. Any field may be <c>null</c> (not applicable / not
/// present).</summary>
internal sealed record ObjectDetail(string? Description, string? ReturnType, TriggerDetail? Trigger, GeneratorDetail? Generator = null);
