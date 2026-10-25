# `player_character`: layout research

Status: **2026-10-25, Task 3.** This note covers the bytes after the skill list: the status list and the item containers. The header, level, XP and skills are described in the spec (§6.4) and were verified in Task 2.

**Sources:**
- The owner's three characters (`tests/fixtures/real/remote/character0-2.ksd`).
- The ten older copies of character2 in `tests/fixtures/real/backup/`.
- Kimikozi's `lib/pc.js` (MIT), commit `1f9699717b5fbc1ea35df94abebadc0c2a83a9da`. It gave the record format and the `[type][n2][type][n3]` header search.
- The item ids in `itemdb2.json` from the same commit.

**Method:** read-only probes over the decompressed payloads of all 13 copies, then the C# implementation (`InventoryScanner`, `ItemRecord`). The implementation reproduces every result below. Nothing outside the fixtures and temp copies was read or written.

Markers: **[13/13]** means verified on all 13 payloads. **[guess]** means it fits the data but hasn't been confirmed.

## 1. Layout

Offsets are into the decompressed `player_character` payload. `S` is the offset of the status count, `58 + 12 + 5 × skills`.

| Part | Bytes | Notes |
|---|---|---|
| Prefix | 0–57 | Opaque. Bytes 9–12 and 13–16 hold Customization item ids (hair and beard, e.g. `VC_Hair_04_06_slick_fade`, `VC_Beard10_balbo`) [13/13]. These are what loose scans from the start mistook for "Customization records with odd counts" (spec §6.4). They are part of the appearance header, not the inventory. |
| Level, XP, skills | 58 … S | Task 2. |
| Status count | S, u32 | 0, 2 or 4 observed. |
| Status entries | count × entry | §2. Kept opaque. |
| Equipped header | 4 | `[u16 slot mask][u8 kind][u8 0]` [13/13]. kind = `03` on character0/1, `05` on every character2 copy. The mask is `ffff` (character0/1) or `ffe0` (character2). |
| Equipped records | popcount(mask) records | [13/13]: 16, 16 and 11 records. The slot bytes run 0…n−1 with no gaps. |
| Inventory header | 8 | `[u16 a][u16 b][u8 05][u8 n2][u8 x][u8 n3]` [13/13]. Byte 4 is always `05`. a, b and x are unknown (§4). |
| Inventory records | n2 records | Slots strictly increase, with gaps [13/13]. |
| Extra records | n3 records | Slots restart (character0: slot 2; character1: slot 0) [13/13]. |
| Tail | 22, to the end | Starts `02 00 03 00` [13/13]. The rest: byte 4 varies (`00`, `07`, `0b`, `0f`). Bytes 10–17 are `92 eb f3 77 4f e0 03 f3` on character2 copies and zero on character0/1. Everything else is zero. |

**No scanning is needed.** Every byte from the status count to the end is accounted for: the status list is parsed by its field masks, the Equipped count comes from the mask, and the Inventory/Extra counts are n2/n3. The 22-byte tail must end the data exactly, which anchors the whole parse.

### Item record (Kimikozi, confirmed on all records here)

`[u8 slot][u32 item id][u16 count][u8 field flags]`, then the optional fields in bit order:

| Bit | Size | Meaning |
|---|---|---|
| 0 | 1 | item level |
| 1 | 8 | u32 durability, u32 max durability |
| 2 | 8 | unknown. Seen only on Water Pouch (`04`/`05`), e.g. `9b 00 00 00 00 05 00 00` |
| 3 | 4 | appearance (transmog) reference. **Not present in any of the 13 payloads.** Its size comes from Kimikozi. |
| 4 | 2 | u8 rarity, u8 extra (`05 05`, `05 00`, `04 04`, `04 00` seen) |
| 5 | 5 | gem socket `[u32 gem id][u8 marker]`; all zero (empty socket) on every weapon here |

Bits 6–7 have no known size, so a record with them set is not a record. A record with count 0 must have no fields (a **ghost**: a remembered empty stack, e.g. Bronze Sword ×0 in character2's slot 15). Observed flag values over all 429 records: `00`×221, `11`×87, `13`×46, `33`×36, `03`×16, `04`×12, `31`×6, `02`×2, `05`×2, `01`×1.

## 2. The status list

Kimikozi treats status entries as "usually 27 bytes" and scans past longer ones. They are in fact self-describing:

`[u32 effect id][u64 A][u64 B][u16 field mask]`, then one field per mask bit, in bit order:

| Mask bit | Size | Observed content |
|---|---|---|
| 1 | 4 | varies (e.g. `2f 2f 1c 5a`) |
| 2 | 4 | `00 00 80 3f` (f32 1.0) every time |
| 3 | 1 | `07` every time |
| 4 | 8 | a negative i64 (e.g. `0a f6 44 d7 37 fd ff ff`) |

Bits 0 and 5–15 have never been seen, so their sizes are unknown. An entry using one makes the strict parse fail (§3). The 2/3 split is a **[guess]**: masks `0c` and `0e` fix the total for bits 2+3 at 5 bytes, but no entry has only one of them. The order f32-then-u8 is the natural reading.

| Effect id | Mask | Length | Seen in |
|---|---|---|---|
| `89cb0b34` | `0c` | 27 | character1, character2, backups 0–2, 5–9 (A = B = 0) |
| `cdbe9c80` | `10` | 30 | every copy with status entries |
| `c6a35bfe` | `0e` | 31 | backups 3, 4 |
| `a162c0d2` | `0e` | 31 | backup 3 |
| `f9b364f6` | `0e` | 31 | backup 3 |

This is why character1's two entries "take ~57 bytes" (27 + 30), not 54, and why the bytes before its first Equipped record looked irregular. The four bytes `ff ff 03 00` / `e0 ff 05 00` that earlier probes saw before the records are the Equipped header. A, B and the field meanings are unknown. Some look like timestamps or durations.

## 3. How the editor reads it (`InventoryScanner`)

1. **Strict layout → `Full`.** The scanner parses the status list by masks, reads popcount(mask) Equipped records, checks the header's `05`, reads n2 + n3 records (slots strictly increasing in each list), and requires exactly 22 bytes left, starting `02 00 03 00`. Item ids are **not** checked against the reference list here: the layout is the check (see unknown ids below). All 13 payloads pass.
2. **Fallback → `InPlaceOnly`.** This is Kimikozi's search, ported: an Equipped run starting at slot 0 near the end of the status list, tried just after the parsed status list, then Kimikozi's two windows (27-byte entries; 22–64 bytes per entry). Then his `[type][n2][type][n3]` header search 4–12 bytes after it. Records must pass `isItem`. Even when the header search succeeds, the status stays `InPlaceOnly`, because only the strict layout counts as recognised. Every non-record byte is opaque and every record must keep its size. Kimikozi's loose "Other" runs are **not** ported: they can't be tied to a container or a slot address.
3. **`NotFound`** when the fallback finds no Equipped run. The tail is then one opaque block.

**Writing (`PlayerCharacter.ToBytes`):**
- Inventory and Extra counts are rewritten from the lists in `Full`.
- The Equipped count can't change: the mask's meaning (§4) isn't known well enough to update it.
- `ToBytes` refuses anything it couldn't read back: slots out of order, count 0 with fields, a size change in `InPlaceOnly`, or a count change without a known count byte.

**Unknown item ids are kept.** Seven ids found by the strict layout are missing from `itemdb2.json`, yet all seven are in the character's own `player_knowledge`:
- character1's five appearance items (Equipped 0–4: `01e879a5`, `4d3ad296`, `20a5e804`, `07d48488`, `da771a05`);
- character0's `f262a2a9` ×5 (Inventory slot 25);
- backups 4–9's `343449dc` ×16/19 (Inventory slot 21).

So the reference data is incomplete; the records are real. Requiring `isItem` here would have dropped character0 and character1 to `InPlaceOnly`/`NotFound` for no structural reason.

## 4. Guesses and open questions

- **Equipped slot map [guess, fits all 13].** Record *i* goes in the position of the mask's *i*-th set bit:

  | Bit | Position |
  |---|---|
  | 0–4 | appearance (vanity) head, chest, hands, legs, feet |
  | 5 | head |
  | 6 | chest |
  | 7 | hands |
  | 8 | legs |
  | 9 | feet |
  | 10–11 | rings |
  | 12 | shield / off-hand |
  | 13 | grappling hook |
  | 14 | glider |
  | 15 | backpack |

  This resolves the earlier contradiction: character2's records 0–4 (Alchemist Apprentice armour) sit at bits 5–9 because its mask is `ffe0` (no appearance slots). Its rings sit at 10–11, the Spectral Ward at 12, and so on. The item categories agree: every helmet is on bit 5, chest 6, arms 7, legs 8, boots 9, rings 10–11. It is unknown whether the mask means "occupied positions" or "the positions this character has". In every save, every position in the mask is filled, so we have never seen an empty equipment slot. Until an in-game test shows one, the editor must not add, remove or move Equipped records. `ToBytes` enforces the count part of that.
- **kind byte** (`03` / `05`): it might be a layout version. It correlates with the mask but nothing confirms that.
- **Inventory header a, b** (u16 pairs): (6, 13), (0, 1), (14, 15), (21, 22), (20, 12), (19, 10), (14, 5). They might be selected hotbar or quick-slot indices **[guess]**. They are preserved untouched.
- **Inventory header x:** `01` (character0, backups 5–9) or `02` (character1, character2, backups 0–4). It isn't n3, and it isn't the backpack: Tiny Backpack in backups 5–9 and Small elsewhere, yet character0 has a Small Backpack with x = 1. Unknown.
- **Inventory slots:** 0–39 seen. Slots 0–14 are used only sometimes (character1 fills 0–29; character2 starts at 15). Whether 0–14 are the hotbar and 15+ the backpack grid is unknown (§10 of the spec).
- **Tail** (22 bytes): meaning unknown. It is preserved, and its first four bytes anchor the parse.
- **Status field meanings**, A/B: unknown, preserved.
- **Transmog (bit 3)** is unverified here, because no record uses it. The size comes from Kimikozi.

## 5. Per-fixture results

From the implementation (`PlayerCharacter.Parse(data, ItemIds.Contains)`). "Round trip" means `ToBytes()` equals the input byte for byte. Kimikozi's original `parsePlayer` result is shown for comparison.

| Payload | Bytes | Status entries | Equipped mask | Result | Equipped / Inventory / Extra | Round trip | Unknown ids | Kimikozi |
|---|---|---|---|---|---|---|---|---|
| character0 | 498 | 0 | `ffff` (kind 03) | Full | 16 / 18 / 1 | yes | Inv 25 `f262a2a9` | equipped only; header search fails at slot 25 |
| character1 | 786 | 2 (27 + 30) | `ffff` (kind 03) | Full | 16 / 38 / 1 | yes | Eq 0–4 (appearance) | nothing found (slot 0 unknown id) |
| character2 | 706 | 2 (27 + 30) | `ffe0` (kind 05) | Full | 11 / 21 / 0 | yes | none | full |
| backup-0 | 706 | 2 (27 + 30) | `ffe0` | Full | 11 / 21 / 0 | yes | none | full |
| backup-1 | 762 | 2 (27 + 30) | `ffe0` | Full | 11 / 28 / 0 | yes | none | full |
| backup-2 | 706 | 2 (27 + 30) | `ffe0` | Full | 11 / 21 / 0 | yes | none | full |
| backup-3 | 795 | 4 (31 + 31 + 31 + 30) | `ffe0` | Full | 11 / 26 / 0 | yes | none | full |
| backup-4 | 637 | 2 (31 + 30) | `ffe0` | Full | 11 / 21 / 0 | yes | Inv 21 `343449dc` | equipped only |
| backup-5 | 552 | 2 (27 + 30) | `ffe0` | Full | 11 / 16 / 0 | yes | Inv 21 `343449dc` | equipped only |
| backup-6 | 552 | 2 (27 + 30) | `ffe0` | Full | 11 / 16 / 0 | yes | Inv 21 `343449dc` | equipped only |
| backup-7 | 552 | 2 (27 + 30) | `ffe0` | Full | 11 / 16 / 0 | yes | Inv 21 `343449dc` | equipped only |
| backup-8 | 552 | 2 (27 + 30) | `ffe0` | Full | 11 / 16 / 0 | yes | Inv 21 `343449dc` | equipped only |
| backup-9 | 552 | 2 (27 + 30) | `ffe0` | Full | 11 / 16 / 0 | yes | Inv 21 `343449dc` | equipped only |

The brief's minimum was character2 `Full` and character0/1 at least `InPlaceOnly`. Because the strict layout fits, character0 and character1 are now `Full`, and their tests assert that.

**Fallback coverage:** the fallback (`InPlaceOnly`) is exercised by tests on modified copies of character2: a broken tail anchor, and a status entry with an unknown mask bit. `NotFound` is tested on a payload with no records.

## 6. Consequences for later tasks

- **Task 7** (`Every_saved_skill_and_item_id_is_known`: "every located ItemRecord.ItemId in Items"). This will fail as written for character0 (`f262a2a9`) and character1 (five appearance ids), because the reference item list lacks ids the game itself knows.
- **Task 10 / Task 16:** these tests use character1 as the `InPlaceOnly` example (`InPlaceOnly_allows_count_but_not_add`, `Add_disabled_for_InPlaceOnly_character`). character1 is now `Full`. An `InPlaceOnly` case can be made the way `InventoryTests` does it: break the tail anchor of a character2 copy.
- **Task 10:** Equipped add/remove/move must stay blocked (`ToBytes` throws on an Equipped count change). Equipped edits in place (count, level, rarity, durability) are fine.
- **Spec §6.4:** the note that "Customization items … sit among the records" is explained in §1 above. They are in the prefix, and no inventory record is a Customization item in any of the 13 payloads.
