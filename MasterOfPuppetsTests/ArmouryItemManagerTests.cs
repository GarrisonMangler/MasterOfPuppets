using MasterOfPuppets;

using Xunit;

public sealed class ArmouryItemManagerTests {
    [Theory]
    [InlineData(true, true, EquipmentSlot.RightRing)]
    [InlineData(true, false, EquipmentSlot.RightRing)]
    [InlineData(false, true, EquipmentSlot.LeftRing)]
    public void SelectRingDestination_UsesFirstAvailableSlot(
        bool rightEmpty,
        bool leftEmpty,
        EquipmentSlot expected) {
        Assert.Equal(expected, ArmouryItemManager.SelectRingDestination(rightEmpty, leftEmpty));
    }

    [Fact]
    public void SelectRingDestination_RejectsWhenBothSlotsAreOccupied() {
        Assert.Null(ArmouryItemManager.SelectRingDestination(false, false));
    }

    [Theory]
    [InlineData(new uint[] { })]
    [InlineData(new uint[] { 100 })]
    [InlineData(new uint[] { 100, 100 })]
    public void HasAmbiguousItemIds_AllowsZeroOrOneDistinctItem(uint[] itemIds) {
        Assert.False(ArmouryItemManager.HasAmbiguousItemIds(itemIds));
    }

    [Fact]
    public void HasAmbiguousItemIds_RejectsDifferentItemsWithTheSameName() {
        Assert.True(ArmouryItemManager.HasAmbiguousItemIds([100, 200, 100]));
    }
}
