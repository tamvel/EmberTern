using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

public class ConstraintNamingTests
{
    [Fact]
    public void NoCollision_ReturnsBaseNameUnchanged()
    {
        Assert.Equal("IDX_ORDERS", ConstraintNaming.MakeUnique("IDX_ORDERS", new[] { "PK_ORDERS", "FK_ORDERS_CUST" }));
    }

    [Fact]
    public void Collision_InsertsCounterAfterLeadingLetters_IBExpertStyle()
    {
        // IDX_ORDERS taken → IDX1_ORDERS; both taken → IDX2_ORDERS.
        Assert.Equal("IDX1_ORDERS", ConstraintNaming.MakeUnique("IDX_ORDERS", new[] { "IDX_ORDERS" }));
        Assert.Equal("IDX2_ORDERS", ConstraintNaming.MakeUnique("IDX_ORDERS", new[] { "IDX_ORDERS", "IDX1_ORDERS" }));
    }

    [Fact]
    public void Collision_FindsLowestFreeNumber_SkippingGaps()
    {
        // 1 is free even though 2 is taken.
        Assert.Equal("IDX1_ORDERS", ConstraintNaming.MakeUnique("IDX_ORDERS", new[] { "IDX_ORDERS", "IDX2_ORDERS" }));
    }

    [Fact]
    public void Comparison_IsCaseInsensitive()
    {
        // Firebird folds unquoted identifiers — a lower-case existing name still collides.
        Assert.Equal("UNQ1_ORDERS", ConstraintNaming.MakeUnique("UNQ_ORDERS", new[] { "unq_orders" }));
    }

    [Theory]
    [InlineData("FK_A_B", "FK1_A_B")]     // FK's two-part base still numbers after "FK"
    [InlineData("CHK_T", "CHK1_T")]
    [InlineData("PK_T", "PK1_T")]
    public void WorksForEveryPrefix_OneMechanism(string baseName, string expected)
    {
        Assert.Equal(expected, ConstraintNaming.MakeUnique(baseName, new[] { baseName }));
    }

    [Fact]
    public void NullOrEmptyExisting_IsSafe()
    {
        Assert.Equal("IDX_T", ConstraintNaming.MakeUnique("IDX_T", null));
        Assert.Equal("IDX_T", ConstraintNaming.MakeUnique("IDX_T", System.Array.Empty<string>()));
    }

    [Fact]
    public void EmptyBase_ReturnedUnchanged()
    {
        Assert.Equal(string.Empty, ConstraintNaming.MakeUnique("", new[] { "X" }));
    }
}
