using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EmberTern.Core.Metadata;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Adapts an owner's column-listing method (e.g.
/// <c>MainWindowViewModel.EnsureColumnsAsync</c>) to <see cref="IColumnsLoader"/> for the
/// "Table column" tab of the merged Domain/Column picker — so the loader can be wired by a
/// delegate without a dedicated class per editor.
/// </summary>
public sealed class DelegateColumnsLoader : IColumnsLoader
{
    private readonly Func<string, Task<IReadOnlyList<ColumnSpec>>> _load;

    public DelegateColumnsLoader(Func<string, Task<IReadOnlyList<ColumnSpec>>> load) => _load = load;

    public Task<IReadOnlyList<ColumnSpec>> LoadColumnsAsync(string table) => _load(table);
}
