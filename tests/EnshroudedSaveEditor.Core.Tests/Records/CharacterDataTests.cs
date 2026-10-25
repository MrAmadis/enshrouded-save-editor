using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Records;

namespace EnshroudedSaveEditor.Tests;

public class CharacterDataTests
{
    [Theory, InlineData("character0.ksd", 0), InlineData("character1.ksd", 1), InlineData("character2.ksd", 2)]
    public void CharacterData_reads_name_slot_and_guid(string file, int slot)
    {
        var k = KsdFile.Parse(Fixtures.Read(file));
        var cd = CharacterData.Parse(k["character_data"].GetData());
        Assert.Equal("Amadis", cd.Name);
        Assert.Equal(slot, cd.SlotIndex);
        Assert.Equal(k.Guid, cd.Guid);
        Assert.Equal(k["character_data"].GetData(), cd.ToBytes());
    }

    [Fact]
    public void CharacterData_rename_changes_length()
    {
        var k = KsdFile.Parse(Fixtures.Read("character2.ksd"));
        var raw = k["character_data"].GetData();
        var cd = CharacterData.Parse(raw).With(name: "Amadis the Bold");
        var re = CharacterData.Parse(cd.ToBytes());
        Assert.Equal("Amadis the Bold", re.Name);
        Assert.Equal(k.Guid, re.Guid);
        Assert.Equal(raw.Length + 9, cd.ToBytes().Length);
    }

    [Fact]
    public void CharacterData_rejects_truncated_record()
    {
        var raw = KsdFile.Parse(Fixtures.Read("character2.ksd"))["character_data"].GetData();
        Assert.Throws<KsdFormatException>(() => CharacterData.Parse(raw[..8]));
        Assert.Throws<KsdFormatException>(() => CharacterData.Parse(raw[..20]));
    }

    [Fact]
    public void CharacterData_reads_the_local_fixture_with_its_storage_tag()
    {
        var k = KsdFile.Parse(File.ReadAllBytes(Path.Combine(Fixtures.RealLocal, "character0.ksd")));
        var raw = k["character_data"].GetData();
        var cd = CharacterData.Parse(raw);
        Assert.Equal("Test", cd.Name);
        Assert.Equal(0, cd.SlotIndex);
        Assert.Equal(new byte[] { 0xa8, 0x23, 0xb2, 0x28 }, cd.StorageTag);
        Assert.Equal(raw, cd.ToBytes());
        Assert.Equal(new byte[] { 0x93, 0x7B, 0x40, 0xB6 },
            CharacterData.Parse(Fixtures.Read("character2.ksd") is var b ? KsdFile.Parse(b)["character_data"].GetData() : []).StorageTag);
    }
}
