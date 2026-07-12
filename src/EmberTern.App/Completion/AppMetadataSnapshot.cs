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

    private AppMetadataSnapshot(
        IReadOnlyList<ObjectMetadata> allObjects,
        IReadOnlyDictionary<string, ObjectMetadata> byName,
        IReadOnlyDictionary<string, IReadOnlyList<ColumnSpec>> columns,
        IReadOnlyDictionary<string, IReadOnlyList<RoutineParameterMetadata>> routineParameters)
    {
        _allObjects = allObjects;
        _byName = byName;
        _columns = columns;
        _routineParameters = routineParameters;
    }

    /// <summary>Builds a snapshot from the loaded metadata objects and the current column /
    /// routine-parameter caches. All inputs are read on the UI thread; the snapshot copies what it
    /// needs so the caller may keep mutating its caches afterward. First-name-wins for
    /// <see cref="FindObject"/> mirrors the metadata-tree category order (Tables before Triggers, …),
    /// so a name shared across kinds resolves to the table.</summary>
    public static AppMetadataSnapshot Build(
        IReadOnlyList<MetadataObject> objects,
        IReadOnlyDictionary<string, IReadOnlyList<ColumnSpec>> columnCache,
        IReadOnlyDictionary<string, IReadOnlyList<RoutineParameterMetadata>>? routineParameterCache = null)
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
        return new AppMetadataSnapshot(all, byName, cols, routines);
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<RoutineParameterMetadata>> EmptyRoutines =
        new Dictionary<string, IReadOnlyList<RoutineParameterMetadata>>();

    public ObjectMetadata? FindObject(string name)
        => name is not null && _byName.TryGetValue(name, out var m) ? m : null;

    public IReadOnlyList<ColumnMetadata> GetColumns(string tableOrView)
        => tableOrView is not null && _columns.TryGetValue(tableOrView, out var c) && c.Count > 0
            ? c.Select(s => new ColumnMetadata(s.Name, s.Type) { Domain = s.Domain, Nullable = !s.NotNull }).ToList()
            : NoColumns;

    // Routine parameters from the VM's routine-param cache captured at build time (M6). Lazily
    // warmed by the App on a signature-help miss (the warm-then-rebuild dance, like columns).
    public IReadOnlyList<RoutineParameterMetadata> GetRoutineParameters(string routine)
        => routine is not null && _routineParameters.TryGetValue(routine, out var p) && p.Count > 0
            ? p
            : NoParameters;

    public IReadOnlyList<ObjectMetadata> AllObjects() => _allObjects;
}
