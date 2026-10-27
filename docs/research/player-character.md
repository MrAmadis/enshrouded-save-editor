# `player_character`: layout research

Status: **2026-10-25, Task 3; revised in Task 12** with the session A captures (§1a). This note covers the bytes after the skill list: the status list and the item containers. The header, level, XP and skills are described in the spec (§6.4) and were verified in Task 2.

**Sources:**
- The owner's three characters (`tests/fixtures/real/remote/character0-2.ksd`).
- The ten older copies of character2 in `tests/fixtures/real/backup/`.
- **Task 12:** the session A Lab copies and the game's 30-second autosaves of them (`tests/fixtures/experiments/`, 42 files, git-ignored). Among them are the "Test Skills" captures `local/character5/20261025-1354…1412` (12 files), taken while the owner took off all gear but the glider and the hook.
- **Task 12:** the game-made local character "Test" (`tests/fixtures/real/local/character0.ksd`, level 1).
- Kimikozi's `lib/pc.js` (MIT), commit `1f9699717b5fbc1ea35df94abebadc0c2a83a9da`. It gave the record format and the `[type][n2][type][n3]` header search.
- The item ids in `itemdb2.json` from the same commit.

**Method:** read-only probes over the decompressed payloads, then the C# implementation (`InventoryScanner`, `ItemRecord`). The implementation reproduces every result below. Nothing outside the fixtures and temp copies was read or written.

Markers: **[13/13]** means verified on the 13 owner payloads. **[55/55]** means verified on those 13 plus the 42 session A files. **[guess]** means it fits the data but hasn't been confirmed.

## 1. Layout

Offsets are into the decompressed `player_character` payload. `S` is the offset of the status count, `58 + 12 + 5 × skills`.

| Part | Bytes | Notes |
|---|---|---|
| Prefix | 0–57 | Opaque. Bytes 9–12 and 13–16 hold Customization item ids (hair and beard, e.g. `VC_Hair_04_06_slick_fade`, `VC_Beard10_balbo`) [13/13]. These are what loose scans from the start mistook for "Customization records with odd counts" (spec §6.4). They are part of the appearance header, not the inventory. |
| Level, XP, skills | 58 … S | Task 2. |
| Status count | S, u32 | 0, 2 or 4 observed. |
| Status entries | count × entry | §2. Kept opaque. |
| Equipped header | 4 | **A u32 field mask** [55/55] (Task 12). Bits 0–15 are the Equipped positions (§4); bits 16–18, header byte 2, flag u16 fields; bits 19–31 have never been set. Seen: `0003ffff` (character0/1), `0005ffe0` (character2), `0002cfff` and `0002e01f` (Test Skills after gear came off). Task 3 read this as `[u16 slot mask][u8 kind][u8 0]`. |
| Equipped records | popcount(bits 0–15) records | [55/55]: 16, 16, 11, 14 and 8 records. They are in mask-bit order, and each record's slot byte is its index, 0…n−1. Record *i* sits at the position of the *i*-th set bit. |
| Equipped u16 fields | 2 × popcount(bits 16–18) | One u16 per set bit, in bit order [55/55] (Task 12). Meaning unknown (§4). Task 3 took two of them as the inventory header's "a, b". |
| Inventory header | 4 | `[u8 05][u8 n2][u8 x][u8 n3]` [55/55]. x is unknown (§4). |
| Inventory records | n2 records | Slots strictly increase, with gaps [13/13]. |
| Extra records | n3 records | Slots restart (character0: slot 2; character1: slot 0) [13/13]. |
| Tail | 22, to the end | Starts `02 00 03 00` [13/13]. The rest: byte 4 varies (`00`, `07`, `0b`, `0f`). Bytes 10–17 are `92 eb f3 77 4f e0 03 f3` on character2 copies and zero on character0/1. Everything else is zero. |

**No scanning is needed.** Every byte from the status count to the end is accounted for. The status list is parsed by its field masks, the Equipped records and u16 fields come from the Equipped mask, and the Inventory/Extra counts are n2/n3. The 22-byte tail must end the data exactly, which anchors the whole parse.

### 1a. Task 12: why the Test Skills captures from 14:11:51 failed

The Task 3 reading fixed 4 bytes between the last Equipped record and the `05`, the "a, b" of an 8-byte inventory header. All 13 owner payloads have two of bits 16–18 set (`03` or `05`), so it always fit. At 14:11:51 the owner had taken off the shield and the grappling hook. The mask went from `0003ffff` (16 records, then `00 00 01 00`) to `0002cfff`: 14 records, then only `01 00`, then `05 28 02 01`. The strict parse read the "inventory header" 2 bytes early, found `02` where it wanted `05`, and gave up. The fallback then found no Equipped run, because Equipped 0–4 are appearance items missing from `itemdb2.json`. So the result was `NotFound`.

The rule that fits all 55 payloads: **each set bit 16–18 adds one u16 after the Equipped records.** For `03` and `05` that is 4 bytes, and for `02` it is 2. It works like the status entries (§2): a field mask, then the fields in bit order. The tests replay the 14:11:51 change on character1 (`Each_header_byte_2_bit_adds_a_u16_after_the_equipped_records`). Every capture is now `Full` and round-trips byte for byte (`Every_experiment_capture_is_fully_recognised_and_round_trips`).

**The fresh local character** ("Test", level 1) has mask `00000100`: one record, Threadbare Pants, at position 8, so it fits the map. After the record come 26 zero bytes, where a played character has `05 n2 x n3` and the `02 00 03 00 …` tail. That shape isn't the strict layout. It opens `InPlaceOnly`, and nothing is guessed for it.

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

1. **Strict layout → `Full`.** The scanner parses the status list by masks and reads the Equipped mask, refusing bits 19–31 because their sizes are unknown. It reads popcount(bits 0–15) Equipped records, whose slot bytes must be 0…n−1, then skips 2 bytes per set bit 16–18. It checks the inventory header's `05`, reads n2 + n3 records (slots strictly increasing in each list), and requires exactly 22 bytes left, starting `02 00 03 00`. Item ids are **not** checked against the reference list here: the layout is the check (see unknown ids below). All 13 owner payloads and all 42 session A files pass.
2. **Fallback → `InPlaceOnly`.** This is Kimikozi's search, ported: an Equipped run starting at slot 0 near the end of the status list, tried just after the parsed status list, then Kimikozi's two windows (27-byte entries; 22–64 bytes per entry). Then his `[type][n2][type][n3]` header search 4–12 bytes after it. Records must pass `isItem`. Even when the header search succeeds, the status stays `InPlaceOnly`, because only the strict layout counts as recognised. Every non-record byte is opaque and every record must keep its size. Kimikozi's loose "Other" runs are **not** ported: they can't be tied to a container or a slot address. *(Task 12)* The Equipped mask, which gives the records' positions, is taken from the 4 bytes before the run only when they fit it: byte 3 is 0 and bits 0–15 count exactly the run's records. Otherwise positions are unknown, and Equipped records can't be addressed.
3. **`NotFound`** when the fallback finds no Equipped run. The tail is then one opaque block.

**Writing (`PlayerCharacter.ToBytes`):**
- Inventory and Extra counts are rewritten from the lists in `Full`.
- The Equipped mask is its own part (`MaskPart`), written back from `PlayerCharacter.EquippedMask`. `Equip` and `Unequip` change its bits 0–15 and the Equipped records together (mask-bit order, slot bytes 0…n−1), and `ToBytes` refuses records the bits don't count. Bits 16–18 and their u16 fields are kept as read (§4). The fallback layouts can't change the Equipped records.
- `ToBytes` refuses anything it couldn't read back: slots out of order, an Equipped slot byte that isn't its index (`Full`), count 0 with fields, a size change in `InPlaceOnly`, or a count change without a known count byte.

**Unknown item ids are kept.** Seven ids found by the strict layout are missing from `itemdb2.json`, yet all seven are in the character's own `player_knowledge`:
- character1's five appearance items (Equipped 0–4: `01e879a5`, `4d3ad296`, `20a5e804`, `07d48488`, `da771a05`);
- character0's `f262a2a9` ×5 (Inventory slot 25);
- backups 4–9's `343449dc` ×16/19 (Inventory slot 21).

So the reference data is incomplete; the records are real. Requiring `isItem` here would have dropped character0 and character1 to `InPlaceOnly`/`NotFound` for no structural reason.

## 4. Guesses and open questions

- **Equipped slot map: confirmed (session A, Task 12).** Record *i* goes in the position of the mask's *i*-th set bit, and the mask marks the **occupied** positions. When the owner took off gear, its bits cleared and the later records moved up (§1a).

  | Bit | Position | Fits (`InventoryLayout.KindOf`) |
  |---|---|---|
  | 0–4 | appearance: helmet, chest, arms, legs, boots | Equipment with no slot, `Vanity_*_Helmet` / `_Chest` / `_Gloves` / `_Legs` / `_Boots`. character0's Exclusive Hollow set is at 0–4 in that order. character1's five appearance ids aren't in the reference data. |
  | 5 | helmet | `EquipSlot.Helmet` |
  | 6 | chest | `Chest` |
  | 7 | arms | `Arms` |
  | 8 | legs | `Legs` |
  | 9 | boots | `Boots` |
  | 10–11 | rings | `Ring` |
  | 12 | shield ("Block") | `Shield`, or Equipment with no slot named `Ward_*`. character2's Spectral Ward (`Ward_T1_Loot`) is at 12. |
  | 13 | grappling hook | Tools, `Tool_Grappling_Hook*` |
  | 14 | glider | Tools, `Tool_Glider_*` |
  | 15 | backpack | Equipment, `Backpack_T0` … `Backpack_T3_NEW` |

  Every Equipped record in all 55 payloads whose id is in the reference data fits its position (`Every_equipped_item_fits_its_position`). Weapons aren't in Equipped: they live in the action bar.
- **Equipped u16 fields (bits 16–18, header byte 2).** The sizes are confirmed, but not the meaning. Read as Inventory slot numbers, the values are:

  | Payload | bit 16 | bit 17 | bit 18 |
  |---|---|---|---|
  | character0 | 6 (empty) | 13 (empty) | – |
  | character1, Test Skills to 14:11:21 | 0 (Wolf's Snarl Longbow) | 1 (Copper Arrow ×88) | – |
  | Test Skills from 14:11:51 | – | 1 (Copper Arrow ×88) | – |
  | character2, backups 0 and 2 | 14 (empty) | – | 15 (ghost) |
  | backup-1 | 21 | – | 22 |
  | backup-3 | 20 | – | 12 |
  | backup-4 | 19 | – | 10 |
  | backups 5–9 | 14 | – | 5 |

  In "Test Slots" (a character2 copy), the game showed a "Q" marker on Inventory slot 14, which is bit 16's value there. So these may be references to Inventory slots **[guess]**. Bit 16 cleared in the same 30 seconds as the shield and the hook came off, while slot 0 still held the bow. Re-equipping the hook and taking off seven armour and ring pieces left byte 2 unchanged. When the game sets or clears these bits is still open. Every capture (2026-10-27): byte 2 changed only in the autosave where the shield and hook came off, and numbers pointing at empty slots are common, so the editor's equip and unequip keep bits 16–18 and their u16s as read (docs/research/in-game-findings.md).
- **Inventory header x:** `01` (character0, backups 5–9) or `02` (character1, character2, backups 0–4, all of Test Skills). It isn't n3, and it isn't the backpack: Tiny Backpack in backups 5–9 and Small elsewhere, yet character0 has a Small Backpack with x = 1. Unknown.
- **Inventory slots: confirmed (session A).** 0–15 are backpack rows 1–2, 16–23 action bar row 1 (keys 1–8), 24–31 action bar row 2, and 32–39 backpack row 3. Extra 0… are the backpack-extension rows, 8 × the equipped backpack's rows (`InventoryLayout`).
- **Tail** (22 bytes): meaning unknown. It is preserved, and its first four bytes anchor the parse.
- **Status field meanings**, A/B: unknown, preserved.
- **Transmog (bit 3)** is unverified here, because no record uses it. The size comes from Kimikozi.

## 5. Per-fixture results

From the implementation (`PlayerCharacter.Parse(data, ItemIds.Contains)`). "Round trip" means `ToBytes()` equals the input byte for byte. Kimikozi's original `parsePlayer` result is shown for comparison.

| Payload | Bytes | Status entries | Equipped mask | Result | Equipped / Inventory / Extra | Round trip | Unknown ids | Kimikozi |
|---|---|---|---|---|---|---|---|---|
| character0 | 498 | 0 | `0003ffff` | Full | 16 / 18 / 1 | yes | Inv 25 `f262a2a9` | equipped only; header search fails at slot 25 |
| character1 | 786 | 2 (27 + 30) | `0003ffff` | Full | 16 / 38 / 1 | yes | Eq 0–4 (appearance) | nothing found (slot 0 unknown id) |
| character2 | 706 | 2 (27 + 30) | `0005ffe0` | Full | 11 / 21 / 0 | yes | none | full |
| backup-0 | 706 | 2 (27 + 30) | `0005ffe0` | Full | 11 / 21 / 0 | yes | none | full |
| backup-1 | 762 | 2 (27 + 30) | `0005ffe0` | Full | 11 / 28 / 0 | yes | none | full |
| backup-2 | 706 | 2 (27 + 30) | `0005ffe0` | Full | 11 / 21 / 0 | yes | none | full |
| backup-3 | 795 | 4 (31 + 31 + 31 + 30) | `0005ffe0` | Full | 11 / 26 / 0 | yes | none | full |
| backup-4 | 637 | 2 (31 + 30) | `0005ffe0` | Full | 11 / 21 / 0 | yes | Inv 21 `343449dc` | equipped only |
| backup-5 | 552 | 2 (27 + 30) | `0005ffe0` | Full | 11 / 16 / 0 | yes | Inv 21 `343449dc` | equipped only |
| backup-6 | 552 | 2 (27 + 30) | `0005ffe0` | Full | 11 / 16 / 0 | yes | Inv 21 `343449dc` | equipped only |
| backup-7 | 552 | 2 (27 + 30) | `0005ffe0` | Full | 11 / 16 / 0 | yes | Inv 21 `343449dc` | equipped only |
| backup-8 | 552 | 2 (27 + 30) | `0005ffe0` | Full | 11 / 16 / 0 | yes | Inv 21 `343449dc` | equipped only |
| backup-9 | 552 | 2 (27 + 30) | `0005ffe0` | Full | 11 / 16 / 0 | yes | Inv 21 `343449dc` | equipped only |
| Test Skills 13:54:16 … 14:11:21 (8) | 836 | 2 (27 + 30) | `0003ffff` | Full | 16 / 38 / 1 | yes | Eq 0–4 | – |
| Test Skills 14:11:51 | 834 | 2 (27 + 30) | `0002cfff` | Full (was NotFound) | 14 / 40 / 1 | yes | Eq 0–4 | – |
| Test Skills 14:12:19 … 14:12:55 (3) | 834 / 839 | 2 (27 + 30) | `0002e01f` | Full (was NotFound) | 8 / 40 / 7 | yes | Eq 0–4 | – |
| the other 30 session A files | | | `0003ffff` / `0005ffe0` | Full | | yes | | – |
| local "Test" (game-made, level 1) | 112 | 0 | `00000100` | InPlaceOnly (§1a) | 1 / – / – | yes | none | – |

The brief's minimum was character2 `Full` and character0/1 at least `InPlaceOnly`. Because the strict layout fits, character0 and character1 are now `Full`, and their tests assert that.

**Fallback coverage:** the fallback (`InPlaceOnly`) is exercised by tests on modified copies of character2: a broken tail anchor, a status entry with an unknown mask bit, an unknown Equipped mask bit (19), and an Equipped slot byte that isn't its index. `NotFound` is tested on a payload with no records.

## 6. Consequences for later tasks

- **Task 7** (`Every_saved_skill_and_item_id_is_known`: "every located ItemRecord.ItemId in Items"). This will fail as written for character0 (`f262a2a9`) and character1 (five appearance ids), because the reference item list lacks ids the game itself knows.
- **Task 10 / Task 16:** these tests use character1 as the `InPlaceOnly` example (`InPlaceOnly_allows_count_but_not_add`, `Add_disabled_for_InPlaceOnly_character`). character1 is now `Full`. An `InPlaceOnly` case can be made the way `InventoryTests` does it: break the tail anchor of a character2 copy.
- **Task 10:** Equipped add/remove/move must stay blocked (`ToBytes` throws on an Equipped count change). Equipped edits in place (count, level, rarity, durability) are fine. *(Task 12: swaps with an item that fits the position are allowed, because they keep the mask and the count. Equipping into an empty position and unequipping stay blocked, §4.)*
- **Spec §6.4:** the note that "Customization items … sit among the records" is explained in §1 above. They are in the prefix, and no inventory record is a Customization item in any of the 13 payloads.
