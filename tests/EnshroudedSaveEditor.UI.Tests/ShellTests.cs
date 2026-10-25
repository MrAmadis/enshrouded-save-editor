using Bunit;
using EnshroudedSaveEditor.Core.Files;
using EnshroudedSaveEditor.Core.Editing;
using EnshroudedSaveEditor.GameData;
using EnshroudedSaveEditor.UI;
using EnshroudedSaveEditor.UI.Components;
using EnshroudedSaveEditor.UI.Screens;
using EnshroudedSaveEditor.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EnshroudedSaveEditor.Tests;

public sealed class FakeAssets : IAssetUrls
{
    public string ItemIcon(uint id) => $"icon://{id}";
    public string SkillIcon(uint nodeId) => $"skill://{nodeId}";
    public string PositionIcon(EquipPosition position) => $"pos://{position}";
}

/// <summary>Owner's rule: temp copies of the fixtures, a temp app data root and a fake process probe only.</summary>
public sealed class ShellTests : IDisposable
{
    readonly string _root = Fixtures.CopyToTemp();
    readonly string _appData = Path.Combine(Path.GetTempPath(), "enshrouded-appdata-" + Guid.NewGuid().ToString("N"));
    readonly EditorServiceTests.FakeProbe _probe = new();
    readonly BunitContext _ctx = new();

    public void Dispose()
    {
        _ctx.Dispose();
        foreach (var d in new[] { _root, _appData })
            try { Directory.Delete(d, true); } catch { }
    }

    string C(int n) => Path.Combine(_root, $"character{n}.ksd");

    EditorService Start(Func<string?>? pick = null, string? root = null)
    {
        var options = new EditorOptions([root ?? _root], _appData, _probe, pick);
        var editor = new EditorService(options, new GameDataProvider(ReferenceGameData.Load), new IconService(options.IconCacheRoot));
        _ctx.Services.AddSingleton(editor);
        _ctx.Services.AddSingleton<IAssetUrls>(new FakeAssets());
        return editor;
    }

    [Fact]
    public void Picker_shows_three_amadis_cards_with_levels()
    {
        Start();
        var cut = _ctx.Render<CharacterPicker>();

        Assert.Equal(3, cut.FindAll(".character-card").Count);
        Assert.Contains("Amadis", cut.Markup);
        Assert.Contains("Level 7", cut.Markup);
        Assert.Contains("Level 18", cut.Markup);
        Assert.Contains("Level 12", cut.Markup);
        Assert.Contains("character1.ksd", cut.Markup);
        Assert.Contains("69 points to spend", cut.Find("[data-file='character1.ksd']").TextContent);
        Assert.Empty(cut.FindAll(".chip-problem"));
    }

    [Fact]
    public void Picker_card_opens_the_character_in_the_editor()
    {
        var editor = Start();
        var cut = _ctx.Render<App>();

        cut.Find("[data-file='character2.ksd']").Click();

        Assert.True(editor.IsOpen);
        Assert.Contains("Save changes", cut.Markup);
        Assert.Contains("character2.ksd", cut.Find(".who").TextContent);
        cut.Find("button.back").Click();
        Assert.False(editor.IsOpen);
        Assert.NotEmpty(cut.FindAll(".character-card"));
    }

    [Fact]
    public void Picker_tags_steam_and_local_characters_and_flags_damaged_ones()
    {
        Assert.Equal("Steam", CharacterPicker.SourceTag(@"C:\Program Files (x86)\Steam\userdata\123\1203620\remote\character0.ksd"));
        Assert.Equal("Local", CharacterPicker.SourceTag(@"C:\Users\x\Saved Games\keen games\Enshrouded\Profile\character0.ksd"));
        Assert.Null(CharacterPicker.SourceTag(@"C:\temp\character0.ksd"));

        File.WriteAllBytes(C(0), [1, 2, 3, 4]);
        Start();
        var cut = _ctx.Render<CharacterPicker>();
        Assert.Contains("Damaged", cut.Find("[data-file='character0.ksd'] .chip-problem").TextContent);
    }

    [Fact]
    public void Picker_offers_choose_saves_folder_when_none_is_found()
    {
        var picked = 0;
        var empty = Path.Combine(Path.GetTempPath(), "enshrouded-missing-" + Guid.NewGuid().ToString("N"));
        Start(() => { picked++; return _root; }, empty);
        var cut = _ctx.Render<CharacterPicker>();

        cut.Find("button.choose-saves").Click();

        Assert.Equal(1, picked);
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".character-card").Count));
    }

    [Fact]
    public void Picker_shows_game_running_banner()
    {
        _probe.Game = true;
        Start();
        var cut = _ctx.Render<CharacterPicker>();
        Assert.Contains("Enshrouded is running. Close it to save.", cut.Find(".banner").TextContent);
    }

    [Fact]
    public void Editor_shows_game_running_banner_and_disables_save()
    {
        _probe.Game = true;
        var editor = Start();
        editor.Open(C(2));
        editor.SetLevel(13);
        var cut = _ctx.Render<Editor>();

        Assert.Equal("Enshrouded is running. Close it to save.", cut.Find(".banner").TextContent.Trim());
        Assert.True(cut.Find("button.save").HasAttribute("disabled"));
    }

    [Fact]
    public void Editor_frame_has_pill_tabs_and_backups()
    {
        var editor = Start();
        editor.Open(C(2));
        var cut = _ctx.Render<Editor>();

        Assert.Contains("Amadis · Level 7 · character2.ksd", cut.Find(".who").TextContent);
        Assert.Equal(["Character", "Inventory", "Skills"], cut.FindAll(".tab").Select(t => t.TextContent.Trim()));
        Assert.True(cut.Find("button.save").HasAttribute("disabled"));   // nothing pending
        cut.Find("button.backups").Click();
        Assert.Contains("No backups yet", cut.Markup);
    }

    [Fact]
    public void Pending_bar_lists_unsaved_edits()
    {
        var editor = Start();
        editor.Open(C(2));
        var cut = _ctx.Render<Editor>();
        Assert.Empty(cut.FindAll(".pending"));

        editor.SetLevel(10);
        editor.SetName("Zed");

        cut.WaitForAssertion(() =>
        {
            var bar = cut.Find(".pending").TextContent;
            Assert.Contains("Unsaved:", bar);
            Assert.Contains("Level 7 → 10", bar);
            Assert.Contains(" · ", bar);
        });
        Assert.False(cut.Find("button.save").HasAttribute("disabled"));
    }

    [Fact]
    public void Save_shows_saved_banner_and_clears_pending()
    {
        var editor = Start();
        editor.Open(C(0));
        editor.SetLevel(10);
        var cut = _ctx.Render<Editor>();

        cut.Find("button.save").Click();

        Assert.Equal("Saved. Backup kept.", cut.Find(".banner").TextContent.Trim());
        Assert.Empty(editor.Current.Pending);
        Assert.Single(editor.Backups());
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".pending")));
    }

    [Fact]
    public void ChangedOnDisk_offers_reload()
    {
        var editor = Start();
        editor.Open(C(2));
        editor.SetLevel(10);
        File.WriteAllBytes(C(2), File.ReadAllBytes(C(1)));
        var cut = _ctx.Render<Editor>();

        cut.Find("button.save").Click();

        Assert.Contains("The file changed since you opened it.", cut.Find(".banner").TextContent);
        cut.Find("button.reload").Click();
        Assert.Empty(editor.Current.Pending);
        Assert.Equal(18, editor.Current.Level);
        Assert.Empty(cut.FindAll(".banner"));
    }

    [Fact]
    public void VerifyFailed_banner_says_the_file_was_restored()
    {
        var editor = Start();
        editor.Open(C(2));
        editor.SetLevel(10);
        editor.OpenSession!.VerifyOverride = () => false;
        var cut = _ctx.Render<Editor>();

        cut.Find("button.save").Click();

        Assert.Equal("Save check failed; your file was restored from the backup.", cut.Find(".banner").TextContent.Trim());
    }

    [Fact]
    public void ReadOnly_session_shows_reason_and_no_save()
    {
        File.WriteAllBytes(C(2), Fixtures.PatchBlock(File.ReadAllBytes(C(2)), "player_character", b => b[10] = 6));
        var editor = Start();
        editor.Open(C(2));
        var cut = _ctx.Render<Editor>();

        Assert.Contains("This save is read-only: " + editor.Current.ReadOnlyReason, cut.Find(".banner").TextContent);
        Assert.True(cut.Find("button.save").HasAttribute("disabled"));
    }

    [Fact]
    public void Backups_dialog_restores_after_confirmation()
    {
        var editor = Start();
        editor.Open(C(2));
        editor.SetLevel(10);
        Assert.Equal(SaveResult.Saved, editor.Save());
        var cut = _ctx.Render<BackupsDialog>();
        Assert.Single(cut.FindAll(".backup-list li"));

        cut.Find("button.restore").Click();
        Assert.Equal(10, editor.Current.Level);            // asked first, nothing restored yet
        cut.Find("button.confirm-restore").Click();

        Assert.Equal(7, editor.Current.Level);
    }

    [Fact]
    public void NumberField_rejects_non_numeric_input()
    {
        var calls = new List<long>();
        var cut = _ctx.Render<NumberField>(p => p
            .Add(x => x.Value, 5L).Add(x => x.Min, 1L).Add(x => x.Max, 55L)
            .Add(x => x.OnCommit, (long v) => calls.Add(v)));

        cut.Find("input").Change("12a");
        Assert.Equal("Enter a whole number", cut.Find(".field-error").TextContent);
        cut.Find("input").Change("99");
        Assert.Equal("Enter a number from 1 to 55", cut.Find(".field-error").TextContent);
        Assert.Empty(calls);

        cut.Find("input").Change("12");
        Assert.Empty(cut.FindAll(".field-error"));
        Assert.Equal([12L], calls);
    }

    [Fact]
    public void Stepper_steps_and_disables_at_the_limits()
    {
        var calls = new List<long>();
        var cut = _ctx.Render<Stepper>(p => p
            .Add(x => x.Value, 1L).Add(x => x.Min, 1L).Add(x => x.Max, 3L)
            .Add(x => x.Chips, [("Max", 3L)])
            .Add(x => x.OnCommit, (long v) => calls.Add(v)));

        Assert.True(cut.Find("button.minus").HasAttribute("disabled"));
        cut.Find("button.plus").Click();
        cut.Find("button.chip").Click();
        Assert.Equal([2L, 3L], calls);
    }
}
