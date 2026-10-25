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
    public void Knowledge_rejects_wrong_length()
    {
        var raw = KsdFile.Parse(Fixtures.Read("character0.ksd"))["player_knowledge"].GetData();
        Assert.Throws<KsdFormatException>(() => PlayerKnowledge.Parse(raw[..^1]));
    }
}
