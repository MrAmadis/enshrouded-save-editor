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
    public void CharacterData_rejects_bad_signature()
    {
        var raw = KsdFile.Parse(Fixtures.Read("character2.ksd"))["character_data"].GetData();
        raw[1] ^= 0xFF;
        Assert.Throws<KsdFormatException>(() => CharacterData.Parse(raw));
    }
}
