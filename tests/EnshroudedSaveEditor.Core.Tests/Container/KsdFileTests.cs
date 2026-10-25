using System.Buffers.Binary;
using EnshroudedSaveEditor.Core.Container;

namespace EnshroudedSaveEditor.Tests.Container;

public class KsdFileTests
{
    [Fact]
    public void Parses_character2_header_and_sections()
    {
        var f = KsdFile.Parse(Fixtures.Read("character2.ksd"));
        Assert.Equal(2, f.Version);
        Assert.Equal(991u, f.Revision);
        Assert.Equal(1792490571, f.SavedAt.ToUnixTimeSeconds());
        Assert.Equal(new[] { "character_data", "player_character", "voiceover", "player_knowledge", "fogofwar" }, f.Sections.Select(s => s.Name));
        Assert.All(f.Sections, s => Assert.True(s.ChecksumValid));
        Assert.False(f["voiceover"].Compressed);
        Assert.Equal(5u, f["player_character"].DataVersion);
        Assert.Equal(706, f["player_character"].GetData().Length);
        Assert.Equal(1_638_413, f["fogofwar"].GetData().Length);
    }

    [Theory, InlineData("character0.ksd"), InlineData("character1.ksd"), InlineData("character2.ksd"), InlineData("global.ksd"), InlineData("world2.ksd")]
    public void Unchanged_file_round_trips_byte_for_byte(string name)
    {
        var bytes = Fixtures.Read(name);
        Assert.Equal(bytes, KsdFile.Parse(bytes).ToBytes());
    }

    [Fact]
    public void Changed_header_fields_are_written()
    {
        var f = KsdFile.Parse(Fixtures.Read("character2.ksd"));
        var guid = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        f.Guid = guid;
        f.Revision = 1000;
        f.SavedAt = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var g = KsdFile.Parse(f.ToBytes());
        Assert.Equal(guid, g.Guid);
        Assert.Equal(1000u, g.Revision);
        Assert.Equal(1_800_000_000, g.SavedAt.ToUnixTimeSeconds());
    }

    [Fact]
    public void SetData_recompresses_and_keeps_other_blocks_identical()
    {
        var original = KsdFile.Parse(Fixtures.Read("character2.ksd"));
        var edited = KsdFile.Parse(Fixtures.Read("character2.ksd"));
        var data = edited["player_character"].GetData();
        data[58] = 8;
        edited["player_character"].SetData(data);

        var bytes = edited.ToBytes();
        var again = KsdFile.Parse(bytes);
        Assert.Equal(8, again["player_character"].GetData()[58]);
        Assert.All(again.Sections, s => Assert.True(s.ChecksumValid));
        foreach (var s in original.Sections.Where(s => s.Name != "player_character"))
            Assert.True(s.Block.Span.SequenceEqual(again[s.Name].Block.Span), s.Name);
        Assert.Equal(0, (0x28 + (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x28))) % 16);
        Assert.Equal((uint)bytes.Length, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)));
    }

    [Fact]
    public void Checksum_mismatch_is_reported_not_thrown()
    {
        var bytes = Fixtures.Read("character2.ksd");
        var corrupt = (byte[])bytes.Clone();
        var blockEnd = IndexOfBlock(bytes, "fogofwar") + KsdFile.Parse(bytes)["fogofwar"].Block.Length;
        corrupt[blockEnd - 1] ^= 0xFF;

        var f = KsdFile.Parse(corrupt);
        Assert.False(f["fogofwar"].ChecksumValid);
        Assert.All(f.Sections.Where(s => s.Name != "fogofwar"), s => Assert.True(s.ChecksumValid));
    }

    [Fact]
    public void PatchBlock_rewrites_the_checksum()
    {
        var patched = Fixtures.PatchBlock(Fixtures.Read("character2.ksd"), "voiceover", b => b[^1] ^= 0xFF);
        Assert.All(KsdFile.Parse(patched).Sections, s => Assert.True(s.ChecksumValid));
    }
    [Fact]
    public void Parse_rejects_garbage()
    {
        var good = Fixtures.Read("character2.ksd");
        byte[] With(Action<byte[]> edit) { var c = (byte[])good.Clone(); edit(c); return c; }

        Assert.Throws<KsdFormatException>(() => KsdFile.Parse(With(b => b[0] = (byte)'X')));
        Assert.Throws<KsdFormatException>(() => KsdFile.Parse(With(b => b[6] = 49)));
        Assert.Throws<KsdFormatException>(() => KsdFile.Parse(With(b => b[8]++)));
        Assert.Throws<KsdFormatException>(() => KsdFile.Parse(With(b => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x28), (uint)b.Length))));
        Assert.Throws<KsdFormatException>(() => KsdFile.Parse(With(b => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x2C), 4097))));
        Assert.Throws<KsdFormatException>(() => KsdFile.Parse(With(b =>
        {
            var tableAt = 0x28 + (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(0x28));
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(tableAt + 8), (uint)b.Length);
        })));
        Assert.Throws<KsdFormatException>(() => KsdFile.Parse(new byte[10]));
    }

    private static int IndexOfBlock(byte[] file, string section)
    {
        var tableAt = 0x28 + (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(0x28));
        var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(0x2C));
        for (var i = 0; i < count; i++)
        {
            var at = tableAt + i * 20;
            var off = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at));
            var len = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 4));
            if (System.Text.Encoding.UTF8.GetString(file, at + off, len) == section)
                return (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 8));
        }
        throw new ArgumentException(section);
    }
}
