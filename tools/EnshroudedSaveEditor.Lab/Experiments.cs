using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Editing;
using EnshroudedSaveEditor.Core.Files;
using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.Core.Records;
using EnshroudedSaveEditor.GameData;

namespace EnshroudedSaveEditor.Lab;

/// <summary>
/// Builds the six in-game experiment copies of session A (Task 11). It only ever creates new character files
/// (via <see cref="CharacterCopier"/>) and modifies those copies, each through <see cref="LabCopyRegistry.EnsureWritable"/>.
/// </summary>
public static partial class Experiments
{
    public const string PlainName = "Test Plain";
    public const string RepackName = "Test Repack";
    public const string SlotsName = "Test Slots";
    public const string ItemsName = "Test Items";
    public const string SkillsName = "Test Skills";
    public const string OverspendName = "Test Overspend With A Forty Char Name 40";

    public static readonly string[] AllNames = [PlainName, RepackName, SlotsName, ItemsName, SkillsName, OverspendName];

    const uint CopperArrow = 0xf12faa48, HealthPotion = 0xccd46f7a, FireballI = 0xe988b200, CopperOre = 0x2b9d5b6f,
        WolfsSnarlLongbow = 0x29433573, AdventurerHelmet = 0xff1b25e5, RingOfHealth = 0xf29b7aa9;

    /// <summary>The Extra slots "Test Slots" fills (session A: 0–7, the then-provisional capacity).</summary>
    const int SlotsExtra = 8;

    [GeneratedRegex(@"^character\d+\.ksd$", RegexOptions.IgnoreCase)]
    private static partial Regex CharacterFile();

    sealed record Made(string Name, string Path, string Purpose, List<string> Edits);

    public static void Run(string remote, LabCopyRegistry reg, BackupService backups, IProcessProbe probe, TextWriter o) =>
        Run(remote, remote, reg, backups, probe, o);

    /// <summary>Reads the sources from <paramref name="source"/> and creates the copies in <paramref name="target"/>.</summary>
    public static void Run(string source, string target, LabCopyRegistry reg, BackupService backups, IProcessProbe probe, TextWriter o)
    {
        if (probe.IsGameRunning())
            throw new InvalidOperationException("Enshrouded is running. Close the game before making experiment copies.");

        var existing = ExistingCopyNames(reg);
        var clash = AllNames.Where(existing.Contains).ToList();
        if (clash.Count > 0)
            throw new InvalidOperationException("Experiment copies already exist: " + string.Join(", ", clash.Select(n => $"\"{n}\"")));

        var separate = !SamePath(source, target);
        byte[]? tag = null;
        if (separate)
        {
            tag = RequireGameMadeTag(target, reg);
            o.WriteLine($"Storage tag for the copies (from the game-made character in the target): {Convert.ToHexString(tag)}");
        }

        var data = ReferenceGameData.Load();
        var before = HashOriginals(source, target, separate);
        var c = new Ctx(source, target, tag);
        var made = new List<Made>
        {
            Plain(c, reg),
            Repack(c, reg, backups, probe, data),
            Slots(c, reg, backups, probe, data),
            Items(c, reg, backups, probe, data),
            Skills(c, reg, backups, probe, data),
            Overspend(c, reg, backups, probe, data),
        };

        var after = HashOriginals(source, target, separate);
        o.WriteLine("Original character files (SHA-256, first 16 hex), before -> after:");
        var changed = new List<string>();
        foreach (var (file, h) in before)
        {
            var a = after.GetValueOrDefault(file, "(missing)");
            o.WriteLine($"  {file}  {h}  ->  {a}  {(a == h ? "unchanged" : "CHANGED")}");
            if (a != h) changed.Add(file);
        }
        if (changed.Count > 0)
            throw new InvalidOperationException("ORIGINAL SAVE CHANGED: " + string.Join(", ", changed));

        o.WriteLine();
        o.WriteLine("Experiment copies:");
        foreach (var m in made)
            o.WriteLine($"  {System.IO.Path.GetFileName(m.Path),-16} {m.Name,-42} {m.Purpose}");
        foreach (var m in made)
        {
            o.WriteLine();
            o.WriteLine($"{System.IO.Path.GetFileName(m.Path)}  \"{m.Name}\"  edits applied:");
            foreach (var e in m.Edits) o.WriteLine($"  - {e}");
        }
    }

    static HashSet<string> ExistingCopyNames(LabCopyRegistry reg)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in reg.List())
        {
            if (!File.Exists(e.Path)) continue;
            try
            {
                var f = KsdFile.Parse(File.ReadAllBytes(e.Path));
                if (f.Find("character_data") is { } s) names.Add(CharacterData.Parse(s.GetData()).Name);
            }
            catch (Exception ex) when (ex is KsdFormatException or IOException) { }
        }
        return names;
    }

    sealed record Ctx(string Source, string Target, byte[]? Tag);

    public static bool SamePath(string a, string b) =>
        string.Equals(System.IO.Path.GetFullPath(a).TrimEnd('\\', '/'), System.IO.Path.GetFullPath(b).TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>The game-made character's storage tag in <paramref name="dir"/>, or a refusal if there is none.</summary>
    public static byte[] RequireGameMadeTag(string dir, LabCopyRegistry reg) =>
        GameMadeTag(dir, reg)
            ?? throw new InvalidOperationException("Create one local character in the game first, so the editor can copy its storage tag.");

    /// <summary>Storage tag of the lowest-numbered characterN.ksd in the folder that is not a registered Lab copy.</summary>
    static byte[]? GameMadeTag(string dir, LabCopyRegistry reg)
    {
        var copies = reg.List().Select(e => e.Path).ToList();
        var files = Directory.EnumerateFiles(dir, "*.ksd")
            .Select(f => (Path: f, M: CharacterNumber().Match(System.IO.Path.GetFileName(f))))
            .Where(x => x.M.Success && !copies.Any(p => SamePath(p, x.Path)))
            .OrderBy(x => int.Parse(x.M.Groups[1].Value));
        foreach (var (path, _) in files)
        {
            try
            {
                var f = KsdFile.Parse(File.ReadAllBytes(path));
                if (f.Find("character_data") is { } s) return CharacterData.Parse(s.GetData()).StorageTag;
            }
            catch (Exception ex) when (ex is KsdFormatException or IOException) { }
        }
        return null;
    }

    [GeneratedRegex(@"^character(\d+)\.ksd$", RegexOptions.IgnoreCase)]
    private static partial Regex CharacterNumber();

    static SortedDictionary<string, string> HashOriginals(string source, string target, bool both)
    {
        var d = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var dir in both ? new[] { source, target } : new[] { source })
            foreach (var f in Directory.EnumerateFiles(dir, "*.ksd"))
            {
                var name = System.IO.Path.GetFileName(f);
                if (CharacterFile().IsMatch(name)) d[f] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))[..16];
            }
        return d;
    }

    // ---- builders -------------------------------------------------------------------------------

    static Made Plain(Ctx c, LabCopyRegistry reg)
    {
        var path = CharacterCopier.CopyTo(System.IO.Path.Combine(c.Source, "character2.ksd"), c.Target, PlainName, reg, c.Tag);
        return new Made(PlainName, path, "identity only: listed? loads? same gear/skills/level 7? rank-0 skills shown learned?",
            ["identity only (new GUID, slot, name); no other change"]);
    }

    static Made Repack(Ctx c, LabCopyRegistry reg, BackupService backups, IProcessProbe probe, ReferenceGameData data)
    {
        var path = CharacterCopier.CopyTo(System.IO.Path.Combine(c.Source, "character2.ksd"), c.Target, RepackName, reg, c.Tag);
        var s = Open(path, reg, data);
        foreach (var sec in s.File.Sections) sec.SetData(sec.GetData());
        s.Player.Level = 9; s.Player.Xp = 0;
        s.MarkChanged();
        Save(s, reg, backups, probe);
        return new Made(RepackName, path, "writer check: loads? level 9? XP 0? skill points (expect 4)? Steam Cloud checks",
            [$"every section recompressed by our writer ({s.File.Sections.Count} sections)", "level 9, XP 0"]);
    }

    static Made Slots(Ctx c, LabCopyRegistry reg, BackupService backups, IProcessProbe probe, ReferenceGameData data)
    {
        var path = CharacterCopier.CopyTo(System.IO.Path.Combine(c.Source, "character2.ksd"), c.Target, SlotsName, reg, c.Tag);
        var s = Open(path, reg, data);
        var inv = s.Player.Container(ContainerKind.Inventory)!.Records;
        var extra = s.Player.Container(ContainerKind.Extra)!.Records;
        inv.Clear(); extra.Clear();
        for (var i = 0; i < InventoryLayout.InventorySlots; i++) inv.Add(new ItemRecord((byte)i, CopperArrow, (ushort)(i + 1)));
        for (var i = 0; i < SlotsExtra; i++) extra.Add(new ItemRecord((byte)i, CopperArrow, (ushort)(100 + i)));
        s.MarkChanged();
        Save(s, reg, backups, probe);
        return new Made(SlotsName, path, "slot map: screenshot inventory and hotbar; which counts appear where, which are missing",
        [
            "Inventory and Extra cleared (Equipped untouched)",
            $"Copper Arrow in Inventory slots 0-{InventoryLayout.InventorySlots - 1}, count = slot+1 (1..{InventoryLayout.InventorySlots})",
            $"Copper Arrow in Extra slots 0-{SlotsExtra - 1}, count = 100+slot (100..{99 + SlotsExtra})",
        ]);
    }

    static Made Items(Ctx c, LabCopyRegistry reg, BackupService backups, IProcessProbe probe, ReferenceGameData data)
    {
        var path = CharacterCopier.CopyTo(System.IO.Path.Combine(c.Source, "character2.ksd"), c.Target, ItemsName, reg, c.Tag);
        var s = Open(path, reg, data);
        var ed = new InventoryEditor(s, data.Items);
        var edits = new List<string>();

        var potion = FindStack(s, HealthPotion);
        if (potion is not null)
        {
            var was = potion.Count;
            if (was != 20) ed.SetCount(new ItemRef(KindOf(s, potion), potion.Slot), 20);
            edits.Add($"Health Potion count {was} -> 20");
        }
        else edits.Add("Health Potion: none in the source; skipped");

        var fire = FindStack(s, FireballI);
        var fireMax = data.Items.Find(FireballI)?.MaxStack ?? 500;
        if (fire is not null)
        {
            var was = fire.Count;
            fire.Count = (ushort)(fireMax + 100);   // bypasses the editor's cap on purpose
            s.MarkChanged();
            edits.Add($"Fireball I count {was} -> {fire.Count} (max stack {fireMax} + 100, written directly)");
        }
        else edits.Add("Fireball I: none in the source; skipped");

        edits.Add(Describe(s, ed.Add(Info(data, CopperOre), 250, null, null, null), "Copper Ore x250"));
        edits.Add(Describe(s, ed.Add(Info(data, WolfsSnarlLongbow), 1, 20, Rarity.Epic, null), "Wolf's Snarl Longbow level 20 Epic"));
        edits.Add(Describe(s, ed.Add(Info(data, AdventurerHelmet), 1, 10, Rarity.Rare, null), "Adventurer Helmet level 10 Rare"));
        edits.Add(Describe(s, ed.Add(Info(data, RingOfHealth), 1, null, null, null), "Ring of Health (default level and rarity)"));

        // Move one existing backpack stack (the potions if they are there) to the highest empty backpack slot.
        var mover = potion is not null && KindOf(s, potion) == ContainerKind.Inventory
            ? potion
            : s.Player.Container(ContainerKind.Inventory)!.Records.FirstOrDefault(r => !r.IsGhost && r.Count > 1);
        ItemRef? to = null;
        for (var i = InventoryLayout.InventorySlots - 1; i >= 0 && to is null; i--)
            if (ed.At(new ItemRef(ContainerKind.Inventory, (byte)i)) is null) to = new ItemRef(ContainerKind.Inventory, (byte)i);
        if (mover is not null && to is { } t)
        {
            var from = new ItemRef(ContainerKind.Inventory, mover.Slot);
            ed.Move(from, t);
            edits.Add($"moved the {data.Items.Find(mover.ItemId)?.Name ?? mover.ItemId.ToString("x8")} stack from Inventory slot {from.Slot} to empty slot {t.Slot}");
        }
        else edits.Add("move skipped (no movable stack or no empty backpack slot)");

        Save(s, reg, backups, probe);
        return new Made(ItemsName, path, "item fields: each item visible/usable with its level and rarity; bow damage; over-max stack",
            edits);
    }

    static Made Skills(Ctx c, LabCopyRegistry reg, BackupService backups, IProcessProbe probe, ReferenceGameData data)
    {
        var path = CharacterCopier.CopyTo(System.IO.Path.Combine(c.Source, "character1.ksd"), c.Target, SkillsName, reg, c.Tag);
        var s = Open(path, reg, data);
        var ed = new SkillsEditor(s, data.Tree, data.Rewards);
        var edits = new List<string> { $"points available before: {ed.Points().Available}" };
        var learnedNew = new List<uint>();
        var already = ed.Learned.ToHashSet();
        var havePassive = false;
        var haveThree = false;

        // Greedy, in node id order, except that a passive and then a 3-rank node (taken to rank 2) are preferred
        // as soon as they are available and affordable.
        while (learnedNew.Count < 10)
        {
            var avail = ed.Points().Available;
            var candidates = data.Tree.Nodes
                .Where(n => n.Type != SkillNodeType.Spark && !already.Contains(n.Id) && !learnedNew.Contains(n.Id)
                    && ed.StateOf(n.Id) == NodeState.Available && n.Cost <= avail)
                .OrderBy(n => n.Id).ToList();
            var pick = (!havePassive ? candidates.FirstOrDefault(n => n.Type == SkillNodeType.Passive) : null)
                ?? (!haveThree ? candidates.FirstOrDefault(n => n.MaxRank >= 3 && n.Cost * 2 <= avail) : null)
                ?? candidates.FirstOrDefault();
            if (pick is null) break;
            ed.Learn(pick.Id);
            learnedNew.Add(pick.Id);
            var rank = 1;
            if (!haveThree && pick.MaxRank >= 3 && ed.Points().Available >= pick.Cost)
            {
                ed.Learn(pick.Id);
                haveThree = true; rank = 2;
            }
            if (pick.Type == SkillNodeType.Passive) havePassive = true;
            edits.Add($"learned {pick.Id:X8} \"{pick.Name}\" ({pick.Type}, cost {pick.Cost}, max rank {pick.MaxRank}) at rank {rank}");
        }

        if (learnedNew.Count < 10) edits.Add($"WARNING: only {learnedNew.Count} nodes learned (points ran out)");
        if (!havePassive) edits.Add("WARNING: no passive learned");
        if (!haveThree) edits.Add("WARNING: no 3-rank node learned at rank 2");
        edits.Add($"points available after: {ed.Points().Available}");
        Save(s, reg, backups, probe);
        return new Made(SkillsName, path, "skills: shown learned? points left = before - spent; passive bonus on stats; learn one more in-game and quit",
            edits);
    }

    static Made Overspend(Ctx c, LabCopyRegistry reg, BackupService backups, IProcessProbe probe, ReferenceGameData data)
    {
        var path = CharacterCopier.CopyTo(System.IO.Path.Combine(c.Source, "character2.ksd"), c.Target, OverspendName, reg, c.Tag);
        var s = Open(path, reg, data);
        var n = 0;
        for (var i = 0; i < s.Player.Skills.Count; i++)
            if (s.Player.Skills[i].Rank == 0) { s.Player.Skills[i] = s.Player.Skills[i] with { Rank = 1 }; n++; }
        s.Character = s.Character.With(name: OverspendName);   // 40 characters, bypassing the editor's name cap (32 then, 15 now)
        s.MarkChanged();
        Save(s, reg, backups, probe);
        return new Made(OverspendName, path, "overspend/long name: tree kept, clamped or reset? full name shown? new-character name box limit",
        [
            $"{n} rank-0 skill entries set to rank 1 (spent > earned)",
            $"name written directly: \"{OverspendName}\" ({OverspendName.Length} characters)",
        ]);
    }

    // ---- helpers --------------------------------------------------------------------------------

    static SaveSession Open(string path, LabCopyRegistry reg, ReferenceGameData data)
    {
        reg.EnsureWritable(path);
        var s = SaveSession.Open(path, id => data.Items.Find(id) is not null);
        if (s.ReadOnly) throw new InvalidOperationException($"{path}: {s.ReadOnlyReason}");
        if (s.Player.InventoryStatus != InventoryStatus.Full)
            throw new InvalidOperationException($"{path}: inventory is {s.Player.InventoryStatus}, not Full.");
        return s;
    }

    static void Save(SaveSession s, LabCopyRegistry reg, BackupService backups, IProcessProbe probe)
    {
        reg.EnsureWritable(s.Path);
        var r = s.Save(backups, probe);
        if (r != SaveResult.Saved) throw new InvalidOperationException($"Saving {s.Path} failed: {r}");
    }

    static ItemInfo Info(ReferenceGameData data, uint id) =>
        data.Items.Find(id) ?? throw new InvalidOperationException($"Item {id:x8} is not in the game data.");

    static ItemRecord? FindStack(SaveSession s, uint id) =>
        s.Player.Container(ContainerKind.Inventory)!.Records.Concat(s.Player.Container(ContainerKind.Extra)!.Records)
            .FirstOrDefault(r => r.ItemId == id && !r.IsGhost);

    static ContainerKind KindOf(SaveSession s, ItemRecord r) =>
        s.Player.Container(ContainerKind.Inventory)!.Records.Contains(r) ? ContainerKind.Inventory : ContainerKind.Extra;

    static string Describe(SaveSession s, ItemRef at, string what)
    {
        var r = s.Player.Container(at.Kind)!.Records.First(x => x.Slot == at.Slot);
        return $"added {what} in {at.Kind} slot {at.Slot} (count {r.Count}, level {r.Level?.ToString() ?? "-"}, rarity {r.Rarity?.Rarity.ToString() ?? "-"})";
    }
}
