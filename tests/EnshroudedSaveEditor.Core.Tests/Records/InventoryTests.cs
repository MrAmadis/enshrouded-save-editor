using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Records;

namespace EnshroudedSaveEditor.Tests;

public class InventoryTests
{
    static Func<uint, bool> IsItem => Fixtures.ItemIds().Contains;

    const uint CopperOre = 0x2b9d5b6f;

    static byte[] Raw(string file) => KsdFile.Parse(Fixtures.Read(file))["player_character"].GetData();

    static byte[] RawAny(string name) => name.Contains(".backup-")
        ? KsdFile.Parse(File.ReadAllBytes(Path.Combine(Fixtures.RealBackup, name)))["player_character"].GetData()
        : Raw(name);

    static PlayerCharacter Load(string file) => PlayerCharacter.Parse(Raw(file), IsItem);

    static int[] Diff(byte[] a, byte[] b)
    {
        Assert.Equal(a.Length, b.Length);
        return Enumerable.Range(0, a.Length).Where(i => a[i] != b[i]).ToArray();
    }

    public static TheoryData<string> AllPlayerCharacters { get; } = new(
        new[] { "character0.ksd", "character1.ksd", "character2.ksd" }
            .Concat(Enumerable.Range(0, 10).Select(i => $"character2.ksd.backup-{i}")));

    [Fact]
    public void Character2_inventory_is_fully_recognised()
    {
        var pc = Load("character2.ksd");
        Assert.Equal(InventoryStatus.Full, pc.InventoryStatus);
        var eq = pc.Container(ContainerKind.Equipped)!.Records;
        Assert.Equal(11, eq.Count); Assert.Equal((0, 0xe5a9ac59u, (byte)0x11), (eq[0].Slot, eq[0].ItemId, eq[0].Flags)); Assert.Equal(0xde38058eu, eq[10].ItemId);
        var inv = pc.Container(ContainerKind.Inventory)!.Records;
        Assert.Equal(21, inv.Count); Assert.Equal(15, inv[0].Slot); Assert.True(inv[0].IsGhost);          // Bronze Sword ×0
        Assert.Contains(inv, r => r.Slot == 30 && r.ItemId == 0xe988b200u && r.Count == 87);              // Fireball I ×87
        Assert.Contains(inv, r => r.ItemId == 0xe589d7a4u && r.Flags == 0x33 && r.Gem is not null);        // Scorching Wand
        Assert.Empty(pc.Container(ContainerKind.Extra)!.Records);
    }

    // Raised from "at least InPlaceOnly" to Full: the strict layout (docs/research/player-character.md) reads it.
    [Fact]
    public void Character0_equipped_has_16_records()
    {
        var pc = Load("character0.ksd");
        Assert.Equal(InventoryStatus.Full, pc.InventoryStatus);
        var eq = pc.Container(ContainerKind.Equipped)!.Records;
        Assert.Equal(16, eq.Count);
        Assert.Equal((15, 0xfc3d5c4cu), (eq[15].Slot, eq[15].ItemId));                                    // Small Backpack
        Assert.Equal(18, pc.Container(ContainerKind.Inventory)!.Records.Count);
        var extra = Assert.Single(pc.Container(ContainerKind.Extra)!.Records);
        Assert.Equal((2, 0x5c76c274u, true), (extra.Slot, extra.ItemId, extra.IsGhost));                // Wolf Daggers ghost
    }

    // Raised from "at least InPlaceOnly" to Full, as above.
    [Fact]
    public void Character1_copper_arrows_are_found()
    {
        var pc = Load("character1.ksd");
        Assert.Equal(InventoryStatus.Full, pc.InventoryStatus);
        Assert.Contains(pc.Containers.SelectMany(c => c.Records), r => r.ItemId == 0xf12faa48u && r.Count == 88);
        var eq = pc.Container(ContainerKind.Equipped)!.Records;
        Assert.Equal(16, eq.Count);
        Assert.Equal(0x01e879a5u, eq[0].ItemId);  // appearance item missing from itemdb2 (it is in player_knowledge)
        Assert.Equal(38, pc.Container(ContainerKind.Inventory)!.Records.Count);
        Assert.Single(pc.Container(ContainerKind.Extra)!.Records);
    }

    [Theory, MemberData(nameof(AllPlayerCharacters))] // 3 fixtures + 10 backups
    public void Every_player_character_round_trips(string name)
    {
        var data = RawAny(name);
        Assert.Equal(data, PlayerCharacter.Parse(data, IsItem).ToBytes());
    }

    [Theory, MemberData(nameof(AllPlayerCharacters))]
    public void Every_player_character_is_fully_recognised(string name)
    {
        var pc = PlayerCharacter.Parse(RawAny(name), IsItem);
        Assert.Equal(InventoryStatus.Full, pc.InventoryStatus);
        Assert.Equal(new[] { ContainerKind.Equipped, ContainerKind.Inventory, ContainerKind.Extra }, pc.Containers.Select(c => c.Kind));
    }

    static byte[] CaptureRaw(string capture) => KsdFile.Parse(Fixtures.ReadCapture(capture))["player_character"].GetData();

    // Every Lab copy and every autosave the game wrote of them in session A (git-ignored; skipped when missing),
    // including the "Test Skills" captures from 14:11:51 on, whose Equipped mask has holes.
    [Theory, MemberData(nameof(Fixtures.ExperimentCaptures), MemberType = typeof(Fixtures))]
    public void Every_experiment_capture_is_fully_recognised_and_round_trips(string capture)
    {
        var data = CaptureRaw(capture);
        var pc = PlayerCharacter.Parse(data, IsItem);
        Assert.Equal(InventoryStatus.Full, pc.InventoryStatus);
        Assert.Equal(data, pc.ToBytes());
    }

    /// <summary>Offset of the Equipped header: after the skills and the status entries (27 + 30 bytes here).</summary>
    static int EquippedHeaderAt(PlayerCharacter pc, int statusBytes) => PlayerCharacter.LevelOffset + 12 + 5 * pc.Skills.Count + 4 + statusBytes;

    static int EquippedEnd(PlayerCharacter pc, int statusBytes) =>
        EquippedHeaderAt(pc, statusBytes) + 4 + pc.Container(ContainerKind.Equipped)!.Records.Sum(r => r.EncodedLength);

    static byte[] Splice(byte[] d, int at, int remove, params byte[] insert) =>
        d.AsSpan(0, at).ToArray().Concat(insert).Concat(d.AsSpan(at + remove).ToArray()).ToArray();

    // "Test Skills" after the owner took off the shield and the grappling hook (14:11:51): mask ffff → cfff.
    [Fact]
    public void Equipped_mask_holes_are_read_by_position()
    {
        var pc = PlayerCharacter.Parse(CaptureRaw("local/character5/20261025-141151.ksd"), IsItem);
        Assert.Equal(InventoryStatus.Full, pc.InventoryStatus);
        Assert.Equal(0x0002cfffu, pc.EquippedMask);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 14, 15], pc.EquippedPositions!);
        var eq = pc.Container(ContainerKind.Equipped)!.Records;
        Assert.Equal(Enumerable.Range(0, 14), eq.Select(r => (int)r.Slot));    // the slot byte is the record index
        Assert.Equal(0x534ba901u, eq[12].ItemId);                              // Advanced Glider, position 14
        Assert.Equal(0xde38058eu, eq[13].ItemId);                              // Small Backpack, position 15
        Assert.Equal(40, pc.Container(ContainerKind.Inventory)!.Records.Count); // the shield and hook went to the backpack
        Assert.Single(pc.Container(ContainerKind.Extra)!.Records);

        // 14:12:19: all gear off but the glider and the hook (back on): mask e01f.
        var later = PlayerCharacter.Parse(CaptureRaw("local/character5/20261025-141219.ksd"), IsItem);
        Assert.Equal(InventoryStatus.Full, later.InventoryStatus);
        Assert.Equal(0x0002e01fu, later.EquippedMask);
        Assert.Equal([0, 1, 2, 3, 4, 13, 14, 15], later.EquippedPositions!);
        Assert.Equal([0x6aa6a1d4u, 0x534ba901u, 0xde38058eu],                  // Improved Grappling Hook, Advanced Glider, Small Backpack
            later.Container(ContainerKind.Equipped)!.Records.Skip(5).Select(r => r.ItemId));
        Assert.Equal(7, later.Container(ContainerKind.Extra)!.Records.Count);
    }

    [Fact]
    public void Equipped_positions_follow_the_mask()
    {
        Assert.Equal(Enumerable.Range(0, 16), Load("character1.ksd").EquippedPositions!);
        var c2 = Load("character2.ksd");
        Assert.Equal(0x0005ffe0u, c2.EquippedMask);
        Assert.Equal(Enumerable.Range(5, 11), c2.EquippedPositions!);
    }

    // The u32 Equipped header: bits 0–15 are item records, and each set bit 16–18 (header byte 2) adds one u16 after
    // the records, in bit order. What the game did at 14:11:51 (03 → 02, two bytes fewer), replayed on character1.
    [Fact]
    public void Each_header_byte_2_bit_adds_a_u16_after_the_equipped_records()
    {
        var raw = Raw("character1.ksd");
        var pc = PlayerCharacter.Parse(raw, IsItem);
        int header = EquippedHeaderAt(pc, 57), end = EquippedEnd(pc, 57);
        Assert.Equal(new byte[] { 0xff, 0xff, 0x03, 0x00 }, raw[header..(header + 4)]);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x01, 0x00, 0x05 }, raw[end..(end + 5)]);   // bit 16: 0, bit 17: 1, then 05 n2 x n3

        var one = Splice(raw, end, 2);                             // drop bit 16's u16 …
        one[header + 2] = 0x02;                                    // … and the bit
        var a = PlayerCharacter.Parse(one, IsItem);
        Assert.Equal(InventoryStatus.Full, a.InventoryStatus);
        Assert.Equal(0x0002ffffu, a.EquippedMask);
        Assert.Equal(38, a.Container(ContainerKind.Inventory)!.Records.Count);
        Assert.Equal(one, a.ToBytes());

        var three = Splice(raw, end + 4, 0, 0xab, 0xcd);            // add bit 18's u16 after bit 17's
        three[header + 2] = 0x07;
        var b = PlayerCharacter.Parse(three, IsItem);
        Assert.Equal(InventoryStatus.Full, b.InventoryStatus);
        Assert.Equal(16, b.Container(ContainerKind.Equipped)!.Records.Count);
        Assert.Equal(38, b.Container(ContainerKind.Inventory)!.Records.Count);
        Assert.Equal(three, b.ToBytes());

        var stale = (byte[])raw.Clone();                           // bit cleared but its u16 kept: no longer the layout
        stale[header + 2] = 0x02;
        Assert.NotEqual(InventoryStatus.Full, PlayerCharacter.Parse(stale, IsItem).InventoryStatus);
    }

    // Bits 19–31 have never been seen, so their sizes are unknown: the strict layout refuses them.
    [Fact]
    public void An_unknown_equipped_header_bit_is_not_the_strict_layout()
    {
        var raw = Raw("character2.ksd");
        var pc = PlayerCharacter.Parse(raw, IsItem);
        int header = EquippedHeaderAt(pc, 57), end = EquippedEnd(pc, 57);
        var data = Splice(raw, end + 4, 0, 0x00, 0x00);
        data[header + 2] = 0x0d;                                   // 05 | bit 19
        var re = PlayerCharacter.Parse(data, IsItem);
        Assert.Equal(InventoryStatus.InPlaceOnly, re.InventoryStatus);
        Assert.Equal(Enumerable.Range(5, 11), re.EquippedPositions!);
        Assert.Equal(data, re.ToBytes());
    }

    [Fact]
    public void Equipped_slot_bytes_must_be_the_record_index()
    {
        var raw = Raw("character2.ksd");
        var pc = PlayerCharacter.Parse(raw, IsItem);
        var lastAt = EquippedEnd(pc, 57) - pc.Container(ContainerKind.Equipped)!.Records[^1].EncodedLength;
        Assert.Equal(10, raw[lastAt]);
        var data = (byte[])raw.Clone();
        data[lastAt] = 12;                                         // still increasing, but not the index
        var re = PlayerCharacter.Parse(data, IsItem);
        Assert.Equal(InventoryStatus.InPlaceOnly, re.InventoryStatus);
        Assert.Equal(data, re.ToBytes());

        pc.Container(ContainerKind.Equipped)!.Records[^1].Slot = 12;
        Assert.Throws<InvalidOperationException>(() => pc.ToBytes());
    }

    // The game-made local "Test" (level 1): one Equipped record (Threadbare Pants, position 8), then zeros where a
    // played character has 05 n2 x n3 and the 02 00 03 00 tail. That shape isn't the strict layout.
    [Fact]
    public void The_fresh_local_character_is_read_in_place_only()
    {
        var raw = KsdFile.Parse(Fixtures.ReadLocal("character0.ksd"))["player_character"].GetData();
        var pc = PlayerCharacter.Parse(raw, IsItem);
        Assert.Equal(InventoryStatus.InPlaceOnly, pc.InventoryStatus);
        Assert.Equal(0x00000100u, pc.EquippedMask);
        Assert.Equal([8], pc.EquippedPositions!);
        Assert.Equal(0xecc1c60cu, Assert.Single(pc.Container(ContainerKind.Equipped)!.Records).ItemId);
        Assert.Equal(raw, pc.ToBytes());
    }

    [Fact]
    public void Count_edit_changes_only_two_bytes()
    {
        var raw = Raw("character2.ksd");
        var pc = PlayerCharacter.Parse(raw, IsItem);
        pc.Container(ContainerKind.Inventory)!.Records.Single(r => r.ItemId == 0xe988b200u).Count = 500;  // Fireball I 87 → 500
        var bytes = pc.ToBytes();
        var diff = Diff(raw, bytes);
        Assert.Equal(2, diff.Length);
        Assert.Equal(diff[0] + 1, diff[1]);
        Assert.Equal(500, PlayerCharacter.Parse(bytes, IsItem).Container(ContainerKind.Inventory)!.Records.Single(r => r.Slot == 30).Count);
    }

    [Fact]
    public void Full_inventory_add_and_remove_rewrite_the_count_bytes()
    {
        var raw = Raw("character2.ksd");
        var pc = PlayerCharacter.Parse(raw, IsItem);
        var inv = pc.Container(ContainerKind.Inventory)!.Records;
        inv.Insert(inv.FindIndex(r => r.Slot > 35), new ItemRecord(35, CopperOre, 5));
        pc.Container(ContainerKind.Extra)!.Records.Add(new ItemRecord(0, CopperOre, 7));

        var bytes = pc.ToBytes();
        Assert.Equal(raw.Length + 16, bytes.Length);
        var re = PlayerCharacter.Parse(bytes, IsItem);
        Assert.Equal(InventoryStatus.Full, re.InventoryStatus);
        Assert.Equal(22, re.Container(ContainerKind.Inventory)!.Records.Count);
        Assert.Contains(re.Container(ContainerKind.Inventory)!.Records, r => r.Slot == 35 && r.ItemId == CopperOre && r.Count == 5);
        Assert.Equal((0, CopperOre, 7), re.Container(ContainerKind.Extra)!.Records.Select(r => ((int)r.Slot, r.ItemId, (int)r.Count)).Single());
        Assert.Equal(7u, re.Level);
        Assert.Equal(30, re.Skills.Count);

        inv.RemoveAll(r => r.Slot == 35);
        pc.Container(ContainerKind.Extra)!.Records.Clear();
        Assert.Equal(raw, pc.ToBytes());
    }

    [Fact]
    public void Full_records_may_change_size()
    {
        var raw = Raw("character2.ksd");
        var pc = PlayerCharacter.Parse(raw, IsItem);
        pc.Container(ContainerKind.Inventory)!.Records.Single(r => r.Slot == 30).Rarity = (2, 0);
        var re = PlayerCharacter.Parse(pc.ToBytes(), IsItem);
        Assert.Equal(InventoryStatus.Full, re.InventoryStatus);
        Assert.Equal((byte)0x10, re.Container(ContainerKind.Inventory)!.Records.Single(r => r.Slot == 30).Flags);
    }

    [Fact]
    public void Equipped_record_count_cannot_change()
    {
        var pc = Load("character2.ksd");
        pc.Container(ContainerKind.Equipped)!.Records.RemoveAt(0);
        Assert.Throws<InvalidOperationException>(() => pc.ToBytes());
    }

    [Fact]
    public void ToBytes_refuses_records_it_could_not_read_back()
    {
        var pc = Load("character2.ksd");
        var inv = pc.Container(ContainerKind.Inventory)!.Records;

        var wand = inv.Single(r => r.ItemId == 0xe589d7a4u);
        wand.Count = 0;                                            // count 0 with fields is not a valid record
        Assert.Throws<InvalidOperationException>(() => pc.ToBytes());
        wand.Count = 1;

        (inv[1], inv[2]) = (inv[2], inv[1]);                       // slots out of order
        Assert.Throws<InvalidOperationException>(() => pc.ToBytes());
        (inv[1], inv[2]) = (inv[2], inv[1]);

        Assert.Equal(Raw("character2.ksd"), pc.ToBytes());
    }

    [Fact]
    public void Unrecognised_tail_falls_back_to_in_place_edits()
    {
        var raw = Raw("character2.ksd");
        raw[^22] = 0x09;                                           // break the end anchor (02 00 03 00 …)
        var pc = PlayerCharacter.Parse(raw, IsItem);

        Assert.Equal(InventoryStatus.InPlaceOnly, pc.InventoryStatus);
        Assert.Equal(11, pc.Container(ContainerKind.Equipped)!.Records.Count);
        Assert.Equal(Enumerable.Range(5, 11), pc.EquippedPositions!);  // the header just before the run fits it
        Assert.Equal(21, pc.Container(ContainerKind.Inventory)!.Records.Count);
        Assert.Equal(raw, pc.ToBytes());

        var inv = pc.Container(ContainerKind.Inventory)!.Records;
        var fireball = inv.Single(r => r.Slot == 30);
        fireball.Count = 500;
        Assert.Equal(2, Diff(raw, pc.ToBytes()).Length);

        fireball.Level = 3;                                        // would change the record's size
        Assert.Throws<InvalidOperationException>(() => pc.ToBytes());
        fireball.Level = null;

        inv.Add(new ItemRecord(38, CopperOre, 1));                 // adding needs the count byte
        Assert.Throws<InvalidOperationException>(() => pc.ToBytes());
    }

    [Fact]
    public void Unrecognised_status_entry_falls_back_to_in_place_edits()
    {
        var raw = Raw("character2.ksd");
        var statusAt = PlayerCharacter.LevelOffset + 12 + 5 * 30;
        raw[statusAt + 4 + 27 + 20] |= 0x01;                        // second status entry: field mask 0x10 → 0x11 (bit 0 unknown)
        var pc = PlayerCharacter.Parse(raw, IsItem);

        Assert.Equal(InventoryStatus.InPlaceOnly, pc.InventoryStatus);
        Assert.Equal(0xe5a9ac59u, pc.Container(ContainerKind.Equipped)!.Records[0].ItemId);
        Assert.Contains(pc.Container(ContainerKind.Inventory)!.Records, r => r.ItemId == 0xe988b200u && r.Count == 87);
        Assert.Equal(raw, pc.ToBytes());
    }

    [Fact]
    public void No_records_means_NotFound()
    {
        var raw = Raw("character2.ksd");
        var statusEnd = PlayerCharacter.LevelOffset + 12 + 5 * 30 + 4 + 27 + 30;   // 30 skills, status entries of 27 and 30 bytes
        var data = raw.AsSpan(0, statusEnd).ToArray().Concat(new byte[64]).ToArray();
        var pc = PlayerCharacter.Parse(data, IsItem);

        Assert.Equal(InventoryStatus.NotFound, pc.InventoryStatus);
        Assert.Empty(pc.Containers);
        Assert.Null(pc.Container(ContainerKind.Equipped));
        Assert.Null(pc.EquippedMask);
        Assert.Null(pc.EquippedPositions);
        Assert.Equal(data, pc.ToBytes());
    }
}
