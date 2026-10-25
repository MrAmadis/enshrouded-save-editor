using EnshroudedSaveEditor.Core.Records;

namespace EnshroudedSaveEditor.Tests;

public class ItemRecordTests
{
    static readonly Func<uint, bool> Any = _ => true;

    // slot 7, id 0x11223344, count 2, flags 0x3f, then all six fields in bit order
    static readonly byte[] AllFields =
    {
        0x07, 0x44, 0x33, 0x22, 0x11, 0x02, 0x00, 0x3f,
        0x09,                                                   // bit 0: level
        0x64, 0x00, 0x00, 0x00, 0xc8, 0x00, 0x00, 0x00,         // bit 1: durability 100 / 200
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,         // bit 2: 8 unknown bytes
        0xaa, 0xbb, 0xcc, 0xdd,                                 // bit 3: transmog
        0x03, 0x01,                                             // bit 4: rarity, extra
        0x88, 0x77, 0x66, 0x55, 0x37,                           // bit 5: gem id, marker
    };

    [Fact]
    public void Reads_every_field_and_writes_the_same_bytes()
    {
        var data = new byte[] { 0xee, 0xee }.Concat(AllFields).Concat(new byte[] { 0xee }).ToArray();
        var r = ItemRecord.TryRead(data, 2, Any)!;

        Assert.Equal((7, 0x11223344u, 2, (byte)0x3f), (r.Slot, r.ItemId, r.Count, r.Flags));
        Assert.Equal((byte)9, r.Level);
        Assert.Equal((100u, 200u), r.Durability);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, r.Bit2);
        Assert.Equal(new byte[] { 0xaa, 0xbb, 0xcc, 0xdd }, r.Transmog);
        Assert.Equal(((byte)3, (byte)1), r.Rarity);
        Assert.Equal((0x55667788u, (byte)0x37), r.Gem);
        Assert.Equal(36, r.EncodedLength);
        Assert.False(r.IsGhost);

        var o = new byte[r.EncodedLength];
        r.WriteTo(o);
        Assert.Equal(AllFields, o);
    }

    [Fact]
    public void Flags_follow_the_fields_that_are_set()
    {
        var r = new ItemRecord(3, 0x29433573, 1) { Rarity = (4, 0) };
        Assert.Equal((byte)0x10, r.Flags);
        Assert.Equal(10, r.EncodedLength);
        r.Level = 20;
        r.Gem = (0, 0);
        Assert.Equal((byte)0x31, r.Flags);
        Assert.Equal(16, r.EncodedLength);
        r.Rarity = null;
        Assert.Equal((byte)0x21, r.Flags);
    }

    [Fact]
    public void Ghost_is_a_plain_record_with_count_0()
    {
        var r = ItemRecord.TryRead(new byte[] { 0x0f, 0x13, 0x83, 0x10, 0x43, 0, 0, 0 }, 0, Any)!;
        Assert.True(r.IsGhost);
        Assert.Equal(0x43108313u, r.ItemId);
    }

    [Theory]
    [InlineData(0x40)]     // bit 6: unknown field size
    [InlineData(0x80)]     // bit 7: unknown field size
    public void Unknown_field_bits_are_not_a_record(int flags)
    {
        var data = new byte[] { 0, 1, 0, 0, 0, 1, 0, (byte)flags }.Concat(new byte[16]).ToArray();
        Assert.Null(ItemRecord.TryRead(data, 0, Any));
    }

    [Fact]
    public void Rejects_what_is_not_a_record()
    {
        var plain = new byte[] { 0, 1, 0, 0, 0, 5, 0, 0 };
        Assert.NotNull(ItemRecord.TryRead(plain, 0, Any));
        Assert.Null(ItemRecord.TryRead(plain, 0, _ => false));                              // not an item id
        Assert.Null(ItemRecord.TryRead(plain, 1, Any));                                     // runs past the end
        Assert.Null(ItemRecord.TryRead(plain, -1, Any));
        Assert.Null(ItemRecord.TryRead(new byte[] { 0, 1, 0, 0, 0, 0, 0, 1, 9 }, 0, Any)); // count 0 with fields
        Assert.Null(ItemRecord.TryRead(new byte[] { 0, 1, 0, 0, 0, 1, 0, 2, 0, 0 }, 0, Any)); // durability truncated
    }

    [Fact]
    public void Byte_fields_must_have_their_size()
    {
        var r = new ItemRecord(0, 1, 1);
        Assert.Throws<ArgumentException>(() => r.Bit2 = new byte[7]);
        Assert.Throws<ArgumentException>(() => r.Transmog = new byte[5]);
        Assert.Throws<ArgumentException>(() => r.WriteTo(new byte[7]));
    }
}
