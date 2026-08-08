using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Core.Metadata;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Leaf in the dependency tree. Wraps a <see cref="DependencyInfo"/> with the
/// per-kind icon + theme-resource key so the XAML side can use the same
/// IconBrushConverter pipeline as the main metadata tree.
/// </summary>
public sealed class DependencyLeafNode
{
    public DependencyInfo Dependency { get; init; } = new();
    public string Icon { get; init; } = string.Empty;
    public string IconResourceKey { get; init; } = string.Empty;
    public string IconGeometryKey { get; init; } = string.Empty;

    public string ObjectName => Dependency.ObjectName;
    public string? FieldName => Dependency.FieldName;
}

/// <summary>
/// Category node in the dependency tree (np. „Tables (135)").
/// </summary>
/// <remarks>
/// ⭐ <b><see cref="IsExpanded"/> dopisane w M4.2b</b>, gdy drzewo przeszło na <see cref="SidebarFlatController"/> —
/// ten sam mechanizm, którym działa drzewo połączenia w Metadata Explorerze. Kontroler spłaszcza drzewo do listy
/// i obserwuje <c>PropertyChanged</c> węzła, więc rozwinięcie musi być <b>obserwowalną własnością WĘZŁA</b>,
/// a nie stanem trzymanym obok: stan obok kontroler by zignorował, bo nie ma go czym zaobserwować.
/// <para>
/// ⚠ Dlatego klasa dziedziczy po <see cref="ObservableObject"/>. Reszta własności zostaje <c>init</c>-only —
/// węzeł nadal jest niemutowalny co do TREŚCI, zmienny wyłącznie co do stanu ROZWINIĘCIA.
/// </para>
/// </remarks>
public sealed partial class DependencyGroupNode : ObservableObject
{
    public string ObjectType { get; init; } = string.Empty;
    public IReadOnlyList<DependencyLeafNode> Children { get; init; } = Array.Empty<DependencyLeafNode>();
    public string Icon { get; init; } = string.Empty;
    public string IconResourceKey { get; init; } = string.Empty;
    public string IconGeometryKey { get; init; } = string.Empty;

    /// <summary>Stan chevronu. ⚠ Musi być obserwowalny — patrz uwagi klasy.</summary>
    [ObservableProperty]
    private bool _isExpanded;

    public int Count => Children.Count;
    public string Header => $"{ObjectType} ({Count})";
}
