using EnshroudedSaveEditor.Core.Container;
using EnshroudedSaveEditor.Core.Files;
using EnshroudedSaveEditor.Core.Records;

namespace EnshroudedSaveEditor.Lab;

public static class Cli
{
    const string Usage = """
        Usage:
          copy --remote <dir> --from <characterN.ksd> --name <name>
          dump --file <ksd> [--section <name>]
          diff --a <ksd> --b <ksd>
          watch --remote <dir> --out <dir>
          list-copies
          make-experiments (--remote <dir> | --source <dir> --target <dir>) [--registry <path>] [--backups <dir>]
        """;

    public static int Run(string[] args, TextWriter o, TextWriter err, string? registryPath = null, IProcessProbe? probe = null)
    {
        try
        {
            if (args.Length == 0) { err.WriteLine(Usage); return 2; }
            var opts = Parse(args.Skip(1).ToArray());
            var reg = new LabCopyRegistry(registryPath ?? opts.GetValueOrDefault("registry") ?? LabCopyRegistry.DefaultPath);
            switch (args[0])
            {
                case "copy":
                    // Copy only ever creates a new file (CreateNew); it never writes an existing one.
                    var path = CharacterCopier.Copy(Req(opts, "remote"), Req(opts, "from"), Req(opts, "name"), reg);
                    o.WriteLine($"Created {path}");
                    return 0;
                case "dump": Dump(Req(opts, "file"), opts.GetValueOrDefault("section"), o); return 0;
                case "diff": Diff(Req(opts, "a"), Req(opts, "b"), o); return 0;
                case "watch": Watch(reg, Req(opts, "out"), o); return 0;
                case "make-experiments":
                    var remote = opts.GetValueOrDefault("remote");
                    var source = opts.GetValueOrDefault("source") ?? remote ?? throw new ArgumentException("Missing --source (or --remote).");
                    var target = opts.GetValueOrDefault("target") ?? remote ?? throw new ArgumentException("Missing --target (or --remote).");
                    Experiments.Run(source, target, reg,
                        new BackupService(opts.GetValueOrDefault("backups") ?? BackupService.DefaultRoot),
                        probe ?? new ProcessProbe(), o);
                    return 0;
                case "list-copies":
                    foreach (var e in reg.List())
                        o.WriteLine($"{e.Path}  {e.Guid}  {(reg.IsWritableCopy(e.Path) ? "writable" : "NOT writable")}");
                    return 0;
                default: err.WriteLine(Usage); return 2;
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or KsdFormatException)
        {
            err.WriteLine(e.Message);
            return 1;
        }
    }

    static Dictionary<string, string> Parse(string[] a)
    {
        var d = new Dictionary<string, string>();
        for (var i = 0; i < a.Length; i++)
        {
            if (!a[i].StartsWith("--") || i + 1 >= a.Length) throw new ArgumentException($"Bad argument '{a[i]}'.");
            d[a[i][2..]] = a[++i];
        }
        return d;
    }

    static string Req(Dictionary<string, string> d, string k) =>
        d.TryGetValue(k, out var v) ? v : throw new ArgumentException($"Missing --{k}.");

    static void Watch(LabCopyRegistry reg, string outDir, TextWriter o)
    {
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        var w = new CopyWatcher(reg, outDir);
        o.WriteLine($"Watching registered copies, writing to {outDir}. Ctrl+C to stop.");
        while (!stop.IsCancellationRequested)
        {
            foreach (var f in w.Poll(DateTime.Now)) o.WriteLine($"Saved {f}");
            stop.Token.WaitHandle.WaitOne(2000);
        }
    }

    static void Dump(string file, string? section, TextWriter o)
    {
        var ksd = KsdFile.Parse(File.ReadAllBytes(file));
        o.WriteLine($"File: {file}");
        o.WriteLine($"Version {ksd.Version}  GUID {Convert.ToHexString(ksd.Guid)}  Revision {ksd.Revision}  Saved {ksd.SavedAt:u}");
        foreach (var s in ksd.Sections)
        {
            if (section is not null && s.Name != section) continue;
            int dataLen;
            try { dataLen = s.GetData().Length; } catch (KsdFormatException) { dataLen = -1; }
            o.WriteLine($"Section {s.Name}: type {s.TypeId}, compressed {s.Compressed}, data version {s.DataVersion}, checksum ok {s.ChecksumValid}, block {s.Block.Length} bytes, data {dataLen} bytes");
        }

        if (ksd.Find("character_data") is { } cds && (section is null or "character_data"))
        {
            var cd = CharacterData.Parse(cds.GetData());
            o.WriteLine($"Character: \"{cd.Name}\" slot {cd.SlotIndex} guid {Convert.ToHexString(cd.Guid)}");
        }
        if (ksd.Find("player_character") is { } pcs && (section is null or "player_character"))
        {
            var pc = PlayerCharacter.Parse(pcs.GetData(), _ => false);
            o.WriteLine($"Level {pc.Level}  XP {pc.Xp}");
            o.WriteLine($"Skill entries: {pc.Skills.Count} ({pc.Skills.Count(s => s.Rank == 0)} at rank 0)");
            o.WriteLine($"Inventory status: {pc.InventoryStatus}");
            foreach (var c in pc.Containers)
            {
                o.WriteLine($"Container {c.Kind}: {c.Records.Count} records");
                foreach (var r in c.Records)
                    o.WriteLine($"  slot {r.Slot,3}  id {r.ItemId:X8}  count {r.Count}  flags {r.Flags:X2}{(r.IsGhost ? "  ghost" : "")}");
            }
        }
    }

    static void Diff(string a, string b, TextWriter o)
    {
        var fa = KsdFile.Parse(File.ReadAllBytes(a));
        var fb = KsdFile.Parse(File.ReadAllBytes(b));
        o.WriteLine($"A: {a}");
        o.WriteLine($"B: {b}");
        if (fa.Revision != fb.Revision) o.WriteLine($"Revision: {fa.Revision} -> {fb.Revision}");
        var names = fa.Sections.Select(s => s.Name).Union(fb.Sections.Select(s => s.Name)).ToList();
        foreach (var n in names)
        {
            var sa = fa.Find(n); var sb = fb.Find(n);
            if (sa is null || sb is null) o.WriteLine($"Section {n}: only in {(sa is null ? "B" : "A")}");
            else if (!sa.Block.Span.SequenceEqual(sb.Block.Span))
                o.WriteLine($"Section {n}: changed ({sa.Block.Length} -> {sb.Block.Length} bytes)");
        }
        if (fa.Find("player_character") is not { } pa || fb.Find("player_character") is not { } pb) return;
        var ca = PlayerCharacter.Parse(pa.GetData(), _ => false);
        var cb = PlayerCharacter.Parse(pb.GetData(), _ => false);
        if (ca.Level != cb.Level) o.WriteLine($"Level: {ca.Level} -> {cb.Level}");
        if (ca.Xp != cb.Xp) o.WriteLine($"XP: {ca.Xp} -> {cb.Xp}");
        o.WriteLine($"Skill entries: {ca.Skills.Count} -> {cb.Skills.Count}");
        var ska = ca.Skills.GroupBy(s => s.NodeId).ToDictionary(g => g.Key, g => g.Last().Rank);
        var skb = cb.Skills.GroupBy(s => s.NodeId).ToDictionary(g => g.Key, g => g.Last().Rank);
        foreach (var id in ska.Keys.Union(skb.Keys).OrderBy(x => x))
        {
            var ra = ska.TryGetValue(id, out var x) ? x.ToString() : "-";
            var rb = skb.TryGetValue(id, out var y) ? y.ToString() : "-";
            if (ra != rb) o.WriteLine($"  skill {id:X8}: rank {ra} -> {rb}");
        }
        foreach (var kind in Enum.GetValues<ContainerKind>())
        {
            var ra = ca.Container(kind)?.Records.GroupBy(r => r.Slot).ToDictionary(g => g.Key, g => g.First()) ?? [];
            var rb = cb.Container(kind)?.Records.GroupBy(r => r.Slot).ToDictionary(g => g.Key, g => g.First()) ?? [];
            foreach (var slot in ra.Keys.Union(rb.Keys).OrderBy(x => x))
            {
                ra.TryGetValue(slot, out var p); rb.TryGetValue(slot, out var q);
                var da = p is null ? "-" : $"{p.ItemId:X8} x{p.Count} f{p.Flags:X2}";
                var db = q is null ? "-" : $"{q.ItemId:X8} x{q.Count} f{q.Flags:X2}";
                if (da != db) o.WriteLine($"  {kind} slot {slot}: {da} -> {db}");
            }
        }
    }
}
