using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Editing;
using EnshroudedSaveEditor.Core.Files;
using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.Core.Records;
using EnshroudedSaveEditor.GameData;
using EnshroudedSaveEditor.UI.Services;

namespace EnshroudedSaveEditor.Tests;

/// <summary>
/// Owner's rule: every test works on a temp copy of the fixtures (<see cref="Fixtures.CopyToTemp"/>) with a temp app
/// data root and a <see cref="FakeProbe"/>; nothing points at the real save folders.
/// </summary>
public sealed class EditorServiceTests : IDisposable
{
    public sealed class FakeProbe : IProcessProbe
    {
        public bool Game { get; set; }
        public bool Steam { get; set; }
        public bool IsGameRunning() => Game;
        public bool IsSteamRunning() => Steam;
    }

    /// <summary>The reference data with an XP table.</summary>
    sealed class WithXpTable(IGameData inner, IReadOnlyList<uint> xp) : IGameData
    {
        public IItemCatalog Items => inner.Items;
        public SkillGraph Tree => inner.Tree;
        public IReadOnlyDictionary<uint, SkillReward> Rewards => inner.Rewards;
        public int LevelCap => inner.LevelCap;
        public IReadOnlyList<uint>? XpPerLevel => xp;
        public string Source => "test";
    }

    const uint CopperOre = 0x2b9d5b6f;          // max 250
    const uint WolfsSnarlLongbow = 0x29433573;  // Weapons/RangedBow
    const uint AdventurerHelmet = 0xff1b25e5;   // Equipment/Helmet
    const uint FireballI = 0xe988b200;          // character2 Inventory 30, ×87
    const uint ScorchingWand = 0xe589d7a4;      // character2 Inventory 16, flags 33, durability 352/363
    const uint AlchemistHat = 0xe5a9ac59;       // character2 Equipped position 5
    const uint WizardNode = 1012172970;         // three-rank passive; rank 0 on character2

    readonly string _root = Fixtures.CopyToTemp();
    readonly string _appData = Path.Combine(Path.GetTempPath(), "enshrouded-appdata-" + Guid.NewGuid().ToString("N"));
    readonly FakeProbe _probe = new();
    readonly ReferenceGameData _game = ReferenceGameData.Load();
    readonly List<string> _extraDirs = [];

    public void Dispose()
    {
        foreach (var d in _extraDirs.Append(_root).Append(_appData))
            try { Directory.Delete(d, true); } catch { }
    }

    EditorService Start(Func<IGameData>? data = null, Func<string?>? pickFolder = null)
    {
        var options = new EditorOptions([_root], _appData, _probe, pickFolder);
        return new EditorService(options, new GameDataProvider(data ?? ReferenceGameData.Load), new IconService(options.IconCacheRoot));
    }

    string C(int n) => Path.Combine(_root, $"character{n}.ksd");
    static ItemRef Inv(byte slot) => new(ContainerKind.Inventory, slot);
    static ItemRef Eq(byte position) => new(ContainerKind.Equipped, position);
    static ItemRef Extra(byte slot) => new(ContainerKind.Extra, slot);
    static Func<uint, bool> IsItem => Fixtures.ItemIds().Contains;

    static IEnumerable<SlotView> AllSlots(SessionView v) => v.Equipped.Concat(v.Backpack).Concat(v.Hotbar).Concat(v.Overflow);
    static ItemView ItemAt(SessionView v, ItemRef r) => AllSlots(v).Single(s => s.Ref == r).Item!;
    static EditRejectedException Rejected(Action edit) => Assert.Throws<EditRejectedException>(edit);

    /// <summary>Changes the temp copy <paramref name="path"/> with Core directly, the way a game save would.</summary>
    void Plant(string path, Action<SaveSession> change)
    {
        var s = SaveSession.Open(path, IsItem);
        change(s);
        s.MarkChanged();
        var backups = Path.Combine(Path.GetTempPath(), "enshrouded-plant-" + Guid.NewGuid().ToString("N"));
        _extraDirs.Add(backups);
        Assert.Equal(SaveResult.Saved, s.Save(new BackupService(backups), new FakeProbe()));
    }

    [Fact]
    public void Characters_list_three_with_unspent_points()
    {
        var list = Start().Characters();

        Assert.Equal(["character2.ksd", "character1.ksd", "character0.ksd"], list.Select(c => c.FileName));
        Assert.Equal(new int?[] { 0, 69, 40 }, list.Select(c => c.UnspentPoints));
        Assert.Equal([7, 18, 12], list.Select(c => c.Level));
        Assert.All(list, c => Assert.Equal(("Amadis", CharacterStatus.Ok, (string?)null), (c.Name, c.Status, c.StatusMessage)));
        Assert.All(list, c => Assert.Equal(DateTimeKind.Local, c.SavedAtLocal.Kind));
        Assert.Equal(C(2), list[0].Path);
    }

    [Fact]
    public void Open_edit_save_round_trip()
    {
        var e = Start();
        var opened = e.Open(C(2));
        Assert.True(e.IsOpen);
        Assert.Equal(("Amadis", 7, 50, "character2.ksd", InventoryStatus.Full), (opened.Name, opened.Level, opened.LevelCap, opened.FileName, opened.Inventory));
        Assert.Empty(opened.Pending);
        Assert.False(opened.CanUndo);

        var edited = e.SetLevel(10);
        Assert.Equal(["Level 7 → 10"], edited.Pending);
        Assert.Equal(6, edited.Points.Available);
        Assert.True(edited.CanUndo);

        Assert.Equal(SaveResult.Saved, e.Save());
        Assert.Empty(e.Current.Pending);
        Assert.False(e.Current.CanUndo);

        var reopened = Start().Open(C(2));
        Assert.Equal(10, reopened.Level);
        Assert.Empty(reopened.Pending);
    }

    [Fact]
    public void Undo_reverts_last_edit_only()
    {
        var e = Start();
        var xp = e.Open(C(2)).Xp;
        e.SetLevel(10);
        Assert.Equal(["Level 7 → 10", $"XP {xp} → 0"], e.SetXp(0).Pending);

        var once = e.Undo();
        Assert.Equal((10, xp), (once.Level, once.Xp));
        Assert.Equal(["Level 7 → 10"], once.Pending);
        Assert.True(once.CanUndo);

        var twice = e.Undo();
        Assert.Equal(7, twice.Level);
        Assert.Empty(twice.Pending);
        Assert.False(twice.CanUndo);
        Assert.Contains("Nothing to undo", Rejected(() => e.Undo()).Message);
    }

    [Fact]
    public void Undo_reverts_inventory_and_skill_edits()
    {
        var e = Start();
        var before = e.Open(C(2));
        var (_, added) = e.AddItem(CopperOre, 10, null, null, null);
        e.RefundAll();

        var undone = e.Undo();
        Assert.Equal(before.Points, undone.Points);
        Assert.Equal(15, undone.Nodes.Count(n => n.Type != SkillNodeType.Spark && n.State == NodeState.Learned));
        Assert.Equal(CopperOre, ItemAt(undone, added).Id);

        var clean = e.Undo();
        Assert.Null(AllSlots(clean).Single(s => s.Ref == added).Item);
        Assert.Equal(SaveResult.Saved, e.Save());
        var original = KsdFile.Parse(Fixtures.Read("character2.ksd"));
        var saved = KsdFile.Parse(File.ReadAllBytes(C(2)));
        foreach (var section in new[] { "character_data", "player_character", "player_knowledge" })
            Assert.Equal(original[section].GetData(), saved[section].GetData());
    }

    [Fact]
    public void Undo_keeps_the_last_50_edits()
    {
        var e = Start();
        e.Open(C(2));
        for (var xp = 1; xp <= 60; xp++) e.SetXp(xp);

        for (var i = 0; i < 50; i++) e.Undo();

        Assert.Equal(10u, e.Current.Xp);
        Assert.Equal(10, e.Current.Pending.Count);
        Assert.False(e.Current.CanUndo);
        Rejected(() => e.Undo());
    }

    [Fact]
    public void Reload_discards_unsaved_edits()
    {
        var e = Start();
        e.Open(C(2));
        e.SetLevel(10);
        e.SetName("Other");

        var reloaded = e.Reload();

        Assert.Equal((7, "Amadis"), (reloaded.Level, reloaded.Name));
        Assert.Empty(reloaded.Pending);
        Assert.False(reloaded.CanUndo);
    }

    [Fact]
    public void Unsaved_edits_count_what_closing_the_window_would_drop()
    {
        var e = Start();
        Assert.Equal(0, e.UnsavedEdits);                                  // nothing open
        e.Open(C(2));
        Assert.Equal(0, e.UnsavedEdits);

        e.SetLevel(10);
        e.SetName("Other");
        Assert.Equal(2, e.UnsavedEdits);
        e.Undo();
        Assert.Equal(1, e.UnsavedEdits);

        Assert.Equal(SaveResult.Saved, e.Save());
        Assert.Equal(0, e.UnsavedEdits);
        e.SetLevel(11);
        e.Close();
        Assert.Equal(0, e.UnsavedEdits);
    }

    [Fact]
    public void Edit_rejections_carry_the_message()
    {
        var e = Start();
        e.Open(C(2));

        Assert.Contains("1", Rejected(() => e.SetCount(Inv(30), 0)).Message);
        Assert.Equal("The name can be at most 15 characters.", Rejected(() => e.SetName(new string('x', 16))).Message);
        Assert.Equal("Already 7", Rejected(() => e.SetLevel(7)).Message);
        Assert.Equal("There's no item in that slot.", Rejected(() => e.RemoveItem(Inv(35))).Message);

        Assert.Empty(e.Current.Pending);
        Assert.False(e.Current.CanUndo);
    }

    [Fact]
    public void Save_reports_gameRunning()
    {
        var e = Start();
        e.Open(C(2));
        e.SetLevel(10);
        var before = File.ReadAllBytes(C(2));
        _probe.Game = true;

        Assert.True(e.GameRunning());
        Assert.Equal(SaveResult.GameRunning, e.Save());

        Assert.Equal(before, File.ReadAllBytes(C(2)));
        Assert.Empty(e.Backups());
        Assert.Equal(["Level 7 → 10"], e.Current.Pending);
        Assert.True(e.Current.CanUndo);
    }

    [Fact]
    public void Save_reports_changedOnDisk()
    {
        var e = Start();
        e.Open(C(2));
        e.SetLevel(10);
        var other = File.ReadAllBytes(C(1));
        File.WriteAllBytes(C(2), other);

        Assert.Equal(SaveResult.ChangedOnDisk, e.Save());

        Assert.Equal(other, File.ReadAllBytes(C(2)));
        Assert.Equal(["Level 7 → 10"], e.Current.Pending);
        Assert.Equal(18, e.Reload().Level);
    }

    [Fact]
    public void Backups_are_listed_and_restorable()
    {
        var e = Start();
        e.Open(C(2));
        Assert.Empty(e.Backups());
        e.SetLevel(10);
        e.Save();
        var backups = e.Backups();
        var backup = Assert.Single(backups);
        Assert.StartsWith(Path.Combine(_appData, "backups"), backup.Path);

        Assert.Equal("That backup doesn't belong to this character", Rejected(() => e.Restore(C(1))).Message);
        _probe.Game = true;
        Assert.Contains("Close it", Rejected(() => e.Restore(backup.Path)).Message);
        _probe.Game = false;

        var restored = e.Restore(backup.Path);

        Assert.Equal(7, restored.Level);
        Assert.Empty(restored.Pending);
        Assert.Equal(2, e.Backups().Count);
        Assert.Equal(File.ReadAllBytes(Path.Combine(Fixtures.RealRemote, "character2.ksd")), File.ReadAllBytes(C(2)));
    }

    [Fact]
    public void A_save_the_records_cant_express_is_refused_and_writes_nothing()
    {
        var e = Start();
        e.Open(C(2));
        e.SetLevel(10);
        var before = File.ReadAllBytes(C(2));
        var equipped = e.OpenSession!.Player.Container(ContainerKind.Equipped)!.Records;
        var slot = equipped[1].Slot;
        equipped[1].Slot = equipped[0].Slot;                               // PlayerCharacter.ToBytes refuses this

        var ex = Rejected(() => e.Save());

        Assert.Equal("Couldn't save these edits: Equipped slots must be in increasing order.", ex.Message);
        Assert.Equal(before, File.ReadAllBytes(C(2)));
        equipped[1].Slot = slot;
        Assert.Equal(["Level 7 → 10"], e.Current.Pending);
        Assert.Equal(SaveResult.Saved, e.Save());
        Assert.Equal(10, Start().Open(C(2)).Level);
    }

    // Review fix 1: after a failed save, undoing back to the opened state and saving must write that state.
    [Fact]
    public void A_failed_verify_then_undo_saves_the_undone_state()
    {
        var e = Start();
        e.Open(C(2));
        e.SetLevel(10);
        e.OpenSession!.VerifyOverride = () => false;

        Assert.Equal(SaveResult.VerifyFailed, e.Save());
        Assert.Equal(["Level 7 → 10"], e.Current.Pending);
        Assert.Equal(Fixtures.Read("character2.ksd"), File.ReadAllBytes(C(2)));

        e.Undo();
        e.OpenSession!.VerifyOverride = null;
        Assert.Equal(SaveResult.Saved, e.Save());

        Assert.Equal(7u, SaveSession.Open(C(2), IsItem).Player.Level);
        Assert.Equal(7, e.Current.Level);
        Assert.Equal(7, Start().Open(C(2)).Level);
    }

    // Review fix 2: the Steam and local folders both hold character0.ksd.
    [Fact]
    public void Same_named_characters_in_other_folders_keep_their_own_backups()
    {
        var local = Fixtures.CopyLocalToTemp();
        _extraDirs.Add(local);
        var options = new EditorOptions([_root, local], _appData, _probe, null);
        var e = new EditorService(options, new GameDataProvider(ReferenceGameData.Load), new IconService(options.IconCacheRoot));
        var localC0 = Path.Combine(local, "character0.ksd");
        e.Open(C(0));
        e.SetLevel(13);
        Assert.Equal(SaveResult.Saved, e.Save());
        var steamBackup = Assert.Single(e.Backups());
        var localBytes = File.ReadAllBytes(localC0);

        e.Open(localC0);

        Assert.Empty(e.Backups());
        Assert.Equal("That backup doesn't belong to this character", Rejected(() => e.Restore(steamBackup.Path)).Message);
        Assert.Equal(localBytes, File.ReadAllBytes(localC0));
    }

    [Fact]
    public void Restore_refuses_a_backup_of_another_character()
    {
        var e = Start();
        e.Open(C(2));
        e.SetLevel(10);
        e.Save();
        var backup = Assert.Single(e.Backups());
        var other = File.ReadAllBytes(C(1));
        Assert.NotEqual(KsdFile.Parse(other).Guid, KsdFile.Parse(File.ReadAllBytes(backup.Path)).Guid);
        File.WriteAllBytes(C(2), other);                                    // the file now holds another character
        e.Reload();

        var ex = Rejected(() => e.Restore(backup.Path));

        Assert.Equal("Couldn't restore that backup: it belongs to a different character (the save GUIDs differ).", ex.Message);
        Assert.Equal(other, File.ReadAllBytes(C(2)));
    }

    [Fact]
    public void Catalog_excludes_customization_and_has_icons()
    {
        var e = Start();
        var catalog = e.Catalog();

        Assert.DoesNotContain(catalog, c => c.Category == ItemCategory.Customization);
        Assert.Equal(_game.Items.All.Count(i => i.Category != ItemCategory.Customization), catalog.Count);
        Assert.All(catalog, c => Assert.StartsWith("https://icons.ese/", c.IconUrl));
        var iconRoot = new EditorOptions([_root], _appData, _probe, null).IconCacheRoot;
        Assert.All(catalog.Select(c => c.IconUrl).Distinct(),
            url => Assert.True(File.Exists(Path.Combine(iconRoot, url["https://icons.ese/".Length..])), url));

        var bow = catalog.Single(c => c.Id == WolfsSnarlLongbow);
        Assert.Equal(("Wolf's Snarl Longbow", ItemCategory.Weapons, "RangedBow", 1, 50), (bow.Name, bow.Category, bow.SubCategory, bow.MinLevel, bow.MaxLevel));
        Assert.Equal("Helmet", catalog.Single(c => c.Id == AdventurerHelmet).SubCategory);
        var ore = catalog.Single(c => c.Id == CopperOre);
        Assert.Equal(("Copper Ore", "", 250, Rarity.Common), (ore.Name, ore.SubCategory, ore.MaxStack, ore.BaseRarity));
        Assert.Equal(catalog.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Id).Select(c => c.Id), catalog.Select(c => c.Id));
    }

    /// <summary>Hands out a 1×1 icon for every id and counts how often it is asked.</summary>
    sealed class CountingTextures : IIconTextureSource
    {
        public int Calls;
        public string Version => "test";
        public IconTexture? ItemTexture(uint id) => Count();
        public IconTexture? SkillTexture(uint nodeId) => Count();

        IconTexture Count()
        {
            Interlocked.Increment(ref Calls);
            return new IconTexture(1, 1, [1, 2, 3, 255]);
        }
    }

    [Fact]
    public void Catalog_extracts_an_item_icon_only_when_it_is_shown()
    {
        var options = new EditorOptions([_root], _appData, _probe, null);
        var textures = new CountingTextures();
        var e = new EditorService(options, new GameDataProvider(ReferenceGameData.Load), new IconService(options.IconCacheRoot, textures));

        var catalog = e.Catalog();
        Assert.Equal(0, textures.Calls);

        Assert.Equal($"https://icons.ese/item/{catalog[0].Id:x8}.png", catalog[0].IconUrl);
        Assert.Equal(catalog[0].IconUrl, catalog[0].IconUrl);
        Assert.Equal(1, textures.Calls);
    }

    [Fact]
    public void AddItem_returns_ref_for_immediate_stack_edit()
    {
        var e = Start();
        e.Open(C(2));

        var (view, added) = e.AddItem(CopperOre, 10, null, null, null);
        Assert.Equal(["Added Copper Ore ×10"], view.Pending);
        Assert.Equal((CopperOre, 10), (ItemAt(view, added).Id, ItemAt(view, added).Count));

        var edited = e.SetCount(added, 37);
        Assert.Equal(["Added Copper Ore ×10", "Copper Ore 10 → 37"], edited.Pending);
        Assert.Equal(37, ItemAt(edited, added).Count);

        var (bowView, bowRef) = e.AddItem(WolfsSnarlLongbow, 1, 20, Rarity.Epic, Inv(37));
        Assert.Equal(Inv(37), bowRef);
        Assert.Equal("Added Wolf's Snarl Longbow", bowView.Pending[^1]);
        Assert.Equal((20, Rarity.Epic), (ItemAt(bowView, bowRef).Level, ItemAt(bowView, bowRef).Rarity));

        var hair = _game.Items.All.First(i => i.Category == ItemCategory.Customization);
        Assert.Equal("Customization items can't be added.", Rejected(() => e.AddItem(hair.Id, 1, null, null, null)).Message);
        Assert.Equal("Unknown item.", Rejected(() => e.AddItem(0xdeadbeef, 1, null, null, null)).Message);
    }

    [Fact]
    public void AddFields_tell_which_fields_an_added_record_gets()
    {
        var e = Start();
        e.Open(C(2));
        const uint torch = 0xcf128197;                                        // character2 has one: a level, no rarity

        Assert.Equal(((int?)null, false), e.AddFields(CopperOre));
        Assert.Equal((7, true), e.AddFields(WolfsSnarlLongbow));             // the character's level, within 1–50
        Assert.Equal((1, false), e.AddFields(torch));                         // fixed at 1
        Assert.Equal((5, true), e.AddFields(AlchemistHat));                   // its base, not the worn hat's 10
        Assert.Equal(((int?)null, false), e.AddFields(0xdeadbeef));
        Assert.Equal(((int?)null, false), e.AddFields(_game.Items.All.First(i => i.Category == ItemCategory.Customization).Id));
        Assert.Empty(e.Current.Pending);                                      // nothing changes
        var (bow, at) = e.AddItem(WolfsSnarlLongbow, 1, 20, Rarity.Rare, null);
        Assert.Equal((20, true), (ItemAt(bow, at).Level, ItemAt(bow, at).HasRarity));
        var (torchView, torchAt) = e.AddItem(torch, 1, 1, null, null);
        Assert.Equal((1, false), (ItemAt(torchView, torchAt).Level, ItemAt(torchView, torchAt).HasRarity));
        Assert.Equal("Weapon_T1_2H_BowEpic_02", e.Catalog().Single(c => c.Id == WolfsSnarlLongbow).InternalName);
    }

    [Fact]
    public void Inventory_edits_log_one_line_each()
    {
        var e = Start();
        var v = e.Open(C(2));
        var wand = ItemAt(v, Inv(16));
        Assert.Equal((ScorchingWand, 352u, 363u), (wand.Id, wand.Durability, wand.MaxDurability));
        var level = wand.Level!.Value;
        var rarity = wand.Rarity!.Value;
        var otherRarity = rarity == Rarity.Legendary ? Rarity.Epic : rarity + 1;

        e.SetItemLevel(Inv(16), level + 1);
        e.SetRarity(Inv(16), otherRarity);
        e.SetDurability(Inv(16), 363, 363);
        e.MoveItem(Inv(30), Inv(35));
        e.MoveItem(Inv(35), Inv(22));
        var last = e.RemoveItem(Inv(22));

        Assert.Equal(
        [
            $"Scorching Wand level {level} → {level + 1}",
            $"Scorching Wand {rarity} → {otherRarity}",
            "Scorching Wand durability 352/363 → 363/363",
            "Moved Fireball I",
            "Swapped Fireball I and Health Potion",
            "Removed Fireball I ×87",
        ], last.Pending);
        Assert.Null(AllSlots(last).Single(s => s.Ref == Inv(22)).Item);
        Assert.Equal("Health Potion", ItemAt(last, Inv(35)).Name);
        Assert.Equal((level + 1, otherRarity, 363u), (ItemAt(last, Inv(16)).Level, ItemAt(last, Inv(16)).Rarity, ItemAt(last, Inv(16)).Durability));
    }

    [Fact]
    public void Character_edits_log_one_line_each()
    {
        var e = Start();
        var xp = e.Open(C(2)).Xp;
        e.SetName("  Xena ");
        e.SetXp(0);
        var v = e.SetLevel(10);
        Assert.Equal(["Name Amadis → Xena", $"XP {xp} → 0", "Level 7 → 10"], v.Pending);
        Assert.Equal(("Xena", 0u, 10), (v.Name, v.Xp, v.Level));
    }

    [Fact]
    public void Skill_edits_follow_the_game_rules_and_log_one_line_each()
    {
        var e = Start();
        var v = e.Open(C(1));
        Assert.Equal(69, v.Points.Available);
        var tree = _game.Tree;
        var sparks = tree.Sparks.Select(s => s.Id).ToHashSet();
        // a: next to a Spark; b: next to a but not to any Spark, so it hangs on a alone.
        var (a, b) = (from n in tree.Nodes
                      where n.Type != SkillNodeType.Spark && tree.Neighbors(n.Id).Any(x => sparks.Contains(x.Id))
                      from m in tree.Neighbors(n.Id)
                      where m.Type != SkillNodeType.Spark && !tree.Neighbors(m.Id).Any(x => sparks.Contains(x.Id))
                      select (n, m)).First();

        Assert.Equal(NodeState.Locked, v.Nodes.Single(x => x.Id == b.Id).State);
        Assert.Equal("Connect it to a learned skill first", Rejected(() => e.Learn(b.Id)).Message);
        e.Learn(a.Id);
        var learned = e.Learn(b.Id);
        Assert.Equal(NodeState.Learned, learned.Nodes.Single(x => x.Id == b.Id).State);
        Assert.Equal(69 - a.Cost - b.Cost, learned.Points.Available);
        Assert.Equal([b.Id], e.DependentsOf(a.Id));

        var cascaded = e.Unlearn(a.Id);
        Assert.Equal(NodeState.Locked, cascaded.Nodes.Single(x => x.Id == b.Id).State);
        Assert.Equal(69, cascaded.Points.Available);
        e.Learn(a.Id);
        e.Unlearn(a.Id);
        e.Learn(a.Id);
        var refunded = e.RefundAll();

        Assert.Equal(
        [
            $"Learned {a.Name}", $"Learned {b.Name}", $"Unlearned {a.Name} (+1 more)", $"Learned {a.Name}",
            $"Unlearned {a.Name}", $"Learned {a.Name}", "Refunded all skills",
        ], refunded.Pending);
        Assert.Equal(69, refunded.Points.Available);
        Assert.Equal("Nothing to refund", Rejected(() => e.RefundAll()).Message);
    }

    [Fact]
    public void Ranking_up_logs_the_new_rank()
    {
        var e = Start();
        e.Open(C(1));
        var tree = _game.Tree;
        var node = tree.Nodes.First(n => n.Type != SkillNodeType.Spark && n.MaxRank > 1
                                         && tree.Neighbors(n.Id).Any(x => x.Type == SkillNodeType.Spark));
        e.Learn(node.Id);
        var v = e.Learn(node.Id);
        Assert.Equal($"Learned {node.Name} (rank 2)", v.Pending[^1]);
        Assert.Equal(2, v.Nodes.Single(x => x.Id == node.Id).Rank);
    }

    [Fact]
    public void Skills_view_marks_states_and_rank0_as_not_learned()
    {
        var v = Start().Open(C(2));

        Assert.Equal(243, v.Nodes.Count);
        Assert.Equal(12, v.Nodes.Count(n => n.Type == SkillNodeType.Spark && n.State == NodeState.Learned));
        Assert.Equal(15, v.Nodes.Count(n => n.Type != SkillNodeType.Spark && n.State == NodeState.Learned));
        var wizard = v.Nodes.Single(n => n.Name == "Wizard");
        Assert.Equal((WizardNode, 0, 3), (wizard.Id, wizard.Rank, wizard.MaxRank));
        Assert.Contains(wizard.State, new[] { NodeState.Available, NodeState.Locked });
        Assert.Equal((312, 12), (v.Links.Count, v.Labels.Count));
        Assert.Equal(new PointsSummary(12, 10, 22, 0), v.Points);
    }

    [Fact]
    public void Views_follow_the_inventory_layout()
    {
        var v = Start().Open(C(2));

        Assert.Equal(InventoryLayout.EquipPositions.Select(p => (Eq(p.Index), (string?)p.Label)), v.Equipped.Select(s => (s.Ref, s.Label)));
        Assert.Equal(AlchemistHat, v.Equipped[5].Item!.Id);
        Assert.All(v.Equipped.Take(5), s => Assert.Null(s.Item));          // character2: mask ffe0
        Assert.Equal(InventoryLayout.BackpackGrid(16), v.Backpack.Select(s => s.Ref));   // Small Backpack _NEW: 2 rows
        Assert.Equal(InventoryLayout.HotbarSlots.Select(Inv), v.Hotbar.Select(s => s.Ref));
        Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8"], v.Hotbar.Take(8).Select(s => s.Label));
        Assert.All(v.Hotbar.Skip(8), s => Assert.Null(s.Label));
        Assert.Null(AllSlots(v).Single(s => s.Ref == Inv(15)).Item);       // a ghost is an empty slot
        Assert.Empty(v.Overflow);
        var e = Start();
        Assert.Equal(32, e.Open(C(0)).Backpack.Count);                       // one extension row
        Assert.Empty(e.Current.Overflow);
        Assert.Empty(e.Open(C(1)).Overflow);
        var fireball = ItemAt(v, Inv(30));
        Assert.Equal((FireballI, "Fireball I", 87, 500, true), (fireball.Id, fireball.Name, fireball.Count, fireball.MaxStack, fireball.Known));
        Assert.StartsWith("https://icons.ese/glyph/", fireball.IconUrl);
    }

    [Fact]
    public void Extra_records_past_the_extension_are_listed_as_overflow()
    {
        Plant(C(2), s =>
        {
            s.Player.Container(ContainerKind.Extra)!.Records.Add(new ItemRecord(20, CopperOre, 5));
            s.Player.Container(ContainerKind.Extra)!.Records.Add(new ItemRecord(21, CopperOre, 0));   // a ghost
        });

        var v = Start().Open(C(2));

        var overflow = Assert.Single(v.Overflow);
        Assert.Equal((Extra(20), CopperOre, 5), (overflow.Ref, overflow.Item!.Id, overflow.Item.Count));
        Assert.DoesNotContain(v.Backpack, s => s.Ref == Extra(20));
    }

    [Fact]
    public void Unknown_items_show_their_id()
    {
        var e = Start();
        var slots = new[] { 0, 1, 2 }.SelectMany(n => AllSlots(e.Open(C(n))).Select(s => (n, s))).ToList();
        var unknown = slots.Where(x => x.s.Item is { Known: false }).ToList();

        // character1's appearance pieces and one character0 action-bar item are missing from the reference data.
        Assert.Equal([(0, Inv(25)), (1, Eq(0)), (1, Eq(1)), (1, Eq(2)), (1, Eq(3)), (1, Eq(4))], unknown.Select(x => (x.n, x.s.Ref)));
        Assert.Equal("Unknown item 01e879a5", unknown[1].s.Item!.Name);
        Assert.All(unknown.Select(x => x.s.Item!), i =>
        {
            Assert.Equal($"Unknown item {i.Id:x8}", i.Name);
            Assert.Null(i.Category);
            Assert.Null(_game.Items.Find(i.Id));
            Assert.Equal("https://icons.ese/glyph/unknown.svg", i.IconUrl);
        });
    }

    [Fact]
    public void Customization_records_are_hidden()
    {
        var hair = _game.Items.All.First(i => i.Category == ItemCategory.Customization);
        Plant(C(2), s => s.Player.Container(ContainerKind.Inventory)!.Records.Single(r => r.Slot == 30).ItemId = hair.Id);

        var e = Start();
        foreach (var n in new[] { 0, 1, 2 })
            Assert.DoesNotContain(AllSlots(e.Open(C(n))), s => s.Item?.Category == ItemCategory.Customization);
        var v = e.Open(C(2));
        Assert.Null(AllSlots(v).Single(s => s.Ref == Inv(30)).Item);

        // carried through untouched
        e.SetLevel(10);
        Assert.Equal(SaveResult.Saved, e.Save());
        var saved = SaveSession.Open(C(2), IsItem);
        Assert.Equal(hair.Id, saved.Player.Container(ContainerKind.Inventory)!.Records.Single(r => r.Slot == 30).ItemId);
    }

    [Fact]
    public void InPlaceOnly_character_allows_only_in_place_edits()
    {
        File.WriteAllBytes(C(2), Fixtures.InPlaceOnlyCharacter2());
        var e = Start();
        var v = e.Open(C(2));
        Assert.Equal(InventoryStatus.InPlaceOnly, v.Inventory);
        Assert.Equal(AlchemistHat, v.Equipped[5].Item!.Id);

        Assert.Equal("Adding and moving items isn't available for this character yet",
            Rejected(() => e.AddItem(CopperOre, 1, null, null, null)).Message);
        Rejected(() => e.MoveItem(Inv(30), Inv(35)));
        e.SetCount(Inv(30), 100);
        e.SetCount(Inv(30), 120);
        var undone = e.Undo();
        Assert.Equal(InventoryStatus.InPlaceOnly, undone.Inventory);
        Assert.Equal(100, ItemAt(undone, Inv(30)).Count);

        Assert.Equal(SaveResult.Saved, e.Save());
        var reopened = Start().Open(C(2));
        Assert.Equal((InventoryStatus.InPlaceOnly, 100), (reopened.Inventory, ItemAt(reopened, Inv(30)).Count));
    }

    [Fact]
    public void Fresh_local_character_opens_with_its_one_equipped_piece()
    {
        var local = Fixtures.CopyLocalToTemp();
        _extraDirs.Add(local);
        var options = new EditorOptions([local], _appData, _probe, null);
        var e = new EditorService(options, new GameDataProvider(ReferenceGameData.Load), new IconService(options.IconCacheRoot));

        var c = Assert.Single(e.Characters());
        Assert.Equal(("Test", 1, 0), (c.Name, c.Level, c.UnspentPoints));
        var v = e.Open(c.Path);

        Assert.Equal(InventoryStatus.InPlaceOnly, v.Inventory);
        var worn = Assert.Single(v.Equipped, s => s.Item is not null);
        Assert.Equal((Eq(8), "Legs", "Threadbare Pants"), (worn.Ref, worn.Label, worn.Item!.Name));
        Assert.Equal(24, v.Backpack.Count);                                  // no backpack: no extension
        Assert.All(v.Backpack.Concat(v.Hotbar), s => Assert.Null(s.Item));
    }

    [Fact]
    public void Xp_to_next_level_comes_from_the_xp_table()
    {
        Assert.Null(Start().Open(C(2)).XpToNextLevel);                    // the reference data has no XP table

        var table = Enumerable.Range(1, 49).Select(i => (uint)(i * 1000)).ToArray();
        var e = Start(() => new WithXpTable(ReferenceGameData.Load(), table));
        Assert.Equal(7000u, e.Open(C(2)).XpToNextLevel);                  // level 7 → 8
        Assert.Null(e.SetLevel(50).XpToNextLevel);                        // at the cap
    }

    [Fact]
    public void Open_rejects_an_unreadable_character()
    {
        var file = KsdFile.Parse(File.ReadAllBytes(C(2)));
        file["player_character"].SetData(new byte[10]);
        File.WriteAllBytes(Path.Combine(_root, "character5.ksd"), file.ToBytes());
        var e = Start();
        var listed = e.Characters().Single(c => c.FileName == "character5.ksd");
        Assert.NotEqual(CharacterStatus.Ok, listed.Status);
        Assert.Null(listed.UnspentPoints);

        var ex = Rejected(() => e.Open(listed.Path));

        Assert.Equal("This character couldn't be opened: player_character is too short.", ex.Message);
        Assert.False(e.IsOpen);
        e.Open(C(2));
        Rejected(() => e.Open(listed.Path));
        Assert.Equal("character2.ksd", e.Current.FileName);                // a failed open keeps the open character
    }

    [Fact]
    public void Open_accepts_only_listed_characters()
    {
        var e = Start();
        var elsewhere = Path.Combine(_root, "global.ksd");
        Assert.Equal("That character isn't in the saves folder.", Rejected(() => e.Open(elsewhere)).Message);
        Assert.Equal("character2.ksd", e.Open(C(2).ToUpperInvariant()).FileName);
    }

    [Fact]
    public void ReadOnly_session_rejects_edits()
    {
        File.WriteAllBytes(C(2), Fixtures.PatchBlock(File.ReadAllBytes(C(2)), "player_character", b => b[10] = 6));
        var before = File.ReadAllBytes(C(2));
        var e = Start();

        var v = e.Open(C(2));

        Assert.True(v.ReadOnly);
        Assert.False(string.IsNullOrWhiteSpace(v.ReadOnlyReason));
        Assert.Equal($"This save is read-only: {v.ReadOnlyReason}", Rejected(() => e.SetLevel(10)).Message);
        Assert.StartsWith("This save is read-only: ", Rejected(() => e.AddItem(CopperOre, 1, null, null, null)).Message);
        Assert.StartsWith("This save is read-only: ", Rejected(() => e.RefundAll()).Message);
        Assert.Equal(SaveResult.ReadOnly, e.Save());
        Assert.Equal(before, File.ReadAllBytes(C(2)));
        Assert.Empty(e.Current.Pending);
    }

    [Fact]
    public void Changed_is_raised_by_every_change_and_not_by_a_rejection()
    {
        var e = Start();
        var fired = 0;
        e.Changed += () => fired++;

        e.Open(C(2));
        e.SetLevel(10);
        e.AddItem(CopperOre, 1, null, null, null);
        e.Undo();
        Assert.Equal(4, fired);

        Rejected(() => e.SetLevel(10));
        e.DependentsOf(WizardNode);
        e.Catalog();
        Assert.Equal(4, fired);

        e.Save();
        e.Reload();
        e.Close();
        Assert.Equal(7, fired);
        Assert.False(e.IsOpen);
    }

    [Fact]
    public void Using_the_session_before_opening_fails()
    {
        var e = Start();
        Assert.False(e.IsOpen);
        Assert.Throws<InvalidOperationException>(() => e.Current);
        Assert.Throws<InvalidOperationException>(() => e.SetLevel(10));
        Assert.Throws<InvalidOperationException>(() => e.Save());
        Assert.NotEmpty(e.Catalog());
    }

    [Fact]
    public void Probe_reports_game_and_steam()
    {
        var e = Start();
        Assert.False(e.GameRunning() || e.SteamRunning());
        _probe.Game = true;
        _probe.Steam = true;
        Assert.True(e.GameRunning() && e.SteamRunning());
    }

    [Fact]
    public void A_chosen_saves_folder_is_kept_in_settings()
    {
        var local = Fixtures.CopyLocalToTemp();
        _extraDirs.Add(local);
        var e = Start(pickFolder: () => local);
        Assert.True(e.SavesFound);
        Assert.Equal(3, e.Characters().Count);
        var fired = 0;
        e.Changed += () => fired++;

        Assert.True(e.ChooseSavesFolder());

        Assert.Equal(1, fired);
        Assert.Equal(local, Settings.Load(Path.Combine(_appData, "settings.json")).SaveRoot);
        Assert.Contains(Start().Characters(), c => c.Path == Path.Combine(local, "character0.ksd"));
        Assert.Equal(4, e.Characters().Count);
        Assert.False(Start(pickFolder: () => null).ChooseSavesFolder());
        Assert.False(Start().ChooseSavesFolder());
    }

    [Fact]
    public void Saves_are_not_found_when_no_root_exists()
    {
        var options = new EditorOptions([Path.Combine(_root, "missing")], _appData, _probe, null);
        var e = new EditorService(options, new GameDataProvider(ReferenceGameData.Load), new IconService(options.IconCacheRoot));
        Assert.False(e.SavesFound);
        Assert.Empty(e.Characters());
    }
}
