# In-game findings

Format per experiment: experiment → observation → decision.

## Session A — 2026-10-25

### Setup and the Steam-folder attempt

- **First attempt:** `make-experiments --remote <Steam remote>` created `character3–8.ksd` in the Steam folder.
- **Observation:** the game listed only the 3 Steam characters. Its log shows it enumerates Steam files through Steam's catalogue: "[savedata2-migration] Found steam file 'character0.ksd'…". That catalogue, `remotecache.vdf`, lists only files the game wrote, so Steam never registered the copies, even after the game started. The copies are invisible and inert.
- **Local characters:**
  - The game also has local characters, stored in `C:\Users\vedra\Saved Games\keen games\Enshrouded\default\` with their own backups in `default\backup\`.
  - Steam and local characters are listed separately ("[LOCAL]" tag) and numbered separately.
  - The owner created a local character "Test" (`character0.ksd`, 278 bytes: `character_data` + `player_character`).
- **Storage tag:** `character_data` bytes 1–4 differ by storage. Steam characters have `93 7B 40 B6`; the local "Test" has `a8 23 b2 28`.
- **Decision (owner):** use local characters. `make-experiments --source <Steam remote> --target <local default> --registry copies-local.json` created local `character1–6.ksd`, carrying the local tag copied from "Test".
  - All Steam files, the local "Test" and the game's backups are verified unchanged by hash.
  - The 6 Steam-folder copies stay in place, unused. Offer the owner a cleanup at the end.

### Results (owner report, plus `watch` captures in `tests/fixtures/experiments/local/`)

| Experiment | Observation | Decision |
|---|---|---|
| All six | Listed as [LOCAL] and all load. The game autosaves each loaded character every ~30 s. Each save changes `character_data` (same size), `player_character`, `voiceover` and `player_knowledge`. | Local copies with the local tag are a valid test path. |
| **Test Plain** | Loads as Amadis level 7, gear and skills unchanged. The rank-0 entries (Wizard, Snake Eater, Dark Arts) are **not learned** and **not connected** in the tree. | Rank 0 = not learned, confirmed. The current SkillsEditor treatment stands. |
| **Test Repack** | Loads at level 9. The XP bar is empty at XP 0, and 4 skill points are available. | Our writer is accepted: every section recompressed at zstd level 3, new revision and time. The points formula is confirmed. XP 0 = an empty bar, which fits XP counting **into the current level** (to confirm with a non-zero value). |
| **Test Slots** | See the slot map below. All 48 counts appear and none is missing. | Set the `InventoryLayout` constants (Task 12). |
| **Test Items** | Health Potion 20 ✓. Fireball I 600 → **shown and saved as 500**: the game clamped it (watch diff: `x600 → x500`). Copper Ore 250 ✓. Wolf's Snarl Longbow level 20, power 30 ✓. Adventurer Helmet level 10 ✓. Ring of Health ✓. The game kept our field sets for every new record. | The cap at MaxStack is correct, because the game clamps anyway. `NewItemFields` defaults are accepted for a bow, armour and a ring. |
| **Test Skills** | All 10 learned. Passives show on the stats. 56 points left (69 − 13) ✓. The owner learned **Athlete** in-game: the game appended entry `F2A8B9BE` at rank 1, exactly as our Learn does. | Learn representation confirmed. |
| **Test Skills (inventory)** | From the 14:11:51 capture on, our parser reports **Inventory NotFound**. The Equipped mask went `ffff → cfff` (bits 12 and 13 cleared, i.e. two positions emptied), and the next header byte went `03 → 02`. **Owner (after the session):** they "took off all gear other than glider and hook". | First observation of an **empty Equipped position**. The strict parser must accept mask holes and work out the meaning of that byte (Task 12). Until then the editor correctly opens such a character with its inventory read-only. **Task 12:** see "Taking off gear" below. Every capture is now `Full`. |
| **Test Overspend …** | The 40-character name shows in full in the character list. On load the game **reset the whole tree**: 30 entries → 20, every rank 0 or removed. | **Drop Unlimited** (owner's decision, spec §10.5). Overspend can't be kept. **Done in Task 12:** `PointsMode` is gone, and `Learn` always applies the points budget. |
| **Name box** (owner, after the session) | The in-game character creator accepts up to **15 characters**. The game still displays the 40-character name written directly. | The editor matches the creator: `CharacterEditor.MaxNameLength = 15` (Task 12, ruling T12-R4 updated). The Lab's direct writes of longer names are unaffected. |

### Taking off gear (Test Skills, `watch` captures in `local/character5/`, Task 12)

The game's 30-second autosaves of "Test Skills" while the owner took off all gear but the glider and the hook:

| Capture | Equipped header | Records | What moved |
|---|---|---|---|
| 13:54:16 (Lab) … 14:11:21 | `ff ff 03 00` | 16: 0–4 appearance, 5 Adventurer Helmet, 6 Chest, 7 Gloves, 8 Trousers, 9 Boots, 10 Lesser Ring of Stamina, 11 Ring of Health, 12 Hero's Shield, 13 Improved Grappling Hook, 14 Advanced Glider, 15 Small Backpack | – |
| 14:11:51 | `ff cf 02 00` | 14: positions 12 and 13 empty, so record 12 is the glider and 13 the backpack | Shield → Inventory 30, hook → Inventory 31: the free **action bar** slots |
| 14:12:19 … 14:12:55 | `1f e0 02 00` | 8: 0–4 appearance, 5 hook (position 13), 6 glider, 7 backpack | Hook back on from 31. Lesser Ring of Stamina → 31; Ring of Health, then the five armour pieces → Extra 1–6 (Inventory 0–39 was full) |

What this settles (Task 12, docs/research/player-character.md §1a and §4):
- **The Equipped header is a u32 field mask.** Bits 0–15 are the positions above, and the mask marks the occupied ones. Records are stored in mask-bit order, and each record's slot byte is its index. The map is confirmed: 0–4 appearance, 5 helmet, 6 chest, 7 arms, 8 legs, 9 boots, 10–11 rings, 12 shield, 13 grappling hook, 14 glider, 15 backpack.
- **Header byte 2 is mask bits 16–18,** and each set bit adds one u16 after the Equipped records. `03` and `05` mean two u16s, `02` one. Our parser assumed two, so it read 2 bytes off from 14:11:51 on. Each of the 55 payloads we have fits this.
- **The game's own placement** put the unequipped shield and hook into free action bar slots (30, 31) before the extension. The editor's Add doesn't do that (ruling T12-R2: base backpack, then extension; the action bar only when targeted).
- **Decision (T12-R3):** the editor now swaps an item with the Equipped item in a position it fits. That keeps the mask, byte 2 and the record count. Equipping into an empty position and unequipping **stay disabled**: they change the mask, and when the game changes byte 2 alongside isn't known (below).

**Byte 2: what is and isn't known.** Read as Inventory slot numbers, the u16 values are:
- character0: bit 16 = 6, bit 17 = 13.
- character1 and Test Skills: bit 16 = 0 (the bow), bit 17 = 1 (Copper Arrow ×88).
- character2: bit 16 = 14, bit 18 = 15.
- character2's backups: (21, 22), (20, 12), (19, 10), (14, 5).

In Test Slots (a character2 copy), the "Q" marker sat on Inventory slot 14, which is bit 16's value. Bit 16 cleared in the same 30 seconds as the shield and the hook came off, while slot 0 still held the bow. Re-equipping the hook and taking off seven armour and ring pieces didn't touch byte 2. One observation can't separate "bit 16 follows the shield" from "bit 16 is a slot reference (the Q marker) that the game dropped for another reason".

### Slot map (Test Slots, a copy of character2 with Small Backpack `_NEW`)

| Save container / slots | In-game place |
|---|---|
| Inventory 0–7 | backpack row 1 |
| Inventory 8–15 | backpack row 2 |
| Inventory 16–23 | action bar row 1 (keys 1–8) |
| Inventory 24–31 | action bar row 2 |
| Inventory 32–39 | backpack row 3 |
| Extra 0–7 | backpack row 4, the first backpack-extension row |
| Extra 8–15 (empty in the test) | backpack row 5. The character screen shows "Backpack Extension 16" for the Small Backpack (`_NEW`, "two rows"). |

- The grid is 8 columns. The base backpack is 3 rows (24 slots: Inventory 0–15 and 32–39). The extension adds 8 × rows-of-the-equipped-backpack in Extra.
- **Equipped** (character2, 11 records): helmet, chest, arms, legs, boots, shield ("Block"), backpack, grappling hook, glider, ring, ring. Weapons live in the action bar, not in Equipped.
- **The ammo slot** on the character screen shows a stack that stays in the inventory. Inventory slot 14 carries a "Q" marker, so ammo is a reference, not a separate record.

### Still open

- ~~The in-game name box limit~~: 15 characters (owner, above).
- How the game accepts an edited **Steam** character, i.e. whether Steam Cloud keeps the edit. It can't be tested on a copy, because Steam never lists copies. It needs either the owner's own Save on a Steam character after the editor is finished, or a game-made Steam throwaway (owner to decide).
- **For session B: when the game sets or clears header byte 2 (Equipped mask bits 16–18).** Until this is answered, equipping into an empty Equipped position and unequipping stay disabled in the editor. Suggested checks, each on a local copy with `watch` running:
  1. Take off **only the shield**, wait for an autosave, then put it back on. Does bit 16 clear and come back? With which u16 value?
  2. Without touching gear, change what the "Q" marker points at (or the ammo slot). Does bit 16's or bit 17's value change?
  3. Take off **only the backpack** (or another non-shield piece). Does byte 2 stay as it is?
  4. If 1–3 show byte 2 is independent of the gear positions: a Lab copy with one armour position emptied by the editor (record removed, mask bit cleared, byte 2 untouched). Does the game load it, show the slot empty, and keep the item in the backpack?
- The meaning of the Equipped u16 values, the inventory header byte x (`01`/`02`), and the 22-byte tail.
