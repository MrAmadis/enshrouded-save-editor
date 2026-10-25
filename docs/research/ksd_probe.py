"""Read-only probe of an Enshrouded 1.0 .ksd (SV2C) container."""
import struct, sys, zstandard, xxhash
from pathlib import Path

def u16(b, o): return struct.unpack_from("<H", b, o)[0]
def u32(b, o): return struct.unpack_from("<I", b, o)[0]

path = Path(sys.argv[1])
out = Path(sys.argv[2]) if len(sys.argv) > 2 else None
b = path.read_bytes()
assert b[:4] == b"SV2C", b[:4]
print(f"{path.name}: version={u16(b,4)} header={u16(b,6)} size={u32(b,8)} (actual {len(b)}) rev={u32(b,28)}")
table_at = 0x28 + u32(b, 0x28)
count = u32(b, 0x2C)
print(f"  table at 0x{table_at:x}, {count} sections")
for i in range(count):
    at = table_at + i * 20
    name_off, name_len, data_off, data_size, h = struct.unpack_from("<IIIII", b, at)
    name = b[at + name_off: at + name_off + name_len].decode("utf-8", "replace")
    block = b[data_off: data_off + data_size]
    tag = block[:4]
    bver, type_id, comp, dver = u32(block, 4), block[8], block[9], u32(block, 10)
    body = block[14:]
    raw = zstandard.ZstdDecompressor().decompress(body, max_output_size=64 << 20) if comp == 1 else body
    h64 = xxhash.xxh3_64_intdigest(block)
    print(f"  [{i}] {name!r:28} off={data_off:6} size={data_size:6} tag={tag} bver={bver} type={type_id} zstd={comp} dver={dver}"
          f" raw={len(raw):7} hash_ok={(h64 & 0xFFFFFFFF) == h}")
    if out:
        out.mkdir(parents=True, exist_ok=True)
        (out / f"{path.stem}.{i}.{name}.bin").write_bytes(raw)
