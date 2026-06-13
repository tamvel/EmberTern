using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Connections;

namespace EmberTern.App.ViewModels;

/// <summary>
/// UI-side catalog of the transaction profiles: the picker items (label +
/// description + consistency-warning flag) and label lookup. Core holds the enum;
/// the display strings live here so Core stays free of UI concerns.
/// </summary>
public sealed record TransactionProfileOption(
    TransactionProfile Value,
    string Label,
    string Description,
    bool IsConsistencyWarning);

public static class TransactionProfileCatalog
{
    public static IReadOnlyList<TransactionProfileOption> All { get; } = new[]
    {
        new TransactionProfileOption(
            TransactionProfile.ReadCommitted,
            UiStrings.TransactionProfileReadCommitted,
            UiStrings.TransactionProfileReadCommittedDesc,
            IsConsistencyWarning: false),
        new TransactionProfileOption(
            TransactionProfile.Snapshot,
            UiStrings.TransactionProfileSnapshot,
            UiStrings.TransactionProfileSnapshotDesc,
            IsConsistencyWarning: false),
        new TransactionProfileOption(
            TransactionProfile.ReadOnlyTableStability,
            UiStrings.TransactionProfileReadOnlyTableStability,
            UiStrings.TransactionProfileReadOnlyTableStabilityDesc,
            IsConsistencyWarning: true),
        new TransactionProfileOption(
            TransactionProfile.ReadWriteTableStability,
            UiStrings.TransactionProfileReadWriteTableStability,
            UiStrings.TransactionProfileReadWriteTableStabilityDesc,
            IsConsistencyWarning: true),
    };

    public static TransactionProfileOption For(TransactionProfile value)
        => All.FirstOrDefault(o => o.Value == value) ?? All[0];

    public static string LabelFor(TransactionProfile value) => For(value).Label;
}
