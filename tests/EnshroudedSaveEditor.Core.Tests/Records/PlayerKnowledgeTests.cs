using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Records;

namespace EnshroudedSaveEditor.Tests;

public class PlayerKnowledgeTests
{
    [Theory, InlineData("character0.ksd", 1094), InlineData("character1.ksd", 1998)]
    public void Knowledge_parses_and_round_trips(string file, int count)
    {
        var raw = KsdFile.Parse(Fixtures.Read(file))["player_knowledge"].GetData();
        var pk = PlayerKnowledge.Parse(raw);
        Assert.Equal(count, pk.Entries.Count);
        Assert.True(pk.Contains(pk.Entries[0].Id));
        Assert.Equal(raw, pk.ToBytes());
    }

    [Fact]
    public void Learn_appends_the_new_ids_with_value_1_and_keeps_the_rest()
    {
        var raw = KsdFile.Parse(Fixtures.Read("character0.ksd"))["player_knowledge"].GetData();
        var pk = PlayerKnowledge.Parse(raw);
        var flags = pk.Entries.First(e => e.Value != 1);

        var learned = pk.Learn([flags.Id, 0xDEADBEEF, 0xDEADBEEF, 0x0BADF00D]);

        Assert.Equal(pk.Entries, learned.Entries.Take(1094));
        Assert.Equal([(0xDEADBEEFu, 1u), (0x0BADF00Du, 1u)], learned.Entries.Skip(1094));
        Assert.True(learned.Contains(0x0BADF00D));
        Assert.Equal(learned.Entries, PlayerKnowledge.Parse(learned.ToBytes()).Entries);
        Assert.Equal(raw.Length + 16, learned.ToBytes().Length);
        Assert.Equal(raw, pk.ToBytes());
    }

    [Fact]
    public void Knowledge_rejects_wrong_length()
    {
        var raw = KsdFile.Parse(Fixtures.Read("character0.ksd"))["player_knowledge"].GetData();
        Assert.Throws<KsdFormatException>(() => PlayerKnowledge.Parse(raw[..^1]));
    }
}
