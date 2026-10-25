using EnshroudedSaveEditor.Core.Files;
using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.Core.Records;

namespace EnshroudedSaveEditor.Core.Editing;

/// <summary>
/// A slot in one of the item containers: an Inventory or Extra slot number, or for <see cref="ContainerKind.Equipped"/>
/// a position 0–15 (<see cref="InventoryLayout.EquipPositions"/>), not the record's slot byte.
/// </summary>
public readonly record struct ItemRef(ContainerKind Kind, byte Slot);

/// <summary>
/// Edits the item containers of the session's <c>player_character</c>. Every edit is checked in full before
/// anything changes, and every edit it accepts leaves records that <see cref="PlayerCharacter.ToBytes"/> can write.
/// A ghost record (count 0, no fields) counts as an empty slot.
/// <list type="bullet">
/// <item>Count, level, rarity and durability edits keep the record's size, so they work on any located record,
/// Equipped ones included.</item>
/// <item>Adding, moving and removing need <see cref="InventoryStatus.Full"/>. New items go into the backpack
/// (Inventory, or Extra within the equipped backpack's extension), never straight into Equipped.</item>
/// <item>An item swaps with the Equipped item in a position it fits (<see cref="InventoryLayout.Fits"/>), which keeps
/// the Equipped mask and record count (T12-R3). Equipping into an empty position and unequipping would change them,
/// and when the game changes the header's byte 2 alongside isn't known, so they are refused.</item>
/// </list>
/// </summary>
public sealed class InventoryEditor(SaveSession s, IItemCatalog items)
{
    /// <summary>Why adding, moving and removing are refused for an inventory that isn't <see cref="InventoryStatus.Full"/>.</summary>
    public const string NeedsFull = "Adding and moving items isn't available for this character yet";

    /// <summary>Why an add is refused when the backpack grid has no free slot.</summary>
    public const string NoRoom = "No room in the backpack";

    /// <summary>Why every inventory edit is refused when the inventory is <see cref="InventoryStatus.NotFound"/>.</summary>
    public const string Unread = "Inventory couldn't be read for this character.";

    const string EquipLocked = "Equipping into an empty slot and unequipping aren't available yet: swap with an item that fits instead";
    const string EmptySlot = "There's no item in that slot.";
    const int BackpackPosition = 15;

    /// <summary>The item in <paramref name="r"/>, or null when the slot is empty or holds a ghost.</summary>
    public ItemRecord? At(ItemRef r) => Find(r) is { IsGhost: false } rec ? rec : null;

    /// <summary>
    /// The Extra slots the equipped backpack gives (<see cref="InventoryLayout.ExtraCapacity"/>): 0 with no backpack in
    /// position 15, or one the game data doesn't list. The backpack grid is <see cref="InventoryLayout.BackpackGrid"/> of it.
    /// </summary>
    public int ExtraCapacity => CapacityFor(At(new ItemRef(ContainerKind.Equipped, BackpackPosition)));

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

    /// <summary>Removes the record. <see cref="InventoryStatus.Full"/> only; never from Equipped (that unequips).</summary>
    public void Remove(ItemRef r)
    {
        EnsureFull();
        if (r.Kind == ContainerKind.Equipped) throw new EditRejectedException(EquipLocked);
        var rec = At(r) ?? throw new EditRejectedException(EmptySlot);
        s.Player.Container(r.Kind)!.Records.Remove(rec);
        s.MarkChanged();
    }

    /// <summary>
    /// Moves an item to another slot, swapping with the item there; a ghost in an Inventory or Extra target is
    /// replaced. <see cref="InventoryStatus.Full"/> only. With Equipped on either side it must be a swap of items that
    /// each fit their new position; a backpack swap must leave every Extra record inside the new backpack's extension.
    /// </summary>
    public void Move(ItemRef from, ItemRef to)
    {
        EnsureFull();
        var src = At(from) ?? throw new EditRejectedException(EmptySlot);
        if (from == to) throw new EditRejectedException("The item is already in that slot.");
        if (from.Kind == ContainerKind.Equipped || to.Kind == ContainerKind.Equipped)
        {
            SwapEquipped(from, to, src);
            return;
        }

        EnsureSlotExists(to);
        var dst = Find(to);
        if (dst is { IsGhost: false }) EnsureSlotExists(from);   // the swapped item lands there
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
    /// Adds a stack to <paramref name="target"/>, an empty or ghost Inventory slot or Extra slot within
    /// <see cref="ExtraCapacity"/>, or else to the first such slot of the backpack grid (T12-R2: Inventory 0–15, then
    /// 32–39, then Extra from 0; the action bar, Inventory 16–31, only when targeted). A ghost there is replaced.
    /// <see cref="InventoryStatus.Full"/> only. Fields come from <see cref="NewItemFields.Create"/>; a level or rarity
    /// the new record has no field for is rejected.
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
            if (t.Kind == ContainerKind.Equipped) throw new EditRejectedException("New items go into the backpack.");
            EnsureSlotExists(t);
            if (At(t) is not null) throw new EditRejectedException("That slot is taken.");
            at = t;
        }
        else
        {
            at = FirstFree() ?? throw new EditRejectedException(NoRoom);
        }

        var list = s.Player.Container(at.Kind)!.Records;
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

    /// <summary>The first empty or ghost slot of the backpack grid (<see cref="InventoryLayout.BackpackGrid"/>).</summary>
    ItemRef? FirstFree()
    {
        foreach (var r in InventoryLayout.BackpackGrid(ExtraCapacity))
            if (At(r) is null) return r;
        return null;
    }

    /// <summary>
    /// T12-R3: swaps <paramref name="src"/> with the item in <paramref name="to"/>, one side or both in Equipped. The
    /// Equipped records keep their count, their mask-bit order and slot bytes 0…n−1: the incoming item takes the
    /// outgoing one's place in the list.
    /// </summary>
    void SwapEquipped(ItemRef from, ItemRef to, ItemRecord src)
    {
        // An empty or ghost target means equipping into an empty position or unequipping: both change the mask.
        var dst = At(to) ?? throw new EditRejectedException(EquipLocked);
        if (to.Kind == ContainerKind.Equipped) EnsureFits(src, to.Slot);
        if (from.Kind == ContainerKind.Equipped) EnsureFits(dst, from.Slot);
        if (from.Kind != ContainerKind.Equipped) EnsureSlotExists(from);
        if (to.Kind != ContainerKind.Equipped) EnsureSlotExists(to);

        var eq = s.Player.Container(ContainerKind.Equipped)!.Records;
        var backpack = eq.Count > 0 && EquippedIndex(BackpackPosition) is { } bi ? eq[bi] : null;
        if (src == backpack || dst == backpack)
        {
            var incoming = src == backpack ? dst : src;          // the backpack that ends up equipped
            var outgoing = src == backpack ? src : dst;
            var cap = CapacityFor(incoming);
            var other = from.Kind == ContainerKind.Equipped ? to : from;
            // Extra after the swap: the incoming backpack leaves it, the outgoing one arrives when the other side is Extra.
            // Ghosts count too: what the game does with a record past the extension isn't known.
            var extraSlots = s.Player.Container(ContainerKind.Extra)!.Records
                .Where(r => r != incoming).Select(r => (int)r.Slot)
                .Concat(other.Kind == ContainerKind.Extra ? [other.Slot] : []);
            if (extraSlots.Any(slot => slot >= cap))
                throw new EditRejectedException($"{NameOf(incoming)} gives {cap} backpack extension slots: move the items out of the others first.");
        }

        if (from.Kind == ContainerKind.Equipped && to.Kind == ContainerKind.Equipped)
        {
            int i = EquippedIndex(from.Slot)!.Value, j = EquippedIndex(to.Slot)!.Value;
            (eq[i], eq[j]) = (eq[j], eq[i]);
            eq[i].Slot = (byte)i;
            eq[j].Slot = (byte)j;
        }
        else
        {
            var (eqRef, otherRef, incoming, outgoing) = to.Kind == ContainerKind.Equipped ? (to, from, src, dst) : (from, to, dst, src);
            var list = s.Player.Container(otherRef.Kind)!.Records;
            var i = EquippedIndex(eqRef.Slot)!.Value;
            list[list.IndexOf(incoming)] = outgoing;
            outgoing.Slot = otherRef.Slot;
            eq[i] = incoming;
            incoming.Slot = (byte)i;
        }
        s.MarkChanged();
    }

    void EnsureFits(ItemRecord r, byte position)
    {
        if (items.Find(r.ItemId) is not { } info || !InventoryLayout.Fits(info, position))
            throw new EditRejectedException($"{NameOf(r)} doesn't go in the {InventoryLayout.EquipPositions[position].Label} slot.");
    }

    int CapacityFor(ItemRecord? backpack) =>
        backpack is null ? 0 : InventoryLayout.ExtraCapacity(items.Find(backpack.ItemId));

    /// <summary>The index of the Equipped record in <paramref name="position"/>, or null when it is empty or unknown.</summary>
    int? EquippedIndex(int position)
    {
        if (s.Player.EquippedPositions is not { } positions) return null;
        for (var i = 0; i < positions.Count; i++)
            if (positions[i] == position) return i;
        return null;
    }

    /// <summary>The record in <paramref name="r"/>'s slot or position, ghosts included.</summary>
    ItemRecord? Find(ItemRef r)
    {
        if (s.Player.Container(r.Kind) is not { } c) return null;
        if (r.Kind != ContainerKind.Equipped) return c.Records.Find(x => x.Slot == r.Slot);
        return EquippedIndex(r.Slot) is { } i && i < c.Records.Count ? c.Records[i] : null;
    }

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

    /// <summary>Inventory slots 0–39; Extra slots within <see cref="ExtraCapacity"/>.</summary>
    void EnsureSlotExists(ItemRef r)
    {
        var (capacity, name) = r.Kind switch
        {
            ContainerKind.Inventory => (InventoryLayout.InventorySlots, "backpack"),
            ContainerKind.Extra => (ExtraCapacity, "backpack extension"),
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
