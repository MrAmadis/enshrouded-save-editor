using Bunit;
using EnshroudedSaveEditor.Core.Editing;
using EnshroudedSaveEditor.Core.Files;
using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.GameData;
using EnshroudedSaveEditor.UI.Components;
using EnshroudedSaveEditor.UI.Services;
using EnshroudedSaveEditor.UI.Tabs;
using Microsoft.Extensions.DependencyInjection;

namespace EnshroudedSaveEditor.Tests;

/// <summary>Owner's rule: temp copies of the fixtures, a temp app data root and a fake process probe only.</summary>
public sealed class SkillsTabTests : IDisposable
{
    const string Module = "./_content/EnshroudedSaveEditor.UI/skilltree.js";

    readonly string _root = Fixtures.CopyToTemp();
    readonly string _appData = Path.Combine(Path.GetTempPath(), "enshrouded-appdata-" + Guid.NewGuid().ToString("N"));
    readonly BunitContext _ctx = new();
    BunitJSModuleInterop _module = null!;

    public void Dispose()
    {
        _ctx.Dispose();
        foreach (var d in new[] { _root, _appData })
            try { Directory.Delete(d, true); } catch { }
    }

    // character1 has 69 points to spend; character2 (Amadis) has 15 learned skills plus the 12 Sparks and 0 available.
    EditorService Start(int character)
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _module = _ctx.JSInterop.SetupModule(Module);
        _module.Mode = JSRuntimeMode.Loose;
        var options = new EditorOptions([_root], _appData, new EditorServiceTests.FakeProbe(), null);
        var editor = new EditorService(options, new GameDataProvider(ReferenceGameData.Load), new IconService(options.IconCacheRoot));
        _ctx.Services.AddSingleton(editor);
        _ctx.Services.AddSingleton<IAssetUrls>(new FakeAssets());
        editor.Open(Path.Combine(_root, $"character{character}.ksd"));
        return editor;
    }

    static Task Click(IRenderedComponent<SkillsTab> cut, uint id) =>
        cut.InvokeAsync(() => cut.FindComponent<SkillTree>().Instance.NodeClicked(id));

    static string Value(IRenderedComponent<SkillsTab> cut, string label) =>
        cut.Find($".points [data-points='{label}']").TextContent.Trim();

    static int LearnedCount(EditorService e) => e.Current.Nodes.Count(n => n.State == NodeState.Learned);

    [Fact]
    public void Tree_renders_243_nodes_and_12_labels()
    {
        Start(2);
        var cut = _ctx.Render<SkillsTab>();

        Assert.Equal(243, cut.FindAll("svg.tree .nd").Count);
        Assert.Equal(12, cut.FindAll("svg.tree .lab").Count);
        Assert.Contains(cut.FindAll("svg.tree .nd"), n => n.TagName.Equals("rect", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(["From levels", "Elixir Wells & Shroud Roots", "Spent", "Available"], cut.FindAll(".points dt").Select(t => t.TextContent.Trim()));
    }

    [Fact]
    public void Learned_nodes_have_learned_class()
    {
        Start(2);
        var cut = _ctx.Render<SkillsTab>();

        var learned = cut.FindAll("svg.tree .nd.learned");
        Assert.Equal(15 + 12, learned.Count);
        Assert.Equal(12, learned.Count(n => n.GetAttribute("r") == "4.2"));   // the Sparks
    }

    [Fact]
    public async Task NodeClicked_learns_connected_node_and_updates_points()
    {
        var editor = Start(1);
        var cut = _ctx.Render<SkillsTab>();
        Assert.Equal("69", Value(cut, "Available"));
        var node = editor.Current.Nodes.First(n => n.State == NodeState.Available && n.Cost > 0 && n.Cost <= 69);

        await Click(cut, node.Id);

        var after = editor.Current.Nodes.Single(n => n.Id == node.Id);
        Assert.Equal(NodeState.Learned, after.State);
        Assert.Equal(1, after.Rank);
        Assert.Equal((69 - node.Cost).ToString(), Value(cut, "Available"));
        Assert.Equal(node.Cost.ToString(), Value(cut, "Spent"));
        Assert.Contains("learned", cut.Find($"svg.tree .nd[data-id='{node.Id}']").ClassList);
    }

    [Fact]
    public async Task Clicking_again_ranks_up_then_unlearns_at_max_rank()
    {
        var editor = Start(1);
        var cut = _ctx.Render<SkillsTab>();
        var node = editor.Current.Nodes.First(n => n.State == NodeState.Available && n.MaxRank == 3 && n.Cost * 3 <= 69);

        await Click(cut, node.Id);
        await Click(cut, node.Id);
        Assert.Equal(2, editor.Current.Nodes.Single(n => n.Id == node.Id).Rank);
        await Click(cut, node.Id);
        Assert.Equal(3, editor.Current.Nodes.Single(n => n.Id == node.Id).Rank);
        await Click(cut, node.Id);   // at max rank with no dependents: unlearns straight away

        Assert.Equal(0, editor.Current.Nodes.Single(n => n.Id == node.Id).Rank);
        Assert.Equal("69", Value(cut, "Available"));
        Assert.Empty(cut.FindAll(".confirm-unlearn"));
    }

    [Fact]
    public async Task Unlearn_with_dependents_asks_first()
    {
        var editor = Start(2);
        var cut = _ctx.Render<SkillsTab>();
        var node = editor.Current.Nodes.First(n => n.Type != SkillNodeType.Spark && n.State == NodeState.Learned
            && n.Rank >= n.MaxRank && editor.DependentsOf(n.Id).Count > 0);
        var dependents = editor.DependentsOf(node.Id).Select(d => editor.Current.Nodes.Single(n => n.Id == d).Name).ToList();
        var before = LearnedCount(editor);

        await Click(cut, node.Id);

        Assert.Equal(before, LearnedCount(editor));   // nothing changed yet
        Assert.Contains(node.Name, cut.Find(".confirm-unlearn").TextContent);
        Assert.Equal(dependents.Order(), cut.FindAll(".confirm-unlearn .dependents li").Select(li => li.TextContent.Trim()).Order());

        cut.Find(".confirm-unlearn .cancel").Click();
        Assert.Empty(cut.FindAll(".confirm-unlearn"));
        Assert.Equal(before, LearnedCount(editor));

        await Click(cut, node.Id);
        cut.Find(".confirm-unlearn .confirm").Click();
        Assert.Empty(cut.FindAll(".confirm-unlearn"));
        Assert.Equal(before - 1 - dependents.Count, LearnedCount(editor));
        Assert.Equal(0, editor.Current.Nodes.Single(n => n.Id == node.Id).Rank);
    }

    [Fact]
    public async Task Rejected_learn_shows_the_message_in_the_hover_card()
    {
        var editor = Start(1);
        var cut = _ctx.Render<SkillsTab>();
        var node = editor.Current.Nodes.First(n => n.State == NodeState.Locked);

        await Click(cut, node.Id);   // must not throw

        Assert.Equal("Connect it to a learned skill first", cut.Find(".tip .tip-message").TextContent.Trim());
        Assert.Equal("69", Value(cut, "Available"));
        Assert.Empty(editor.Current.Pending);
    }

    [Fact]
    public async Task Hover_card_shows_name_rank_cost_and_action()
    {
        var editor = Start(1);
        var cut = _ctx.Render<SkillsTab>();
        var node = editor.Current.Nodes.First(n => n.State == NodeState.Available && n.Cost > 0);
        Assert.Empty(cut.FindAll(".tip"));

        await cut.InvokeAsync(() => cut.FindComponent<SkillTree>().Instance.NodeHovered(node.Id));

        Assert.Equal(node.Name, cut.Find(".tip .tip-name").TextContent.Trim());
        Assert.Equal($"rank 0/{node.MaxRank} · cost {node.Cost}", cut.Find(".tip .tip-meta").TextContent.Trim());
        Assert.Equal("Click to learn", cut.Find(".tip .tip-action").TextContent.Trim());

        await cut.InvokeAsync(() => cut.FindComponent<SkillTree>().Instance.NodeHovered(0));
        Assert.Empty(cut.FindAll(".tip"));
    }

    [Fact]
    public void Find_a_skill_calls_flyTo()
    {
        var editor = Start(2);
        var cut = _ctx.Render<SkillsTab>();
        var node = editor.Current.Nodes.First(n => n.Type == SkillNodeType.Ability);

        cut.Find("input.find").Change(node.Name.ToUpperInvariant());

        var call = _module.VerifyInvoke("flyTo");
        Assert.Equal(3, call.Arguments.Count);
        Assert.Single(cut.FindAll("svg.tree .selring"));
        Assert.Empty(cut.FindAll(".find-miss"));

        cut.Find("input.find").Change("zzz no such skill");
        Assert.NotEmpty(cut.FindAll(".find-miss"));
        Assert.Empty(cut.FindAll("svg.tree .selring"));
    }

    [Fact]
    public void Zoom_buttons_call_the_module()
    {
        Start(2);
        var cut = _ctx.Render<SkillsTab>();

        cut.Find("button.zin").Click();
        cut.Find("button.zout").Click();
        cut.Find("button.zfit").Click();

        Assert.Equal(2, _module.Invocations["zoomBy"].Count());
        _module.VerifyInvoke("fit");
    }

    [Fact]
    public void Refund_all_asks_for_confirmation_then_clears_the_tree_and_undo_restores_it()
    {
        var editor = Start(2);
        var cut = _ctx.Render<SkillsTab>();

        cut.Find("button.refund-all").Click();
        Assert.NotEmpty(cut.FindAll(".confirm-refund"));
        Assert.Equal(27, LearnedCount(editor));

        cut.Find(".confirm-refund .cancel").Click();
        Assert.Empty(cut.FindAll(".confirm-refund"));
        Assert.Equal(27, LearnedCount(editor));

        cut.Find("button.refund-all").Click();
        cut.Find(".confirm-refund .confirm").Click();
        Assert.Equal(12, LearnedCount(editor));
        Assert.Equal("22", Value(cut, "Available"));
        Assert.Equal(12, cut.FindAll("svg.tree .nd.learned").Count);

        cut.Find("button.undo").Click();
        Assert.Equal(27, LearnedCount(editor));
        Assert.Equal("0", Value(cut, "Available"));
    }

    [Fact]
    public void Refund_all_with_nothing_learned_shows_the_message_inline()
    {
        Start(1);
        var cut = _ctx.Render<SkillsTab>();

        cut.Find("button.refund-all").Click();
        cut.Find(".confirm-refund .confirm").Click();

        Assert.Equal("Nothing to refund", cut.Find(".skills-error").TextContent.Trim());
    }

    [Fact]
    public void Undo_with_nothing_to_undo_shows_the_message_inline()
    {
        Start(2);
        var cut = _ctx.Render<SkillsTab>();

        cut.Find("button.undo").Click();

        Assert.Equal("Nothing to undo", cut.Find(".skills-error").TextContent.Trim());
    }

    [Fact]
    public void There_is_no_unlimited_control()
    {
        Start(2);
        var cut = _ctx.Render<SkillsTab>();

        Assert.DoesNotContain("Unlimited", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Game rules", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(cut.FindAll(".seg"));
    }
}
