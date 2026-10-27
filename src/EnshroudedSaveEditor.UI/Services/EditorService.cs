using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Editing;
using EnshroudedSaveEditor.Core.Files;
using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.Core.Records;
using EnshroudedSaveEditor.GameData;

namespace EnshroudedSaveEditor.UI.Services;

/// <param name="SaveRoots">The folders searched for <c>characterN.ksd</c> (<see cref="SaveLocator.DefaultRoots"/> in the app).</param>
/// <param name="AppDataRoot">Holds <c>backups\</c>, <c>cache\icons\</c>, <c>cache\gamedata\</c> and <c>settings.json</c>.</param>
/// <param name="PickFolder">Shows a native folder dialog; supplied by the desktop window.</param>
public sealed record EditorOptions(IReadOnlyList<string> SaveRoots, string AppDataRoot, IProcessProbe Probe, Func<string?>? PickFolder)
{
    /// <summary><c>%LOCALAPPDATA%\EnshroudedSaveEditor</c>.</summary>
    public static string DefaultAppDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EnshroudedSaveEditor");

    public string BackupRoot => Path.Combine(AppDataRoot, "backups");

    /// <summary>The folder the window maps to <c>https://icons.ese/</c>.</summary>
    public string IconCacheRoot => Path.Combine(AppDataRoot, "cache", "icons");

    /// <summary>Where the game data read from the install is cached.</summary>
    public string GameDataCacheRoot => Path.Combine(AppDataRoot, "cache", "gamedata");

    public string SettingsPath => Path.Combine(AppDataRoot, "settings.json");
}

/// <summary>
/// Everything the screens do: list characters, open one, edit it through the Core editors, undo, and save with a
/// backup. Every edit returns the new <see cref="SessionView"/>, adds one line to its <see cref="SessionView.Pending"/>
/// and raises <see cref="Changed"/>. A rejected edit throws <see cref="EditRejectedException"/> with a message for the
/// user and changes nothing (no-op edits are rejected with "Already …"; the UI disables controls already at a value).
/// Using the session before <see cref="Open"/> throws <see cref="InvalidOperationException"/>.
/// </summary>
public sealed class EditorService(EditorOptions options, GameDataProvider game, IconService icons)
{
    /// <summary>How many edits <see cref="Undo"/> can go back.</summary>
    public const int UndoDepth = 50;

    private sealed record UndoStep(SessionSnapshot Before, int PendingCount);

    private readonly Lock _lock = new();
    private readonly BackupService _backups = new(options.BackupRoot);
    private readonly ChangeLog _log = new();
    private readonly LinkedList<UndoStep> _undo = new();
    private SaveSession? _session;
    private IReadOnlyList<CatalogEntryView>? _catalog;

    public event Action? Changed;

    public bool IsOpen
    {
        get
        {
            lock (_lock)
                return _session is not null;
        }
    }

    public SessionView Current
    {
        get
        {
            lock (_lock)
                return View(Session);
        }
    }

    /// <summary>The open character's unsaved edits (0 when none is open): what closing the window would drop.</summary>
    public int UnsavedEdits
    {
        get
        {
            lock (_lock)
                return _session is null ? 0 : _log.Count;
        }
    }

    private SaveSession Session => _session ?? throw new InvalidOperationException("No character is open.");

    /// <summary>Tests only: the open session (e.g. to force a verify failure).</summary>
    internal SaveSession? OpenSession
    {
        get
        {
            lock (_lock)
                return _session;
        }
    }

    private IGameData Data => game.Data;

    private bool IsItem(uint id) => Data.Items.Find(id) is not null;

    /// <summary>Where the game data comes from (<see cref="GameDataProvider.Source"/>), shown on the picker.</summary>
    public string DataSource => game.Source;

    /// <summary>Whether any saves folder exists (the picker offers "Choose saves folder" when none does).</summary>
    public bool SavesFound => Roots().Any(Directory.Exists);

    /// <summary>
    /// Asks for a saves folder (<see cref="EditorOptions.PickFolder"/>) and keeps it in the settings, listed alongside
    /// <see cref="EditorOptions.SaveRoots"/>. False when there's no dialog, it was cancelled, or the folder doesn't exist.
    /// </summary>
    public bool ChooseSavesFolder()
    {
        if (options.PickFolder?.Invoke() is not { Length: > 0 } folder || !Directory.Exists(folder))
            return false;
        var settings = Settings.Load(options.SettingsPath);
        settings.SaveRoot = folder;
        settings.Save(options.SettingsPath);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Every character file in the saves folders, most recently saved first.</summary>
    public IReadOnlyList<CharacterView> Characters() =>
        Scan().Select(c => new CharacterView(
                c.Path, c.FileName, c.Name, c.Level, LocalTime(c.SavedAt), c.Status, c.StatusMessage,
                c.Status == CharacterStatus.Ok ? UnspentPoints(c.Path) : null))
            .ToList();

    /// <summary>
    /// Opens a listed character, dropping any unsaved edits of the open one. A file whose records can't be read is
    /// refused (R5) and the open character stays; one with an unsupported version or a checksum mismatch opens read-only.
    /// </summary>
    public SessionView Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var info = Scan().FirstOrDefault(c => SamePath(c.Path, path))
                   ?? throw new EditRejectedException("That character isn't in the saves folder.");
        SaveSession session;
        try
        {
            session = SaveSession.Open(info.Path, IsItem);
        }
        catch (Exception e) when (e is KsdFormatException or IOException or UnauthorizedAccessException)
        {
            throw new EditRejectedException($"This character couldn't be opened: {e.Message}");
        }
        lock (_lock)
        {
            _session = session;
            _undo.Clear();
            _log.Clear();
        }
        Changed?.Invoke();
        return Current;
    }

    /// <summary>Reads the open character from disk again; unsaved edits and the undo history are dropped.</summary>
    public SessionView Reload()
    {
        string path;
        lock (_lock)
            path = Session.Path;
        return Open(path);
    }

    /// <summary>Back to the picker; unsaved edits are dropped.</summary>
    public void Close()
    {
        lock (_lock)
        {
            _session = null;
            _undo.Clear();
            _log.Clear();
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Writes the edits after a backup (<see cref="SaveSession.Save"/>). Only <see cref="SaveResult.Saved"/> clears the
    /// pending edits and the undo history; every other result leaves the session as it was. A save that can't be written,
    /// or records the save can't hold, is refused with <see cref="EditRejectedException"/>.
    /// </summary>
    public SaveResult Save()
    {
        SaveResult result;
        lock (_lock)
        {
            var session = Session;
            try
            {
                result = session.Save(_backups, options.Probe);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new EditRejectedException($"Couldn't write the save: {e.Message}");
            }
            catch (InvalidOperationException e)
            {
                // The records can't be written as they stand (PlayerCharacter.ToBytes); only the backup was written.
                throw new EditRejectedException($"Couldn't save these edits: {e.Message}");
            }
            if (result == SaveResult.Saved)
            {
                _undo.Clear();
                _log.Clear();
            }
        }
        Changed?.Invoke();
        return result;
    }

    /// <summary>The open character's backups, newest first.</summary>
    public IReadOnlyList<BackupInfo> Backups()
    {
        string path;
        lock (_lock)
            path = Session.Path;
        return _backups.List(path);
    }

    /// <summary>
    /// Puts back one of <see cref="Backups"/> (it backs up the current file first), then reopens the character. Refused
    /// for any other file, for a backup of another character (its save GUID differs), and while the game runs.
    /// </summary>
    public SessionView Restore(string backupPath)
    {
        ArgumentNullException.ThrowIfNull(backupPath);
        string path;
        lock (_lock)
            path = Session.Path;
        var backup = _backups.List(path).FirstOrDefault(b => SamePath(b.Path, backupPath))
                     ?? throw new EditRejectedException("That backup doesn't belong to this character");
        if (options.Probe.IsGameRunning())
            throw new EditRejectedException("Enshrouded is running. Close it to restore a backup.");
        try
        {
            _backups.Restore(backup, path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new EditRejectedException($"Couldn't restore that backup: {e.Message}");
        }
        return Open(path);
    }

    public bool GameRunning() => options.Probe.IsGameRunning();

    public bool SteamRunning() => options.Probe.IsSteamRunning();

    // Character

    public SessionView SetName(string name) => Edit(s =>
    {
        var old = s.Character.Name;
        new CharacterEditor(s).SetName(name);
        return ChangeLog.Name(old, s.Character.Name);
    });

    /// <summary>1 to the game's level cap.</summary>
    public SessionView SetLevel(int level) => Edit(s =>
    {
        var old = s.Player.Level;
        new CharacterEditor(s).SetLevel(level, Data.LevelCap);
        return ChangeLog.Level(old, level);
    });

    public SessionView SetXp(long xp) => Edit(s =>
    {
        var old = s.Player.Xp;
        new CharacterEditor(s).SetXp(xp);
        return ChangeLog.Xp(old, xp);
    });

    // Map

    /// <summary>Reveals the whole map (<see cref="MapEditor.ClearFog"/>).</summary>
    public SessionView ClearFog() => Edit(s =>
    {
        new MapEditor(s).ClearFog();
        return ChangeLog.ClearedFog;
    });

    /// <summary>Discovers every location on the map (<see cref="MapEditor.DiscoverLocations"/>); needs the install's data.</summary>
    public SessionView DiscoverLocations() => Edit(s =>
        ChangeLog.Discovered(new MapEditor(s).DiscoverLocations(MapLocations() ?? throw new EditRejectedException(NoLocationData))));

    private const string NoLocationData = "The map's locations come from your Enshrouded install, and its data couldn't be read.";

    /// <summary>The map's locations, or null when the game data has none (only the install does).</summary>
    private IReadOnlyList<MapLocation>? MapLocations()
    {
        try
        {
            return Data.Locations;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private LocationsView Locations(SaveSession s)
    {
        var map = new MapEditor(s);
        if (MapLocations() is not { } all) return new LocationsView(0, 0, NoLocationData);
        if (!map.HasKnowledge) return new LocationsView(0, all.Count, MapEditor.Explain(FogStatus.NoMap));
        var (discovered, total) = map.Discovered(all);
        return new LocationsView(discovered, total, null);
    }

    // Inventory (see InventoryEditor for the rules)

    public SessionView SetCount(ItemRef r, int count) => Edit(s =>
    {
        var inventory = Inventory(s);
        var rec = inventory.At(r);
        var old = rec?.Count ?? 0;
        inventory.SetCount(r, count);
        return ChangeLog.Stack(ItemName(rec!.ItemId), old, count);
    });

    public SessionView SetItemLevel(ItemRef r, int level) => Edit(s =>
    {
        var inventory = Inventory(s);
        var rec = inventory.At(r);
        var old = rec?.Level ?? 0;
        inventory.SetItemLevel(r, level);
        return ChangeLog.ItemLevel(ItemName(rec!.ItemId), old, level);
    });

    public SessionView SetRarity(ItemRef r, Rarity rarity) => Edit(s =>
    {
        var inventory = Inventory(s);
        var rec = inventory.At(r);
        var old = (Rarity)(rec?.Rarity?.Rarity ?? 0);
        inventory.SetRarity(r, rarity);
        return ChangeLog.ItemRarity(ItemName(rec!.ItemId), old, rarity);
    });

    public SessionView SetDurability(ItemRef r, uint current, uint max) => Edit(s =>
    {
        var inventory = Inventory(s);
        var rec = inventory.At(r);
        var old = rec?.Durability ?? default;
        inventory.SetDurability(r, current, max);
        return ChangeLog.Durability(ItemName(rec!.ItemId), old, (current, max));
    });

    public SessionView RemoveItem(ItemRef r) => Edit(s =>
    {
        var inventory = Inventory(s);
        var rec = inventory.At(r);
        inventory.Remove(r);
        return ChangeLog.Removed(ItemName(rec!.ItemId), rec.Count);
    });

    /// <summary>Moves an item, swapping with the one in <paramref name="to"/>; Equipped allows swaps only.</summary>
    public SessionView MoveItem(ItemRef from, ItemRef to) => Edit(s =>
    {
        var inventory = Inventory(s);
        var item = inventory.At(from);
        var other = inventory.At(to);
        inventory.Move(from, to);
        return other is null ? ChangeLog.Moved(ItemName(item!.ItemId)) : ChangeLog.Swapped(ItemName(item!.ItemId), ItemName(other.ItemId));
    });

    /// <summary>
    /// Adds a stack to <paramref name="target"/>, or else to the first free backpack slot, and returns where it went so
    /// it can be selected and edited straight away. Customization items are never added.
    /// </summary>
    public (SessionView View, ItemRef Added) AddItem(uint itemId, int count, int? level, Rarity? rarity, ItemRef? target)
    {
        ItemRef added = default;
        var view = Edit(s =>
        {
            var info = Data.Items.Find(itemId) ?? throw new EditRejectedException("Unknown item.");
            if (info.Category == ItemCategory.Customization)
                throw new EditRejectedException("Customization items can't be added.");
            added = Inventory(s).Add(info, count, level, rarity, target);
            return ChangeLog.Added(info.Name, count);
        });
        return (view, added);
    }

    /// <summary>
    /// Every item the Add item browser offers, by name: all but Customization. Built once; an entry's icon is resolved when
    /// it is shown.
    /// </summary>
    public IReadOnlyList<CatalogEntryView> Catalog()
    {
        lock (_lock)
            return _catalog ??= Data.Items.All
                .Where(i => i.Category != ItemCategory.Customization)
                .Select(CatalogEntry)
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Id)
                .ToList()
                .AsReadOnly();
    }

    /// <summary>An item as the Add item browser lists it, with the levels it may take (<see cref="InventoryLayout.LevelRange"/>).</summary>
    private CatalogEntryView CatalogEntry(ItemInfo i)
    {
        var (min, max) = InventoryLayout.LevelRange(i);
        return new CatalogEntryView(i.Id, i.Name, i.Category, SubCategory(i), i.BaseRarity, MaxStack(i), min, max, i.FixedLevel,
            () => icons.ItemIcon(i.Id, i), i.InternalName);
    }

    /// <summary>
    /// The level a stack of <paramref name="itemId"/> added now would get by default (null when it gets no level field),
    /// and whether it would get a rarity field. The field set comes from a record already in this save
    /// (<see cref="NewItemFields.Create"/>), so the Add item panel offers only the fields <see cref="AddItem"/> can set.
    /// Changes nothing; (null, false) for an unknown or Customization item.
    /// </summary>
    public (int? Level, bool Rarity) AddFields(uint itemId)
    {
        lock (_lock)
        {
            var session = Session;
            if (Data.Items.Find(itemId) is not { } info || info.Category == ItemCategory.Customization)
                return (null, false);
            var rec = NewItemFields.Create(info, 0, 1, null, null, session.Player, Data.Items);
            return (rec.Level, rec.Rarity is not null);
        }
    }

    // Skills (always under the game's rules: linked to a learned node or a Spark, and paid for)

    public SessionView Learn(uint id) => Edit(s =>
    {
        var skills = Skills(s);
        skills.Learn(id);
        return ChangeLog.Learned(SkillName(id), skills.RankOf(id));
    });

    /// <summary>The learned skills that <see cref="Unlearn"/> would take with <paramref name="id"/>; changes nothing.</summary>
    public IReadOnlyList<uint> DependentsOf(uint id)
    {
        lock (_lock)
            return Skills(Session).DependentsOf(id);
    }

    /// <summary>Unlearns the skill and every learned skill that loses its path to a Spark without it.</summary>
    public SessionView Unlearn(uint id) => Edit(s =>
    {
        var skills = Skills(s);
        var dependents = skills.DependentsOf(id).Count;
        skills.Unlearn(id);
        return ChangeLog.Unlearned(SkillName(id), dependents);
    });

    public SessionView RefundAll() => Edit(s =>
    {
        Skills(s).RefundAll();
        return ChangeLog.RefundedAll;
    });

    /// <summary>Reverts the last edit: its three records, fog of war, change count and pending line.</summary>
    public SessionView Undo()
    {
        lock (_lock)
        {
            var session = Session;
            if (_undo.Last is not { } last)
                throw new EditRejectedException("Nothing to undo");
            try
            {
                session.Restore(last.Value.Before);
            }
            catch (KsdFormatException e)
            {
                throw new EditRejectedException($"Couldn't undo that edit: {e.Message}");
            }
            _undo.RemoveLast();
            _log.TruncateTo(last.Value.PendingCount);
        }
        Changed?.Invoke();
        return Current;
    }

    /// <summary>
    /// Applies one edit: refuses read-only sessions, snapshots the records for <see cref="Undo"/>, and logs the line
    /// <paramref name="apply"/> returns. The editors check everything before they change anything, so a rejection
    /// leaves the session as it was; any other failure puts the snapshot back before it propagates.
    /// </summary>
    private SessionView Edit(Func<SaveSession, string> apply)
    {
        lock (_lock)
        {
            var session = Session;
            if (session.ReadOnly)
                throw new EditRejectedException($"This save is read-only: {session.ReadOnlyReason}");
            var before = session.Snapshot();
            string line;
            try
            {
                line = apply(session);
            }
            catch (Exception e) when (e is not EditRejectedException)
            {
                session.Restore(before);
                throw;
            }
            _undo.AddLast(new UndoStep(before, _log.Count));
            if (_undo.Count > UndoDepth)
                _undo.RemoveFirst();
            _log.Add(line);
        }
        Changed?.Invoke();
        return Current;
    }

    private InventoryEditor Inventory(SaveSession s) => new(s, Data.Items);

    private SkillsEditor Skills(SaveSession s) => new(s, Data.Tree, Data.Rewards);

    private string ItemName(uint id) => Data.Items.Find(id)?.Name ?? UnknownItem(id);

    private static string UnknownItem(uint id) => $"Unknown item {id:x8}";

    private string SkillName(uint id) => Data.Tree.Find(id)?.Name ?? $"Skill {id:x8}";

    /// <summary>The configured folders plus the one picked in the settings.</summary>
    private List<string> Roots()
    {
        var roots = options.SaveRoots.ToList();
        if (Settings.Load(options.SettingsPath).SaveRoot is { Length: > 0 } chosen && !roots.Any(r => SamePath(r, chosen)))
            roots.Add(chosen);
        return roots;
    }

    private List<CharacterInfo> Scan()
    {
        var locator = new SaveLocator();
        return Roots()
            .SelectMany(locator.Scan)
            .DistinctBy(c => FullPath(c.Path), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(c => c.SavedAt)
            .ThenBy(c => c.FileName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The points left to spend, read without keeping the file open; null when it can't be read.</summary>
    private int? UnspentPoints(string path)
    {
        try
        {
            return Skills(SaveSession.Open(path, IsItem)).Points().Available;
        }
        catch (Exception e) when (e is KsdFormatException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static DateTime LocalTime(DateTimeOffset t) =>
        t == DateTimeOffset.MinValue ? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Local) : t.LocalDateTime;

    private static string FullPath(string path) => Path.GetFullPath(path);

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(FullPath(a), FullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static int MaxStack(ItemInfo? info) => info is null ? ushort.MaxValue : Math.Clamp(info.MaxStack, 1, ushort.MaxValue);

    private static string SubCategory(ItemInfo i) => i.Category switch
    {
        ItemCategory.Weapons => i.WeaponType ?? "",
        ItemCategory.Equipment => i.Slot.ToString(),
        _ => "",
    };

    private SessionView View(SaveSession s)
    {
        var data = Data;
        var inventory = Inventory(s);
        var skills = Skills(s);
        var capacity = inventory.ExtraCapacity;

        SlotView Slot(ItemRef r, string? label) => new(r, label, inventory.At(r) is { } rec ? ItemOf(rec) : null);

        var equipped = InventoryLayout.EquipPositions
            .Select(p => Slot(new ItemRef(ContainerKind.Equipped, p.Index), p.Label))
            .ToList();
        var backpack = InventoryLayout.BackpackGrid(capacity).Select(r => Slot(r, null)).ToList();
        var hotbar = InventoryLayout.HotbarRows
            .SelectMany((row, ri) => row.Select((slot, i) => Slot(new ItemRef(ContainerKind.Inventory, slot), ri == 0 ? $"{i + 1}" : null)))
            .ToList();

        var overflow = new List<SlotView>();
        foreach (var (kind, shown) in new[] { (ContainerKind.Inventory, InventoryLayout.InventorySlots), (ContainerKind.Extra, capacity) })
            if (s.Player.Container(kind) is { } container)
                foreach (var rec in container.Records)
                    if (rec.Slot >= shown && !rec.IsGhost && ItemOf(rec) is { } item)
                        overflow.Add(new SlotView(new ItemRef(kind, rec.Slot), null, item));

        var nodes = data.Tree.Nodes
            .Select(n => new SkillNodeView(n.Id, n.Name, n.Description, n.Type, n.Class, n.Cost, skills.RankOf(n.Id), n.MaxRank,
                skills.StateOf(n.Id), n.X, n.Y, AttributeOf(n), icons.SkillPicture(n), TitleOf(n)))
            .ToList();

        var level = (int)s.Player.Level;
        return new SessionView(
            s.Path, Path.GetFileName(s.Path), s.Character.Name, level, s.Player.Xp, data.LevelCap, s.ReadOnly, s.ReadOnlyReason,
            s.Player.InventoryStatus, equipped, backpack, hotbar, skills.Points(), nodes, data.Tree.Links, data.Tree.Labels,
            _log.Lines, _undo.Count > 0, XpToNextLevel(data, level), overflow, new MapEditor(s).Fog, Locations(s));
    }

    /// <summary>The attributes, by the word the game's text uses, with the tag the skill tree shows.</summary>
    private static readonly (string Word, string Tag)[] Attributes =
    [
        ("Strength", "STR"), ("Dexterity", "DEX"), ("Intelligence", "INT"), ("Constitution", "CON"), ("Spirit", "SPI"),
        ("Endurance", "END"),
    ];

    /// <summary>
    /// The tag of the attribute an attribute node raises, named by the node ("Strength") or else by its text ("Gain one point
    /// of Spirit for every two levels of the Flame."); null for other nodes.
    /// </summary>
    private static string? AttributeOf(SkillNode n) =>
        n.Type != SkillNodeType.Attribute
            ? null
            : Attributes.FirstOrDefault(a => n.Name.Contains(a.Word, StringComparison.OrdinalIgnoreCase)).Tag
              ?? Attributes.FirstOrDefault(a => n.Description.Contains(a.Word, StringComparison.OrdinalIgnoreCase)).Tag;

    /// <summary>A named skill's name; null for a Spark and for an attribute node named after its attribute.</summary>
    private static string? TitleOf(SkillNode n) => n.Type switch
    {
        SkillNodeType.Spark => null,
        SkillNodeType.Attribute when Attributes.Any(a => a.Word.Equals(n.Name, StringComparison.OrdinalIgnoreCase)) => null,
        _ => n.Name,
    };

    /// <summary>R6: <c>XpPerLevel[level − 1]</c>; null without a table, past its end, or at the cap.</summary>
    private static uint? XpToNextLevel(IGameData data, int level) =>
        data.XpPerLevel is { } table && level >= 1 && level < data.LevelCap && level - 1 < table.Count ? table[level - 1] : null;

    /// <summary>The view of a record, or null for one the views hide (Customization).</summary>
    private ItemView? ItemOf(ItemRecord rec)
    {
        var info = Data.Items.Find(rec.ItemId);
        if (info?.Category == ItemCategory.Customization)
            return null;
        Rarity? own = rec.Rarity is { } r && Enum.IsDefined((Rarity)r.Rarity) ? (Rarity)r.Rarity : null;
        (int Min, int Max)? levels = info is null ? null : InventoryLayout.LevelRange(info);
        return new ItemView(
            rec.ItemId, info?.Name ?? UnknownItem(rec.ItemId), info?.Category, own ?? info?.BaseRarity, rec.Count, MaxStack(info),
            rec.Level, levels?.Min, levels?.Max, info?.FixedLevel ?? false, rec.Durability?.Current, rec.Durability?.Max,
            info is not null, icons.ItemIcon(rec.ItemId, info), rec.Rarity is not null);
    }
}
