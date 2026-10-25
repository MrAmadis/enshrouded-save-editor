using Bunit;
using EnshroudedSaveEditor.Core.Editing;
using EnshroudedSaveEditor.Core.Files;
using EnshroudedSaveEditor.GameData;
using EnshroudedSaveEditor.UI.Screens;
using EnshroudedSaveEditor.UI.Services;
using EnshroudedSaveEditor.UI.Tabs;
using Microsoft.Extensions.DependencyInjection;

namespace EnshroudedSaveEditor.Tests;

/// <summary>Owner's rule: temp copies of the fixtures, a temp app data root and a fake process probe only.</summary>
public sealed class CharacterTabTests : IDisposable
{
    readonly string _root = Fixtures.CopyToTemp();
    readonly string _appData = Path.Combine(Path.GetTempPath(), "enshrouded-appdata-" + Guid.NewGuid().ToString("N"));
    readonly BunitContext _ctx = new();

    public void Dispose()
    {
        _ctx.Dispose();
        foreach (var d in new[] { _root, _appData })
            try { Directory.Delete(d, true); } catch { }
    }

    // character2: Amadis, level 7, 12 from levels + 10 from rewards, 22 spent, 0 available.
    EditorService Start(int character = 2)
    {
        var options = new EditorOptions([_root], _appData, new EditorServiceTests.FakeProbe(), null);
        var editor = new EditorService(options, new GameDataProvider(ReferenceGameData.Load), new IconService(options.IconCacheRoot));
        _ctx.Services.AddSingleton(editor);
        _ctx.Services.AddSingleton<IAssetUrls>(new FakeAssets());
        editor.Open(Path.Combine(_root, $"character{character}.ksd"));
        return editor;
    }

    static string Value(IRenderedComponent<CharacterTab> cut, string label) =>
        cut.Find($".points [data-points='{label}']").TextContent.Trim();

    [Fact]
    public void Shows_the_character_and_the_points_summary()
    {
        Start();
        var cut = _ctx.Render<CharacterTab>();

        Assert.Equal("Amadis", cut.Find("input.name-field").GetAttribute("value"));
        Assert.Contains("15", cut.Find(".name-limit").TextContent);
        Assert.Equal("7", cut.Find(".level-stepper input").GetAttribute("value"));
        Assert.Contains("max 50", cut.Find(".level-stepper").TextContent);
        Assert.Equal(["From levels", "Elixir Wells & Shroud Roots", "Spent", "Available"],
            cut.FindAll(".points dt").Select(t => t.TextContent.Trim()));
        Assert.Equal(["12", "10", "22", "0"], cut.FindAll(".points dd").Select(t => t.TextContent.Trim()));
        Assert.Empty(cut.FindAll(".xp-next"));   // the reference data has no XP table
    }

    [Fact]
    public void Level_change_updates_points()
    {
        var editor = Start();
        var cut = _ctx.Render<CharacterTab>();

        cut.Find(".level-stepper button.plus").Click();

        Assert.Equal(8, editor.Current.Level);
        Assert.Equal("8", cut.Find(".level-stepper input").GetAttribute("value"));
        Assert.Equal("14", Value(cut, "From levels"));
        Assert.Equal("2", Value(cut, "Available"));
        Assert.Empty(cut.FindAll(".overspend"));

        cut.Find(".level-stepper input").Change("20");
        Assert.Equal(20, editor.Current.Level);
        Assert.Equal("38", Value(cut, "From levels"));
    }

    [Fact]
    public void Overspend_warning_appears_below_spent_level()
    {
        var editor = Start();
        var cut = _ctx.Render<CharacterTab>();

        cut.Find(".level-stepper button.minus").Click();

        Assert.Equal(6, editor.Current.Level);
        Assert.Equal("-2", Value(cut, "Available"));
        Assert.Contains("negative", cut.Find(".points [data-points='Available']").ClassList);
        Assert.Equal("Your skills cost more than this level gives. Refund skills first.", cut.Find(".overspend").TextContent.Trim());

        cut.Find(".level-stepper button.plus").Click();
        Assert.Empty(cut.FindAll(".overspend"));
        Assert.DoesNotContain("negative", cut.Find(".points [data-points='Available']").ClassList);
    }

    [Fact]
    public void Name_validation_message_shows()
    {
        var editor = Start();
        var cut = _ctx.Render<CharacterTab>();

        cut.Find("input.name-field").Change("ABCDEFGHIJKLMNOP");
        Assert.Equal("The name can be at most 15 characters.", cut.Find(".name-error").TextContent.Trim());
        cut.Find("input.name-field").Change("   ");
        Assert.Equal("The name cannot be empty.", cut.Find(".name-error").TextContent.Trim());
        Assert.Equal("Amadis", editor.Current.Name);

        cut.Find("input.name-field").Change("Zed");
        Assert.Empty(cut.FindAll(".name-error"));
        Assert.Equal("Zed", editor.Current.Name);
    }

    [Fact]
    public void Unchanged_values_are_skipped_without_an_Already_error()
    {
        var editor = Start();
        var cut = _ctx.Render<CharacterTab>();

        cut.Find("input.name-field").Change("Amadis");
        cut.Find("input.name-field").Change("  Amadis ");
        cut.Find(".level-stepper input").Change("7");
        cut.Find(".xp-field input").Change(editor.Current.Xp.ToString());

        Assert.Empty(cut.FindAll(".name-error"));
        Assert.Empty(cut.FindAll(".field-error"));
        Assert.Empty(editor.Current.Pending);
    }

    [Fact]
    public void Xp_edit_is_applied()
    {
        var editor = Start();
        var cut = _ctx.Render<CharacterTab>();

        cut.Find(".xp-field input").Change("1234");

        Assert.Equal(1234u, editor.Current.Xp);
        Assert.Single(editor.Current.Pending);
    }

    [Fact]
    public void Read_only_session_disables_every_control()
    {
        File.WriteAllBytes(Path.Combine(_root, "character2.ksd"),
            Fixtures.PatchBlock(File.ReadAllBytes(Path.Combine(_root, "character2.ksd")), "player_character", b => b[10] = 6));
        Start();
        var cut = _ctx.Render<CharacterTab>();

        Assert.All(cut.FindAll("input, button"), e => Assert.True(e.HasAttribute("disabled")));
    }

    [Fact]
    public void Save_with_overspend_asks_for_confirmation_first()
    {
        var editor = Start();
        editor.SetLevel(6);
        var cut = _ctx.Render<Editor>();

        cut.Find("button.save").Click();

        Assert.Contains("If you save like this, the game resets your whole skill tree when it loads.", cut.Markup);
        Assert.Single(editor.Current.Pending);               // nothing written yet
        Assert.Empty(editor.Backups());

        cut.Find("button.cancel-overspend").Click();
        Assert.Empty(cut.FindAll("button.confirm-overspend"));
        Assert.Empty(editor.Backups());

        cut.Find("button.save").Click();
        cut.Find("button.confirm-overspend").Click();

        Assert.Empty(editor.Current.Pending);
        Assert.Single(editor.Backups());
        Assert.Equal("Saved. Backup kept.", cut.Find(".banner").TextContent.Trim());
    }

    [Fact]
    public void Save_without_overspend_does_not_ask()
    {
        var editor = Start();
        editor.SetLevel(10);
        var cut = _ctx.Render<Editor>();

        cut.Find("button.save").Click();

        Assert.Empty(cut.FindAll("button.confirm-overspend"));
        Assert.Empty(editor.Current.Pending);
    }
}
