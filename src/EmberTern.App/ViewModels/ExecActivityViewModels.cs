using System.Collections.Generic;
using EmberTern.App.Localization;
using EmberTern.Core.Query;

namespace EmberTern.App.ViewModels;

/// <summary>
/// One table's row in the expanded exec-info body — the table name plus the changes written to it.
///
/// <para>⭐ <b>Why the card stopped binding Core records directly (etap C6).</b> The template used to be
/// keyed on <see cref="TableChange"/>'s three subtypes and bound <c>Count</c> and <c>Verb</c> as two
/// separate, differently coloured text blocks. That is English word order written into the LAYOUT: Polish
/// puts the count between the verb and the noun, so no translation of "inserted" could have produced a
/// correct line. The row now carries ONE localized sentence, split around its number so the number keeps its
/// colour wherever the language puts it.</para>
///
/// <para>⭐ These are plain projections that resolve on READ, exactly like <see cref="SessionWarningViewModel"/>:
/// no cached text, no <c>INotifyPropertyChanged</c>, no per-row subscription to leak. A language change
/// rebuilds the collection, which replaces the bound objects — the same answer the Session Manager reached,
/// and the reason a binding beats a subscription throughout this stage.</para>
/// </summary>
public sealed class ExecActivityLineViewModel
{
    public ExecActivityLineViewModel(TableActivityLine line)
    {
        Table = line.Table;
        var changes = new List<ExecActivityChangeViewModel>(line.Changes.Count);
        foreach (var change in line.Changes)
        {
            changes.Add(new ExecActivityChangeViewModel(change));
        }

        Changes = changes;
    }

    public string Table { get; }

    public IReadOnlyList<ExecActivityChangeViewModel> Changes { get; }
}

/// <summary>
/// One "14 inserted" line inside a table's card: an icon, a kind colour, and the sentence split around its
/// count so the count can be accented.
/// </summary>
public sealed class ExecActivityChangeViewModel
{
    private readonly TableChange _change;

    public ExecActivityChangeViewModel(TableChange change)
    {
        _change = change;
    }

    /// <summary>Glyph for the change kind, resolved via <c>IconGeometryConverter</c>.</summary>
    public string IconGeometryKey => _change switch
    {
        InsertChange => "Icon.Plus",
        UpdateChange => "Icon.Pencil",
        _ => "Icon.Trash",
    };

    /// <summary>Theme brush key for the icon and the count, resolved via <c>IconBrushConverter</c>.</summary>
    /// <remarks>⚠ The VM holds a KEY, never a brush — architecture rule #1. Unchanged from the template it
    /// replaces, which named the same three resources inline.</remarks>
    public string IconResourceKey => _change switch
    {
        InsertChange => "SuccessIconBrush",
        UpdateChange => "WarningIconBrush",
        _ => "DangerIconBrush",
    };

    /// <summary>Whatever the sentence says before the count — empty in English, a verb in Polish.</summary>
    public string Before => Parts.Before;

    /// <summary>The count itself, drawn in <see cref="IconResourceKey"/>.</summary>
    public string Value => Parts.Value;

    /// <summary>Whatever the sentence says after the count.</summary>
    public string After => Parts.After;

    // ⚠ Resolved on every read, never cached: a field would hold whatever language was current when the
    // execution finished and would keep showing it after a switch (localization.md §2.2).
    private (string Before, string Value, string After) Parts
        => Loc.FormatParts(Core.Localization.LocalizableMessage.Of(_change.TermKey, _change.Count));
}
