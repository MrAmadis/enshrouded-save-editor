using System.IO.Hashing;

namespace EnshroudedSaveEditor.Core.Container;

public static class KsdChecksum
{
    /// <summary>Low 32 bits of XXH3-64 over the whole block (header included).</summary>
    public static uint Compute(ReadOnlySpan<byte> block) => (uint)XxHash3.HashToUInt64(block);
}
