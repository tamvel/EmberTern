using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using Xunit;

namespace EmberTern.Tests;

public class TransactionProfileCatalogTests
{
    [Fact]
    public void All_ContainsTheFourProfilesInIbExpertOrder()
    {
        Assert.Collection(
            TransactionProfileCatalog.All,
            o => Assert.Equal(TransactionProfile.ReadCommitted, o.Value),
            o => Assert.Equal(TransactionProfile.Snapshot, o.Value),
            o => Assert.Equal(TransactionProfile.ReadOnlyTableStability, o.Value),
            o => Assert.Equal(TransactionProfile.ReadWriteTableStability, o.Value));
    }

    [Fact]
    public void OnlyTableStabilityProfiles_CarryTheConsistencyWarning()
    {
        Assert.False(TransactionProfileCatalog.For(TransactionProfile.ReadCommitted).IsConsistencyWarning);
        Assert.False(TransactionProfileCatalog.For(TransactionProfile.Snapshot).IsConsistencyWarning);
        Assert.True(TransactionProfileCatalog.For(TransactionProfile.ReadOnlyTableStability).IsConsistencyWarning);
        Assert.True(TransactionProfileCatalog.For(TransactionProfile.ReadWriteTableStability).IsConsistencyWarning);
    }

    [Fact]
    public void LabelFor_ReturnsTheIbExpertLabel()
    {
        Assert.Equal("Read Committed", TransactionProfileCatalog.LabelFor(TransactionProfile.ReadCommitted));
        Assert.Equal("Read Write Table Stability", TransactionProfileCatalog.LabelFor(TransactionProfile.ReadWriteTableStability));
    }

    [Fact]
    public void For_UnknownFallsBackToFirst()
    {
        // Defensive: out-of-range cast still yields a usable option.
        Assert.Equal(TransactionProfile.ReadCommitted, TransactionProfileCatalog.For((TransactionProfile)999).Value);
    }
}
