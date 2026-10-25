using System.Buffers.Binary;
using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Records;

namespace EnshroudedSaveEditor.Tests;

public class PlayerCharacterHeaderTests
{
    static byte[] Raw(string file) => KsdFile.Parse(Fixtures.Read(file))["player_character"].GetData();

    [Theory, InlineData("character0.ksd", 12u, 2913u, 0), InlineData("character1.ksd", 18u, 5802u, 0), InlineData("character2.ksd", 7u, 6500u, 30)]
    public void Header_level_xp_skills(string file, uint level, uint xp, int skills)
    {
        var raw = Raw(file);
        var pc = PlayerCharacter.Parse(raw, _ => true);
        Assert.Equal(level, pc.Level);
        Assert.Equal(xp, pc.Xp);
        Assert.Equal(skills, pc.Skills.Count);
        Assert.Equal(raw, pc.ToBytes());
    }

    [Fact]
    public void Character2_has_15_rank0_entries()
    {
        var pc = PlayerCharacter.Parse(Raw("character2.ksd"), _ => true);
        Assert.Equal(15, pc.Skills.Count(s => s.Rank == 0));
        Assert.Equal(15, pc.Skills.Count(s => s.Rank >= 1));
    }

    [Fact]
    public void Skill_list_can_shrink_and_grow()
    {
        var pc = PlayerCharacter.Parse(Raw("character2.ksd"), _ => true);
        pc.Skills.RemoveRange(0, 3);
        pc.Skills.Add(new SkillEntry(4242, 3));
        var re = PlayerCharacter.Parse(pc.ToBytes(), _ => true);
        Assert.Equal(pc.Skills, re.Skills);
        Assert.Equal(28, re.Skills.Count);
        Assert.Equal(7u, re.Level);
        Assert.Equal(6500u, re.Xp);
    }

    [Fact]
    public void Unrecognised_header_is_rejected()
    {
        var raw = Raw("character2.ksd");
        BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(38), 1e9f);
        Assert.Throws<UnsupportedLayoutException>(() => PlayerCharacter.Parse(raw, _ => true));
    }
}
