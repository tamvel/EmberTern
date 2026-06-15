using System;
using System.Collections.Generic;
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

public sealed class DependencyGroupNode
{
    public string ObjectType { get; init; } = string.Empty;
    public IReadOnlyList<DependencyLeafNode> Children { get; init; } = Array.Empty<DependencyLeafNode>();
    public string Icon { get; init; } = string.Empty;
    public string IconResourceKey { get; init; } = string.Empty;
    public string IconGeometryKey { get; init; } = string.Empty;

    public int Count => Children.Count;
    public string Header => $"{ObjectType} ({Count})";
}
