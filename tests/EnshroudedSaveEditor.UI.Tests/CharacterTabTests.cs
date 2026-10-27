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
    EditorService Start(int character = 2, Func<IGameData>? data = null)
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;                      // fit.js: no browser here
        var options = new EditorOptions([_root], _appData, new EditorServiceTests.FakeProbe(), null);
        var editor = new EditorService(options, new GameDataProvider(data ?? ReferenceGameData.Load), new IconService(options.IconCacheRoot));
        _ctx.Services.AddSingleton(editor);
        _ctx.Services.AddSingleton<IAssetUrls>(new FakeAssets());
        editor.Open(Path.Combine(_root, $"character{character}.ksd"));
        return editor;
    }

    static string Value(IRenderedComponent<CharacterTab> cut, string label) =>
        cut.Find($".points [data-points='{label}']").TextContent.Trim();

    // Owner, 2026-10-26: the tab scales with the window like the inventory, but doesn't fill it: fit.js "window" mode
    // scales by the window's size against the default one, never below today's size.
    [Fact]
    public async Task The_tab_scales_with_the_window()
    {
        Start();
        var fit = _ctx.JSInterop.SetupModule("./_content/EnshroudedSaveEditor.UI/fit.js");
        fit.Mode = JSRuntimeMode.Loose;
        _ctx.Render<CharacterTab>();

        var attach = Assert.Single(fit.Invocations["attach"]);
        Assert.Equal("window", attach.Arguments[1]);
        await _ctx.DisposeComponentsAsync();
        Assert.Single(fit.Invocations["detach"]);
    }

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

    // Owner, 2026-10-27: both map options are marked experimental and possibly dangerous.
    [Fact]
    public void The_map_options_are_marked_experimental_and_possibly_dangerous()
    {
        Start();
        var panel = _ctx.Render<CharacterTab>().Find(".map-panel");

        Assert.Equal("Experimental", panel.QuerySelector("h3 .tag-experimental")!.TextContent.Trim());
        Assert.Contains("possibly dangerous", panel.QuerySelector(".map-warning")!.TextContent);
        Assert.NotNull(panel.QuerySelector("button.clear-fog"));
        Assert.NotNull(panel.QuerySelector("button.discover"));
    }

    [Fact]
    public void Clear_fog_reveals_the_map()
    {
        var editor = Start();
        var cut = _ctx.Render<CharacterTab>();
        Assert.Equal("Partly explored", cut.Find(".fog-status").TextContent.Trim());

        cut.Find("button.clear-fog").Click();

        Assert.Equal(FogStatus.Cleared, editor.Current.Fog);
        Assert.Equal(["Cleared fog of war"], editor.Current.Pending);
        Assert.Equal("The whole map is revealed", cut.Find(".fog-status").TextContent.Trim());
        Assert.True(cut.Find("button.clear-fog").HasAttribute("disabled"));
    }

    // The game-made local "Test" character never went into a world, so it has no fog to clear.
    [Fact]
    public void Clear_fog_is_off_for_a_character_without_a_map()
    {
        File.Copy(Path.Combine(Fixtures.RealLocal, "character0.ksd"), Path.Combine(_root, "character3.ksd"));
        Start(character: 3);
        var cut = _ctx.Render<CharacterTab>();

        Assert.Equal(MapEditor.Explain(FogStatus.NoMap), cut.Find(".fog-status").TextContent.Trim());
        Assert.True(cut.Find("button.clear-fog").HasAttribute("disabled"));
    }

    [Fact]
    public void Discover_all_locations_learns_them()
    {
        var editor = Start(data: () => new EditorServiceTests.WithLocations(ReferenceGameData.Load()));
        var cut = _ctx.Render<CharacterTab>();
        Assert.Equal("1 of 3 discovered", cut.Find(".places-status").TextContent.Trim());

        cut.Find("button.discover").Click();

        Assert.Equal(["Discovered 2 locations"], editor.Current.Pending);
        Assert.Equal("3 of 3 discovered", cut.Find(".places-status").TextContent.Trim());
        Assert.True(cut.Find("button.discover").HasAttribute("disabled"));
    }

    [Fact]
    public void Discover_is_off_without_the_install_data()
    {
        var editor = Start();
        var cut = _ctx.Render<CharacterTab>();

        Assert.Equal(editor.Current.Locations.Unavailable, cut.Find(".places-status").TextContent.Trim());
        Assert.True(cut.Find("button.discover").HasAttribute("disabled"));
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
