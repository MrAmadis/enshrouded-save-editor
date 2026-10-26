namespace EnshroudedSaveEditor.GameData.Kfc;

/// <summary>
/// Decodes a <c>keen::UiTextureResource</c>'s content: its mip levels one after the other, largest first, each half the
/// size of the one before (rounded down). The formats icons use are read: <c>BC7_unorm_block</c> and
/// <c>R8G8B8A8_unorm</c>. See docs/research/kfc-format.md, "Icons".
/// </summary>
internal static class KfcTexture
{
    /// <summary>
    /// The first mip level whose width and height are at most <paramref name="maxSize"/> (else the smallest), as RGBA.
    /// Throws <see cref="NotSupportedException"/> for another format and <see cref="InvalidDataException"/> for a size the
    /// content doesn't hold.
    /// </summary>
    public static IconTexture Decode(string format, int width, int height, int levels, ReadOnlySpan<byte> content, int maxSize)
    {
        var bc7 = format switch
        {
            "BC7_unorm_block" => true,
            "R8G8B8A8_unorm" => false,
            _ => throw new NotSupportedException($"Texture format {format} is not read."),
        };
        if (width <= 0 || height <= 0 || levels <= 0)
            throw new InvalidDataException($"A {width}×{height} texture with {levels} levels.");

        long offset = 0;
        for (var level = 1; level < levels && Math.Max(width, height) > maxSize; level++)
        {
            offset += LevelSize(bc7, width, height);
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
        }
        if (offset + LevelSize(bc7, width, height) > content.Length)
            throw new InvalidDataException($"The texture's content ({content.Length} bytes) is too short for its size.");
        var data = content[(int)offset..];

        var rgba = new byte[width * height * 4];
        if (!bc7)
        {
            data[..rgba.Length].CopyTo(rgba);
            return new IconTexture(width, height, rgba);
        }
        Span<byte> block = stackalloc byte[4 * 4 * 4];
        var blocksWide = (width + 3) / 4;
        for (var by = 0; by < (height + 3) / 4; by++)
            for (var bx = 0; bx < blocksWide; bx++)
            {
                Bc7.DecodeBlock(data.Slice((by * blocksWide + bx) * 16, 16), block, 0, 16);
                // Blocks at the right and bottom edges may stick out of the image.
                int columns = Math.Min(4, width - bx * 4), rows = Math.Min(4, height - by * 4);
                for (var y = 0; y < rows; y++)
                    block.Slice(y * 16, columns * 4).CopyTo(rgba.AsSpan(((by * 4 + y) * width + bx * 4) * 4));
            }
        return new IconTexture(width, height, rgba);
    }

    static long LevelSize(bool bc7, int width, int height) =>
        bc7 ? (long)((width + 3) / 4) * ((height + 3) / 4) * 16 : (long)width * height * 4;
}
