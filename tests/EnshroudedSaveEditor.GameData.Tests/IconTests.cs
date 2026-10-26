using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text;
using EnshroudedSaveEditor.Core.Model;
using EnshroudedSaveEditor.GameData;
using EnshroudedSaveEditor.GameData.Install;
using EnshroudedSaveEditor.GameData.Kfc;

namespace EnshroudedSaveEditor.Tests;

/// <summary>The install's icon textures, opened once for the tests that read them; they skip when Enshrouded isn't installed.</summary>
public sealed class InstallIconsFixture : IDisposable
{
    readonly GameInstall? _install = GameInstall.Find(null);
    KfcIconTextures? _textures;

    public KfcIconTextures Textures
    {
        get
        {
            Assert.SkipWhen(_install is null, "Enshrouded is not installed");
            return _textures ??= new KfcIconTextures(_install!);
        }
    }

    public void Dispose() => _textures?.Dispose();
}

public sealed class IconTests(InstallIconsFixture install) : IClassFixture<InstallIconsFixture>, IDisposable
{
    const uint CopperArrow = 0xf12faa48; // Ammunition
    const uint BareFists = 0xda9e5c1b;   // its install icon is fully transparent, like those of Customization items

    static readonly ReferenceGameData Reference = ReferenceGameData.Load();

    readonly string _cache = Path.Combine(Path.GetTempPath(), "ese-icons-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_cache, true); } catch (IOException) { }
    }

    /// <summary>A texture source that hands out a 2×2 image and counts how often it is asked.</summary>
    sealed class FakeTextures(string version = "build 1") : IIconTextureSource
    {
        public int Calls;
        public HashSet<uint> Missing { get; } = [];
        public HashSet<uint> Throwing { get; } = [];
        public string Version => version;
        public IconTexture? ItemTexture(uint id) => Texture(id);
        public IconTexture? SkillTexture(uint nodeId) => Texture(nodeId);

        IconTexture? Texture(uint id)
        {
            Interlocked.Increment(ref Calls);
            if (Throwing.Contains(id)) throw new InvalidCastException("a texture source bug");
            return Missing.Contains(id) ? null : new IconTexture(2, 2, [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 0, 0, 0, 0]);
        }
    }

    string FileFor(string url)
    {
        Assert.StartsWith("https://icons.ese/", url);
        return Path.Combine(_cache, url["https://icons.ese/".Length..].Replace('/', Path.DirectorySeparatorChar));
    }

    // ---- Brief tests

    [Fact]
    public void Copper_arrow_icon_is_a_png_of_expected_size()
    {
        var icons = new IconService(_cache, install.Textures);

        var url = icons.ItemIcon(CopperArrow, Reference.Items.Find(CopperArrow));

        Assert.Equal("https://icons.ese/item/f12faa48.png", url);
        var png = ReadPng(File.ReadAllBytes(FileFor(url)));
        Assert.Equal(png.Width, png.Height);
        Assert.InRange(png.Width, 128, 256);
        // The arrow on a transparent background, not noise: clear corners, an opaque arrow across part of the square.
        foreach (var (x, y) in new[] { (0, 0), (png.Width - 1, 0), (0, png.Height - 1), (png.Width - 1, png.Height - 1) })
            Assert.Equal(0, png.Rgba[(y * png.Width + x) * 4 + 3]);
        var opaque = Enumerable.Range(0, png.Width * png.Height).Count(i => png.Rgba[i * 4 + 3] == 255);
        Assert.InRange(opaque, png.Width * png.Height / 20, png.Width * png.Height / 2);
    }

    [Fact]
    public void Unknown_id_falls_back_to_glyph()
    {
        var icons = new IconService(_cache, install.Textures);

        Assert.Equal("https://icons.ese/glyph/unknown.svg", icons.ItemIcon(0xdeadbeef, null));
        Assert.Equal("https://icons.ese/glyph/unknown.svg", icons.ItemIcon(0xdeadbeef, null));
        Assert.False(Directory.Exists(Path.Combine(_cache, "item")));
    }

    [Fact]
    public void A_blank_install_icon_falls_back_to_glyph()
    {
        var icons = new IconService(_cache, install.Textures);

        Assert.StartsWith("https://icons.ese/glyph/", icons.ItemIcon(BareFists, Reference.Items.Find(BareFists)));
        Assert.False(File.Exists(Path.Combine(_cache, "item", $"{BareFists:x8}.png")));
    }

    [Fact]
    public void Icons_are_cached_once()
    {
        var textures = new FakeTextures();
        var icons = new IconService(_cache, textures);

        var url = icons.ItemIcon(7, null);
        var file = FileFor(url);
        var written = File.GetLastWriteTimeUtc(file);
        Assert.Equal("https://icons.ese/item/00000007.png", url);
        Assert.Equal(url, icons.ItemIcon(7, null));
        Assert.Equal(1, textures.Calls);

        // The next session reuses the file without decoding the texture again.
        Assert.Equal(url, new IconService(_cache, textures).ItemIcon(7, null));
        Assert.Equal(1, textures.Calls);
        Assert.Equal(written, File.GetLastWriteTimeUtc(file));
    }

    // ---- More behaviour

    [Fact]
    public void Ability_nodes_get_their_install_icon_and_other_nodes_their_glyph()
    {
        var icons = new IconService(_cache, install.Textures);

        foreach (var node in Reference.Tree.Nodes)
        {
            var url = icons.SkillIcon(node);
            if (node.Type != SkillNodeType.Ability)
            {
                Assert.StartsWith("https://icons.ese/glyph/", url);
                continue;
            }
            Assert.Equal($"https://icons.ese/skill/{node.Id:x8}.png", url);
            var png = ReadPng(File.ReadAllBytes(FileFor(url)));
            Assert.InRange(png.Width, 32, 128);
            Assert.InRange(png.Height, 32, 128);
        }
    }

    [Fact]
    public void Reference_items_get_install_icons()
    {
        var icons = new IconService(_cache, install.Textures);
        // Customization items are left out: their install icons are blank, and the editor hides them.
        var sample = Reference.Items.All.Where(i => i.Category != ItemCategory.Customization).Where((_, i) => i % 35 == 0).ToList();

        var pngs = sample.Count(i => icons.ItemIcon(i.Id, i) == $"https://icons.ese/item/{i.Id:x8}.png");

        // The reference is an older build: a few of its items are gone from the install (docs/research/kfc-format.md).
        Assert.True(pngs >= sample.Count * 0.95, $"{pngs} of {sample.Count} sampled reference items have an install icon");
    }

    [Fact]
    public void A_texture_that_cant_be_read_gets_its_glyph_and_is_not_retried()
    {
        var textures = new FakeTextures();
        textures.Missing.Add(9);
        var icons = new IconService(_cache, textures);
        var ore = Reference.Items.All.First(i => i.Category == ItemCategory.Materials);

        Assert.Equal("https://icons.ese/glyph/materials.svg", icons.ItemIcon(9, ore));
        Assert.Equal("https://icons.ese/glyph/materials.svg", icons.ItemIcon(9, ore));
        Assert.Equal(1, textures.Calls);
    }

    [Fact]
    public void A_texture_source_that_throws_gives_the_glyph_every_time()
    {
        var textures = new FakeTextures();
        textures.Throwing.Add(9);
        var icons = new IconService(_cache, textures);
        var ore = Reference.Items.All.First(i => i.Category == ItemCategory.Materials);

        Assert.Equal("https://icons.ese/glyph/materials.svg", icons.ItemIcon(9, ore));
        Assert.Equal("https://icons.ese/glyph/materials.svg", icons.ItemIcon(9, ore));
        Assert.Equal(1, textures.Calls);
        Assert.Equal("https://icons.ese/item/00000007.png", icons.ItemIcon(7, null));
    }

    [Fact]
    public void Icons_cached_for_another_game_build_are_extracted_again()
    {
        var url = new IconService(_cache, new FakeTextures("build 1")).ItemIcon(7, null);
        File.WriteAllText(FileFor(url), "stale");
        var newer = new FakeTextures("build 2");

        Assert.Equal(url, new IconService(_cache, newer).ItemIcon(7, null));

        Assert.Equal(1, newer.Calls);
        Assert.Equal(2, ReadPng(File.ReadAllBytes(FileFor(url))).Width);
    }

    [Fact]
    public void An_unwritable_cache_gives_the_glyph_never_an_exception()
    {
        File.WriteAllText(_cache, "a file where the cache folder should be");
        try
        {
            var icons = new IconService(_cache, new FakeTextures());
            Assert.Equal("https://icons.ese/glyph/unknown.svg", icons.ItemIcon(7, null));
        }
        finally
        {
            File.Delete(_cache);
        }
    }

    // ---- Texture decoding

    /// <summary>One random block per BC7 mode and the texels an independent decoder (Pillow 13) gives for it.</summary>
    static readonly (string Block, string Texels)[] Bc7Vectors =
    [
        ("A94843541C5E28C8CE7719C9F4E3CB86", "537D2DFF5A2918FF58451FFF58451FFF537D2DFF566126FF4AEF4AFF4AEF4AFFA50063FF2CDB78FFA542B5FF462AD9FF2CDB78FF2CDB78FF2121E7FF462AD9FF"),
        ("965CBF1C9BBB1887C0A9B1633115DEBC", "726E1EFF252488FFF7BB0AFF2C2C70FFAA8E16FF2A2978FFE4B00DFF232091FF282680FF978418FF2C2C70FFF7BB0AFF211E99FF85791BFF1C18A9FFD2A510FF"),
        ("AC9BFA4BB81DF91F788F074445EFCC94", "6BDEDEFF6BDEDEFF63C39DFF73C6ADFF5AFF00FF90F728FF5AFF00FF101810FFC9EF53FFC9EF53FF90F728FF305144FF5AA759FF5AA759FF6BDEDEFF305144FF"),
        ("58A74BCADBE0C88C1D4DA9E69A5F7C16", "D3078FFF4B8F4DFF949852FF6F639BFF4B8F4DFF6F639BFF7B7483FFD3078FFF7B7483FF6F639BFF4B8F4DFFD3078FFF6F639BFF786263FFD3078FFF949852FF"),
        ("1015AD7A5DBEAC8A8FA15A8B4D33562C", "8A75A5CD8A75A5988A75A5618A75A5618A75A5E78A75A598AD5ABD9842AD73B242AD73988A75A546AD5ABDE742AD7398AD5ABD61AD5ABDE78A75A5988A75A5CD"),
        ("E098A13FA7E5C396F8B4640C26794C8A", "30FD82B5877282F9877253F94DCFB0CB6AA082E36AA053E34DCF25CB4DCF82CB6AA0B0E330FD25B58772B0F930FD82B56AA053E34DCF53CB30FDB0B530FD53B5"),
        ("40224A9049D0A79BFD18A0BA6E1ADF0F", "72186A7A5133E9376B1D856C860820A0890513A76324A35C6324A35C6027B0555530DC3E72186A7A6324A35C860820A05133E937592DCB475133E937890513A7"),
        ("80A1CEB94D959E89758B7BA8BE46B355", "D2834F49CF558EBED2834F49D2834F496D7DDFA6CB49AA388C6CCE828C6CCE82D2834F49D06C6F85D2834F49CF558EBE8C6CCE828C6CCE828C6CCE82CB49AA38"),
    ];

    public static TheoryData<string, string> Bc7Blocks
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var (block, texels) in Bc7Vectors) data.Add(block, texels);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Bc7Blocks))]
    public void Bc7_blocks_decode_like_an_independent_decoder(string block, string texels)
    {
        var rgba = new byte[4 * 4 * 4];
        Bc7.DecodeBlock(Convert.FromHexString(block), rgba, 0, 16);
        Assert.Equal(texels, Convert.ToHexString(rgba));
    }

    [Fact]
    public void A_bc7_block_without_a_mode_is_transparent_black()
    {
        var rgba = Enumerable.Repeat((byte)7, 64).ToArray();
        Bc7.DecodeBlock(new byte[16], rgba, 0, 16);
        Assert.All(rgba, b => Assert.Equal(0, b));
    }

    [Fact]
    public void A_texture_larger_than_an_icon_gives_its_first_mip_level_that_fits()
    {
        // 512×256, 256×128 and 128×64 RGBA levels, filled with 1, 2 and 3.
        var content = new byte[(512 * 256 + 256 * 128 + 128 * 64) * 4];
        content.AsSpan(0, 512 * 256 * 4).Fill(1);
        content.AsSpan(512 * 256 * 4, 256 * 128 * 4).Fill(2);
        content.AsSpan((512 * 256 + 256 * 128) * 4).Fill(3);

        var image = KfcTexture.Decode("R8G8B8A8_unorm", 512, 256, 3, content, 128);

        Assert.Equal((128, 64), (image.Width, image.Height));
        Assert.All(image.Rgba, b => Assert.Equal(3, b));

        // With a single level, that level.
        var single = KfcTexture.Decode("R8G8B8A8_unorm", 512, 256, 1, content, 128);
        Assert.Equal((512, 256), (single.Width, single.Height));
        Assert.All(single.Rgba, b => Assert.Equal(1, b));
    }

    [Fact]
    public void A_bc7_texture_of_odd_size_is_cropped_from_its_blocks()
    {
        var blocks = Bc7Vectors.Take(4).Select(v => v.Block).ToList();
        var texels = Bc7Vectors.Take(4).Select(v => Convert.FromHexString(v.Texels)).ToList();

        var image = KfcTexture.Decode("BC7_unorm_block", 6, 5, 1, Convert.FromHexString(string.Concat(blocks)), 128);

        Assert.Equal((6, 5), (image.Width, image.Height));
        for (var y = 0; y < 5; y++)
            for (var x = 0; x < 6; x++)
            {
                var block = texels[y / 4 * 2 + x / 4];
                var want = block.AsSpan((y % 4 * 4 + x % 4) * 4, 4).ToArray();
                Assert.Equal(want, image.Rgba.AsSpan((y * 6 + x) * 4, 4).ToArray());
            }
    }

    [Fact]
    public void Unsupported_formats_and_short_textures_are_refused()
    {
        Assert.Throws<NotSupportedException>(() => KfcTexture.Decode("BC5_unorm_block", 4, 4, 1, new byte[16], 128));
        Assert.Throws<InvalidDataException>(() => KfcTexture.Decode("BC7_unorm_block", 8, 8, 1, new byte[48], 128));
        Assert.Throws<InvalidDataException>(() => KfcTexture.Decode("R8G8B8A8_unorm", 0, 4, 1, new byte[16], 128));
    }

    [Fact]
    public void Png_files_hold_the_image()
    {
        var rgba = new byte[5 * 3 * 4];
        Random.Shared.NextBytes(rgba);

        var png = ReadPng(Png.Encode(new IconTexture(5, 3, rgba)));

        Assert.Equal((5, 3), (png.Width, png.Height));
        Assert.Equal(rgba, png.Rgba);
    }

    // ---- A PNG reader for the checks

    static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Reads an 8-bit RGBA, non-interlaced PNG, checking every chunk's CRC.</summary>
    static (int Width, int Height, byte[] Rgba) ReadPng(byte[] png)
    {
        Assert.Equal(Signature, png[..8]);
        int at = 8, width = 0, height = 0;
        var idat = new MemoryStream();
        var end = false;
        while (!end)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = Encoding.ASCII.GetString(png, at + 4, 4);
            var data = png.AsSpan(at + 8, length);
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length));
            Assert.Equal(Crc32.HashToUInt32(png.AsSpan(at + 4, length + 4)), crc);
            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(data);
                    height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                    Assert.Equal([8, 6, 0, 0, 0], data[8..13].ToArray());
                    break;
                case "IDAT":
                    idat.Write(data);
                    break;
                case "IEND":
                    end = true;
                    break;
            }
            at += 12 + length;
        }
        Assert.Equal(png.Length, at);

        var raw = new MemoryStream();
        using (var z = new ZLibStream(new MemoryStream(idat.ToArray()), CompressionMode.Decompress)) z.CopyTo(raw);
        var bytes = raw.ToArray();
        var stride = width * 4;
        Assert.Equal(height * (stride + 1), bytes.Length);
        var rgba = new byte[height * stride];
        for (var y = 0; y < height; y++)
        {
            var filter = bytes[y * (stride + 1)];
            for (var x = 0; x < stride; x++)
            {
                int a = x >= 4 ? rgba[y * stride + x - 4] : 0, b = y > 0 ? rgba[(y - 1) * stride + x] : 0;
                int c = x >= 4 && y > 0 ? rgba[(y - 1) * stride + x - 4] : 0;
                int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                var predictor = filter switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => pa <= pb && pa <= pc ? a : pb <= pc ? b : c,
                    _ => throw new InvalidDataException($"PNG filter {filter}"),
                };
                rgba[y * stride + x] = (byte)(bytes[y * (stride + 1) + 1 + x] + predictor);
            }
        }
        return (width, height, rgba);
    }
}
