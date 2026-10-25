using EnshroudedSaveEditor.Core.Files;
using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.Core.Records;

namespace EnshroudedSaveEditor.Core.Editing;

/// <summary>A slot in one of the item containers.</summary>
public readonly record struct ItemRef(ContainerKind Kind, byte Slot);

/// <summary>
/// Edits the item containers of the session's <c>player_character</c>. Every edit is checked in full before
/// anything changes, and every edit it accepts leaves records that <see cref="PlayerCharacter.ToBytes"/> can write.
/// A ghost record (count 0, no fields) counts as an empty slot.
/// <list type="bullet">
/// <item>Count, level, rarity and durability edits keep the record's size, so they work on any located record,
/// Equipped ones included.</item>
/// <item>Adding, moving and removing need <see cref="InventoryStatus.Full"/>, and never change the Equipped
/// records: the meaning of the Equipped slot mask isn't confirmed (<see cref="InventoryLayout.EquipMapKnown"/>).</item>
/// </list>
/// </summary>
public sealed class InventoryEditor(SaveSession s, IItemCatalog items)
{
    const string NeedsFull = "Adding and moving items isn't available for this character yet";
    const string NoRoom = "No room in the backpack";
    const string Unread = "Inventory couldn't be read for this character.";
    const string EquipLocked = "Equipping and unequipping items isn't available yet";
    const string EmptySlot = "There's no item in that slot.";

    /// <summary>The item in <paramref name="r"/>, or null when the slot is empty or holds a ghost.</summary>
    public ItemRecord? At(ItemRef r) => Find(r) is { IsGhost: false } rec ? rec : null;

    /// <summary>1 to the item's max stack; 1 to 65535 for an item missing from the game data.</summary>
    public void SetCount(ItemRef r, int count)
    {
        var rec = Item(r);
        var max = items.Find(rec.ItemId) is { } info ? MaxStack(info) : ushort.MaxValue;
        if (count < 1 || count > max) throw new EditRejectedException($"The count must be between 1 and {max}.");
        if (count == rec.Count) throw new EditRejectedException($"Already {count}");
        rec.Count = (ushort)count;
        s.MarkChanged();
    }

    /// <summary>1 to <see cref="InventoryLayout.MaxItemLevel"/> for any item; the record must have a level.</summary>
    public void SetItemLevel(ItemRef r, int level)
    {
        var rec = Item(r);
        if (rec.Level is not { } current) throw new EditRejectedException($"{NameOf(rec)} has no item level.");
        EnsureLevelInRange(level);
        if (level == current) throw new EditRejectedException($"Already level {level}");
        rec.Level = (byte)level;
        s.MarkChanged();
    }

    /// <summary>The record must have a rarity; its extra byte is kept.</summary>
    public void SetRarity(ItemRef r, Rarity v)
    {
        var rec = Item(r);
        if (rec.Rarity is not { } current) throw new EditRejectedException($"{NameOf(rec)} has no rarity.");
        if (!Enum.IsDefined(v)) throw new EditRejectedException("Unknown rarity.");
        if ((byte)v == current.Rarity) throw new EditRejectedException($"Already {v}");
        rec.Rarity = ((byte)v, current.Extra);
        s.MarkChanged();
    }

    /// <summary>The record must have durability; 1 ≤ max and current ≤ max.</summary>
    public void SetDurability(ItemRef r, uint current, uint max)
    {
        var rec = Item(r);
        if (rec.Durability is not { } old) throw new EditRejectedException($"{NameOf(rec)} has no durability.");
        if (max == 0) throw new EditRejectedException("The maximum durability must be at least 1.");
        if (current > max) throw new EditRejectedException("The durability can't be more than its maximum.");
        if (old == (current, max)) throw new EditRejectedException($"Already {current}/{max}");
        rec.Durability = (current, max);
        s.MarkChanged();
    }

    /// <summary>Removes the record. <see cref="InventoryStatus.Full"/> only; never from Equipped.</summary>
    public void Remove(ItemRef r)
    {
        EnsureFull();
        // Removing would change the Equipped record count, which the slot mask holds fixed (ToBytes refuses it).
        if (r.Kind == ContainerKind.Equipped) throw new EditRejectedException(EquipLocked);
        var rec = At(r) ?? throw new EditRejectedException(EmptySlot);
        s.Player.Container(r.Kind)!.Records.Remove(rec);
        s.MarkChanged();
    }

    /// <summary>
    /// Moves an item to another Inventory or Extra slot, swapping with the item there. A ghost in the target slot
    /// is replaced. <see cref="InventoryStatus.Full"/> only. Moves into, out of or within Equipped need the
    /// confirmed slot map, and items that fit their new slots.
    /// </summary>
    public void Move(ItemRef from, ItemRef to)
    {
        EnsureFull();
        var src = At(from) ?? throw new EditRejectedException(EmptySlot);
        if (from == to) throw new EditRejectedException("The item is already in that slot.");
        var dst = Find(to);
        if (from.Kind == ContainerKind.Equipped || to.Kind == ContainerKind.Equipped)
        {
            if (!EquipSwapAllowed(src, dst, from, to)) throw new EditRejectedException(EquipLocked);
        }
        else
        {
            EnsureSlotExists(to);
        }

        var srcList = s.Player.Container(from.Kind)!.Records;
        var dstList = s.Player.Container(to.Kind)!.Records;
        if (dst is null && from.Kind != to.Kind) EnsureRoom(dstList);
        srcList.Remove(src);
        if (dst is not null) dstList.Remove(dst);
        if (dst is { IsGhost: false })
        {
            dst.Slot = from.Slot;
            Insert(srcList, dst);
        }
        src.Slot = to.Slot;
        Insert(dstList, src);
        s.MarkChanged();
    }

    /// <summary>
    /// Adds a stack to <paramref name="target"/>, an empty or ghost Inventory slot, or else to the first such slot
    /// from 0. A ghost there is replaced. <see cref="InventoryStatus.Full"/> only. Fields come from
    /// <see cref="NewItemFields.Create"/>; a level or rarity the new record has no field for is rejected.
    /// </summary>
    public ItemRef Add(ItemInfo info, int count, int? level, Rarity? rarity, ItemRef? target)
    {
        ArgumentNullException.ThrowIfNull(info);
        EnsureFull();
        var maxStack = MaxStack(info);
        if (count < 1 || count > maxStack) throw new EditRejectedException($"The count must be between 1 and {maxStack}.");
        if (level is { } l) EnsureLevelInRange(l);
        if (rarity is { } v && !Enum.IsDefined(v)) throw new EditRejectedException("Unknown rarity.");

        ItemRef at;
        if (target is { } t)
        {
            if (t.Kind != ContainerKind.Inventory) throw new EditRejectedException("New items go into the backpack.");
            EnsureSlotExists(t);
            if (At(t) is not null) throw new EditRejectedException("That slot is taken.");
            at = t;
        }
        else
        {
            at = FirstFree() ?? throw new EditRejectedException(NoRoom);
        }

        var list = s.Player.Container(ContainerKind.Inventory)!.Records;
        var ghost = Find(at);
        if (ghost is null) EnsureRoom(list);
        var rec = NewItemFields.Create(info, at.Slot, count, level, rarity, s.Player, items);
        if (level is not null && rec.Level is null) throw new EditRejectedException($"{info.Name} has no item level.");
        if (rarity is not null && rec.Rarity is null) throw new EditRejectedException($"{info.Name} has no rarity.");

        if (ghost is not null) list[list.IndexOf(ghost)] = rec;
        else Insert(list, rec);
        s.MarkChanged();
        return at;
    }

    /// <summary>The record in <paramref name="r"/>'s slot, ghosts included.</summary>
    ItemRecord? Find(ItemRef r) => s.Player.Container(r.Kind)?.Records.Find(x => x.Slot == r.Slot);

    void EnsureWritable()
    {
        if (s.ReadOnly) throw new EditRejectedException($"This save is read-only: {s.ReadOnlyReason}");
    }

    void EnsureFull()
    {
        EnsureWritable();
        if (s.Player.InventoryStatus != InventoryStatus.Full) throw new EditRejectedException(NeedsFull);
    }

    /// <summary>The item to edit in place, for any located inventory.</summary>
    ItemRecord Item(ItemRef r)
    {
        EnsureWritable();
        if (s.Player.InventoryStatus == InventoryStatus.NotFound) throw new EditRejectedException(Unread);
        return At(r) ?? throw new EditRejectedException(EmptySlot);
    }

    /// <summary>
    /// Until the slot map is confirmed in-game, nothing touches Equipped (T3-R3). Even then only a swap can pass:
    /// the Equipped record count is fixed by its slot mask (ToBytes), and each item must fit its new slot.
    /// </summary>
    bool EquipSwapAllowed(ItemRecord src, ItemRecord? dst, ItemRef from, ItemRef to) =>
        InventoryLayout.EquipMapKnown
        && dst is { IsGhost: false }
        && (to.Kind != ContainerKind.Equipped || Fits(src, to.Slot))
        && (from.Kind != ContainerKind.Equipped || Fits(dst, from.Slot));

    bool Fits(ItemRecord r, byte slot) =>
        items.Find(r.ItemId) is { } info
        && InventoryLayout.EquippedSlotsFor.TryGetValue(info.Slot, out var slots) && slots.Contains(slot);

    ItemRef? FirstFree()
    {
        for (var i = 0; i < InventoryLayout.InventorySlots; i++)
        {
            var r = new ItemRef(ContainerKind.Inventory, (byte)i);
            if (At(r) is null) return r;
        }
        return null;
    }

    static void EnsureSlotExists(ItemRef r)
    {
        var (capacity, name) = r.Kind switch
        {
            ContainerKind.Inventory => (InventoryLayout.InventorySlots, "backpack"),
            ContainerKind.Extra => (InventoryLayout.ExtraSlots, "extra"),
            _ => (0, r.Kind.ToString()),
        };
        if (r.Slot >= capacity) throw new EditRejectedException($"There's no {name} slot {r.Slot}.");
    }

    /// <summary>A list holds at most 255 records: its count is one byte.</summary>
    static void EnsureRoom(List<ItemRecord> list)
    {
        if (list.Count >= byte.MaxValue) throw new EditRejectedException(NoRoom);
    }

    /// <summary>Inserts <paramref name="rec"/> in slot order.</summary>
    static void Insert(List<ItemRecord> list, ItemRecord rec)
    {
        var i = list.FindIndex(x => x.Slot > rec.Slot);
        list.Insert(i < 0 ? list.Count : i, rec);
    }

    string NameOf(ItemRecord r) => items.Find(r.ItemId)?.Name ?? "This item";

    static int MaxStack(ItemInfo info) => Math.Clamp(info.MaxStack, 1, ushort.MaxValue);

    /// <summary>T10-R1: 1 to <see cref="InventoryLayout.MaxItemLevel"/> for every item, whatever its reference range.</summary>
    static void EnsureLevelInRange(int level)
    {
        if (level < 1 || level > InventoryLayout.MaxItemLevel)
            throw new EditRejectedException($"The level must be between 1 and {InventoryLayout.MaxItemLevel}.");
    }
}
