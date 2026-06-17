using System;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Core.Sql;

namespace EmberTern.App.ViewModels;

/// <summary>
/// One editable row in a procedure's <b>Variables</b> grid (Easy mode). Inherits the
/// full field-definition editing surface (Type/Domain dropdowns, TYPE OF, Size, Scale,
/// Sub Type, Charset, Collate, Not Null, Default) from <see cref="ProcedureFieldRowBase"/>
/// — the same infrastructure as the table field grids, no second type system. Maps
/// to/from the Core <see cref="ProcedureVariable"/>.
/// </summary>
public sealed class ProcedureVariableRowViewModel : ProcedureFieldRowBase
{
    public ProcedureVariableRowViewModel() : base(null) { }
    public ProcedureVariableRowViewModel(ProcedureDetailTabViewModel? owner) : base(owner) { }

    public ProcedureVariable ToVariable() => new()
    {
        Name = (Name ?? string.Empty).Trim(),
        TypeText = (TypeText ?? string.Empty).Trim(),
        NotNull = NotNull,
        Default = string.IsNullOrWhiteSpace(DefaultValue) ? null : DefaultValue.Trim(),
    };

    public static ProcedureVariableRowViewModel From(ProcedureVariable v, ProcedureDetailTabViewModel? owner = null)
    {
        var row = new ProcedureVariableRowViewModel(owner)
        {
            NotNull = v.NotNull,
            DefaultValue = v.Default ?? string.Empty,
        };
        row.Name = v.Name;
        row.LoadType(v.TypeText);
        return row;
    }
}

/// <summary>One editable row in a procedure's <b>Cursors</b> list + split editor.
/// <see cref="Declaration"/> is the full <c>DECLARE … CURSOR …;</c> text, edited
/// verbatim in the right-hand SQL editor. <see cref="Name"/> is editable directly in
/// the left list; <see cref="Scroll"/> toggles the <c>SCROLL</c> keyword. Editing the
/// name or Scroll regenerates the declaration so it stays in sync; conversely editing
/// the declaration re-derives the name. Maps to/from the Core <see cref="ProcedureCursor"/>.</summary>
public partial class ProcedureCursorRowViewModel : ObservableObject
{
    private bool _suppressSync;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private bool _scroll;
    [ObservableProperty] private string _declaration = string.Empty;

    partial void OnNameChanged(string value)
    {
        if (_suppressSync) return;
        var upper = (value ?? string.Empty).ToUpperInvariant();
        if (!string.Equals(value, upper, StringComparison.Ordinal)) { Name = upper; return; }
        RegenerateFromNameOrScroll();
    }

    partial void OnScrollChanged(bool value)
    {
        if (_suppressSync) return;
        RegenerateFromNameOrScroll();
    }

    partial void OnDeclarationChanged(string value)
    {
        if (_suppressSync) return;
        _suppressSync = true;
        try
        {
            var n = ProcedureBodySplitter.ParseCursorName(value);
            if (!string.IsNullOrEmpty(n)) Name = n;
            Scroll = ProcedureBodySplitter.CursorIsScroll(value);
        }
        finally { _suppressSync = false; }
    }

    // Rewrites the DECLARE header (name + optional SCROLL) while keeping the cursor's
    // SELECT body, so editing the name/Scroll in the list updates the declaration text.
    private void RegenerateFromNameOrScroll()
    {
        var rebuilt = ProcedureBodySplitter.RewriteCursorHeader(Declaration, Name, Scroll);
        if (rebuilt is null || string.Equals(rebuilt, Declaration, StringComparison.Ordinal)) return;
        _suppressSync = true;
        try { Declaration = rebuilt; }
        finally { _suppressSync = false; }
    }

    public ProcedureCursor ToCursor() => new()
    {
        Name = (Name ?? string.Empty).Trim(),
        Declaration = (Declaration ?? string.Empty).Trim(),
    };

    public static ProcedureCursorRowViewModel From(ProcedureCursor c)
    {
        var row = new ProcedureCursorRowViewModel();
        row._suppressSync = true;
        row.Name = c.Name;
        row.Declaration = c.Declaration;
        row.Scroll = ProcedureBodySplitter.CursorIsScroll(c.Declaration);
        row._suppressSync = false;
        return row;
    }
}

/// <summary>One editable row in a procedure's <b>Subprograms</b> list + split editor.
/// <see cref="Declaration"/> is the full <c>DECLARE PROCEDURE|FUNCTION …</c> text;
/// <see cref="Name"/> is editable directly in the left list (regenerates the
/// declaration header); <see cref="Kind"/> is chosen at creation. Maps to/from the
/// Core <see cref="ProcedureSubprogram"/>.</summary>
public partial class ProcedureSubprogramRowViewModel : ObservableObject
{
    private bool _suppressSync;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _kind = "PROCEDURE";
    [ObservableProperty] private string _declaration = string.Empty;

    partial void OnNameChanged(string value)
    {
        if (_suppressSync) return;
        var upper = (value ?? string.Empty).ToUpperInvariant();
        if (!string.Equals(value, upper, StringComparison.Ordinal)) { Name = upper; return; }
        RegenerateHeader();
    }

    partial void OnDeclarationChanged(string value)
    {
        if (_suppressSync) return;
        _suppressSync = true;
        try
        {
            var (kind, name) = ProcedureBodySplitter.ParseSubprogram(value);
            if (!string.IsNullOrEmpty(name)) Name = name;
            if (!string.IsNullOrEmpty(kind)) Kind = kind;
        }
        finally { _suppressSync = false; }
    }

    private void RegenerateHeader()
    {
        var rebuilt = ProcedureBodySplitter.RewriteSubprogramName(Declaration, Name);
        if (rebuilt is null || string.Equals(rebuilt, Declaration, StringComparison.Ordinal)) return;
        _suppressSync = true;
        try { Declaration = rebuilt; }
        finally { _suppressSync = false; }
    }

    public ProcedureSubprogram ToSubprogram() => new()
    {
        Name = (Name ?? string.Empty).Trim(),
        Kind = string.IsNullOrEmpty(Kind) ? "PROCEDURE" : Kind,
        Declaration = (Declaration ?? string.Empty).Trim(),
    };

    public static ProcedureSubprogramRowViewModel From(ProcedureSubprogram s)
    {
        var row = new ProcedureSubprogramRowViewModel();
        row._suppressSync = true;
        row.Name = s.Name;
        row.Kind = s.Kind;
        row.Declaration = s.Declaration;
        row._suppressSync = false;
        return row;
    }
}
