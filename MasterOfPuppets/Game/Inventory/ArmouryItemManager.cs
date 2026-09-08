using System;
using System.Collections.Generic;
using System.Linq;

using FFXIVClientStructs.FFXIV.Client.Game;

namespace MasterOfPuppets;

public sealed record ArmouryItemResolution(
    bool Success,
    string Status,
    string Message,
    InventoryDescriptor? Source,
    EquipmentSlot? EquipmentSlot);

public static unsafe class ArmouryItemManager {
    private static readonly InventoryType[] ArmouryTypes = [
        InventoryType.ArmoryMainHand,
        InventoryType.ArmoryOffHand,
        InventoryType.ArmoryHead,
        InventoryType.ArmoryBody,
        InventoryType.ArmoryHands,
        InventoryType.ArmoryLegs,
        InventoryType.ArmoryFeets,
        InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck,
        InventoryType.ArmoryWrist,
        InventoryType.ArmoryRings,
    ];

    public static ArmouryItemResolution EquipByName(string itemName) {
        if (!DalamudApi.Framework.IsInFrameworkUpdateThread)
            return Failure("armoury equipment must be requested on the framework thread");

        var name = itemName?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return Failure("item name is required");

        var matches = FindMatches(name);
        if (matches.Count == 0)
            return Failure($"armoury item named '{name}' does not exist");
        if (HasAmbiguousItemIds(matches.Select(match => match.ItemId)))
            return Failure($"armoury item name '{name}' matches multiple items");

        var inventory = InventoryManager.Instance();
        if (inventory == null)
            return Failure("inventory manager is unavailable");

        var match = matches[0];
        var destination = match.EquipmentSlot;
        if (destination is EquipmentSlot.RightRing or EquipmentSlot.LeftRing) {
            var equipped = inventory->GetInventoryContainer(InventoryType.EquippedItems);
            if (equipped == null)
                return Failure("equipped-item inventory is unavailable");

            var rightRing = equipped->GetInventorySlot((int)EquipmentSlot.RightRing);
            var leftRing = equipped->GetInventorySlot((int)EquipmentSlot.LeftRing);
            if (rightRing == null || leftRing == null)
                return Failure("equipped ring slots are unavailable");

            var ringDestination = SelectRingDestination(rightRing->IsEmpty(), leftRing->IsEmpty());
            if (ringDestination == null)
                return Failure("both ring slots are occupied; unequip a ring before equipping by name");
            destination = ringDestination.Value;
        }

        inventory->MoveItemSlot(
            match.Source.Type,
            (ushort)match.Source.Slot,
            InventoryType.EquippedItems,
            (ushort)destination,
            true);

        return new(true, "submitted", $"submitted equip request for '{match.Name}'", match.Source, destination);
    }

    internal static EquipmentSlot? SelectRingDestination(bool rightEmpty, bool leftEmpty) =>
        rightEmpty ? EquipmentSlot.RightRing : leftEmpty ? EquipmentSlot.LeftRing : null;

    internal static bool HasAmbiguousItemIds(IEnumerable<uint> itemIds) =>
        itemIds.Distinct().Take(2).Count() > 1;

    private static List<ArmouryMatch> FindMatches(string name) {
        var matches = new List<ArmouryMatch>();
        var inventory = InventoryManager.Instance();
        if (inventory == null)
            return matches;

        foreach (var armouryType in ArmouryTypes) {
            var container = inventory->GetInventoryContainer(armouryType);
            if (container == null)
                continue;

            for (var slot = 0; slot < container->Size; slot++) {
                var item = container->GetInventorySlot(slot);
                if (item == null || item->IsEmpty())
                    continue;

                var itemId = item->GetItemId() % 1_000_000u;
                var info = ItemHelper.GetItem(itemId);
                if (info == null || !info.Value.Name.ToString().Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;

                var equipmentSlot = GetEquipmentSlot((EquipSlotCategoryEnum)info.Value.EquipSlotCategory.RowId);
                if (equipmentSlot == null)
                    continue;

                matches.Add(new(
                    info.Value.Name.ToString(),
                    itemId,
                    new InventoryDescriptor(container->Type, slot),
                    equipmentSlot.Value));
            }
        }

        return matches;
    }

    private static EquipmentSlot? GetEquipmentSlot(EquipSlotCategoryEnum category) => category switch {
        EquipSlotCategoryEnum.WeaponTwoHand or EquipSlotCategoryEnum.WeaponMainHand => EquipmentSlot.MainHand,
        EquipSlotCategoryEnum.OffHand => EquipmentSlot.OffHand,
        EquipSlotCategoryEnum.Head => EquipmentSlot.Head,
        EquipSlotCategoryEnum.Body => EquipmentSlot.Body,
        EquipSlotCategoryEnum.Gloves => EquipmentSlot.Hands,
        EquipSlotCategoryEnum.Legs => EquipmentSlot.Legs,
        EquipSlotCategoryEnum.Feet => EquipmentSlot.Feet,
        EquipSlotCategoryEnum.Ears => EquipmentSlot.Ears,
        EquipSlotCategoryEnum.Neck => EquipmentSlot.Neck,
        EquipSlotCategoryEnum.Wrists => EquipmentSlot.Wrists,
        EquipSlotCategoryEnum.Ring => EquipmentSlot.RightRing,
        _ => null,
    };

    private static ArmouryItemResolution Failure(string message) =>
        new(false, "rejected", message, null, null);

    private sealed record ArmouryMatch(
        string Name,
        uint ItemId,
        InventoryDescriptor Source,
        EquipmentSlot EquipmentSlot);
}
