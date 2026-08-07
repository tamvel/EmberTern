using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using EmberTern.App.Controls;

namespace EmberTern.App.Behaviors;

/// <summary>
/// The ONE seam every editable <see cref="DataGrid"/> in EmberTern goes through, carrying two things that
/// used to have no single owner: the <b>Enter gesture</b> and the <b>cell-editor height role</b>.
///
/// <para>⭐⭐ WHY ONE SEAM FOR TWO PROBLEMS — they had one cause (stabilization sprint S-1a + S-3,
/// 2026-08-05). The set of "editable definition grids" was implicit: it was <em>whoever calls
/// <c>FieldGridColumns.Build</c></em>, which is where the <c>field-grid</c> class was applied. Table Detail
/// Fields, New Table Fields and View Detail Columns build their columns in XAML and only INSERT the shared
/// picker column, so they silently missed anything hung on that call — the reported "the TextBox in Table is
/// still too low" (S-3) and, had it been hung there, the Enter gesture too. Making the set EXPLICIT and
/// guarded is the actual fix; the two behaviours are then just what the seam carries.</para>
///
/// <para>⭐ THE RATIFIED UX RULE (user, 2026-08-05): <b>Enter does what clicking that cell does.</b> One rule
/// for definition grids and data grids alike — the user rejected two behaviours for two kinds of editable
/// grid — and it applies only to cells that are actually editable; where a cell is not editable, Enter keeps
/// its existing meaning.</para>
///
/// <para>⭐⭐ AND SINCE 2026-08-07 THE HEIGHT ROLE IS ONE RULE TOO — the seam no longer has a "kind". It used
/// to carry an <c>EditableGridKind</c> whose only job was to withhold the height role from Table Data, on the
/// stated ground that <em>"a 24 px minimum grows every row the moment editing starts, because those grids have
/// no ComboBox holding the row open (22–32 px rows)"</em>. ⚠⚠ <b>That reasoning is sound about a grid in
/// general and was never checked against the one grid it governed.</b> <c>TableDetailTabView.axaml</c> pins
/// <c>DataGrid.data-edit DataGridRow</c> to a fixed <c>Height="32"</c> — an exact height, not a minimum, so it
/// cannot grow from its content at all — and with that view's <c>DataGridCell</c> padding of <c>6 2</c> the
/// cell offers 28 px to a 24 px editor. The distinction therefore protected nothing and cost the reported
/// defect: <em>"the TextBox while editing is still too low"</em>.</para>
///
/// <para>⚠ THE CONDITION THAT REPLACES IT, so the next author does not have to re-derive it: a grid that goes
/// through this seam must let its ROW carry a <c>Size.Control</c> editor — i.e. declare a row height of at
/// least <c>Size.Control</c> + its cell padding. Every editable grid in the app does today (definition rows
/// measure ≥30 px because of their Type combo; Table Data pins 32). <c>EditableGridSeamTests</c> pins that
/// premise against the markup rather than trusting this paragraph. The read-only result grids never reach
/// here at all — they have no in-cell editor to size.</para>
///
/// <para>⚠⚠ MEASURED FRAMEWORK FACTS this rests on (headless probe, Avalonia 12.0.0 — none of them are
/// guesses, and two contradict the obvious approach):</para>
/// <list type="number">
/// <item><b>Enter is claimed by the DataGrid itself.</b> <c>ProcessEnterKey</c> commits any edit and moves
/// down one row — that is the framework's design, not our bug, and it is exactly the reported symptom
/// ("Enter moves to the next row"). <c>ProcessF2Key</c> is what begins an edit.</item>
/// <item><b>A TUNNEL handler is required.</b> Measured on the grid: at tunnel our handler sees Enter first
/// and unhandled; by the bubble phase it is ALREADY handled. A bubbling <c>KeyDown</c> would therefore run
/// after the selection had moved. Same justification as gotchas #224 / #298 — a tunnel handler is right when
/// the control genuinely claims the key.</item>
/// <item><b>There is no public "am I editing" on <see cref="DataGrid"/>.</b> The only public editing-related
/// member is <c>CurrentColumn</c>. So the gate is FOCUS: when nothing in a cell has focus the grid itself is
/// the focused element (measured), and once an edit begins focus is on the editing control. Acting only while
/// the GRID holds focus therefore means "not editing yet" without needing a flag — and it also hands Enter
/// back to an embedded control the moment the user is inside one.</item>
/// <item><b><c>BeginEdit()</c> focuses the editing element itself</b>, so a real editable column needs
/// nothing beyond that call.</item>
/// </list>
/// </summary>
internal static class EditableGridBehavior
{
    /// <summary>
    /// Attaches the seam. Idempotent per grid — a second call is a no-op, so a view that re-runs its
    /// column setup cannot double-handle Enter.
    /// </summary>
    public static void Attach(DataGrid grid)
    {
        if (grid is null || grid.Classes.Contains(AttachedClass)) return;
        grid.Classes.Add(AttachedClass);

        // The cell-editor height role. Scope is the CLASS ON THE GRID, which is why it can be granted here
        // and only here — a global setter would reach the READ-ONLY result grids too, which have no in-cell
        // editor and no row height reserved for one.
        if (!grid.Classes.Contains(FieldGridClass))
        {
            grid.Classes.Add(FieldGridClass);
        }

        grid.AddHandler(InputElement.KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
    }

    /// <summary>The marker that makes <see cref="Attach"/> idempotent and lets a test see which grids went
    /// through the seam.</summary>
    internal const string AttachedClass = "editable-grid";

    /// <summary>The class carrying the in-cell editor height role (<c>DataGrid.field-grid DataGridCell
    /// TextBox</c> in <c>ControlStyles.axaml</c>). Owned here now; <c>FieldGridColumns</c> used to apply it,
    /// which is precisely why the three grids that build their own columns never received it.</summary>
    internal const string FieldGridClass = "field-grid";

    private static void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.Handled) return;
        if (sender is not DataGrid grid || grid.IsReadOnly) return;

        // Not our Enter: something inside a cell holds focus, so an edit is either already running or the
        // user is inside an always-visible editor. Either way Enter belongs to that control (commit / newline
        // / picker accept), exactly as it does today. When nothing in a cell has focus, the focused element
        // is the grid itself — measured.
        if (!ReferenceEquals(TopLevel.GetTopLevel(grid)?.FocusManager?.GetFocusedElement(), grid))
        {
            return;
        }

        var column = grid.CurrentColumn;
        if (column is null) return;

        // A genuinely editable column: begin editing, which also focuses the editing element.
        if (!column.IsReadOnly)
        {
            if (grid.BeginEdit()) e.Handled = true;
            return;
        }

        // An IsReadOnly column holding an ALWAYS-VISIBLE editor — the Type combo, the merged Domain/Column
        // picker, and the Size / Scale / Sub Type / Charset boxes are all built this way, because a
        // DataGridTextColumn supports IsReadOnly only per COLUMN while their enable gate is per ROW
        // (gotcha #83 / #124). There is no edit MODE to begin, so Enter does what a click does: put keyboard
        // focus in the control, and open it if it is a picker.
        var target = FirstInteractiveControlInCurrentCell(grid, column);
        if (target is null) return;

        target.Focus(NavigationMethod.Tab);
        OpenIfPicker(target);
        e.Handled = true;
    }

    /// <summary>
    /// The first focusable, enabled control inside the current cell, or <c>null</c>.
    /// <para>
    /// ⚠ The cell is located from <see cref="DataGrid.SelectedItem"/> + the column's
    /// <c>DisplayIndex</c>, because <c>DataGridCell</c> exposes no public <c>Column</c> and the focused
    /// element is the grid rather than a cell (both measured). DisplayIndex — not the column's position in
    /// <c>Columns</c> — because these grids allow column reordering, and the cells sit in DISPLAY order.
    /// </para>
    /// <para>
    /// ⭐ A DISABLED editor is deliberately skipped rather than focused: those boxes are disabled exactly
    /// when a domain or TYPE OF governs the type, and clicking one does nothing either — so "Enter does what
    /// a click does" means doing nothing here too.
    /// </para>
    /// </summary>
    private static Control? FirstInteractiveControlInCurrentCell(DataGrid grid, DataGridColumn column)
    {
        var row = grid.GetVisualDescendants().OfType<DataGridRow>()
            .FirstOrDefault(r => ReferenceEquals(r.DataContext, grid.SelectedItem));
        if (row is null) return null;

        var cells = row.GetVisualDescendants().OfType<DataGridCell>().ToList();
        var index = column.DisplayIndex;
        if (index < 0 || index >= cells.Count) return null;

        return cells[index].GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(c => c.Focusable && c.IsEffectivelyEnabled && c.IsVisible);
    }

    // Clicking a closed picker opens it, so Enter must too. Both picker types in these grids expose the same
    // property name; neither shares a base type that declares it, hence the two arms.
    private static void OpenIfPicker(Control target)
    {
        switch (target)
        {
            case SearchableComboBox searchable: searchable.IsDropDownOpen = true; break;
            case ComboBox combo: combo.IsDropDownOpen = true; break;
        }
    }
}
