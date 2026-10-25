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
| **Test Skills (inventory)** | From the 14:11:51 capture on, our parser reports **Inventory NotFound**. The Equipped mask went `ffff → cfff` (bits 12 and 13 cleared, i.e. two positions emptied), and the next header byte went `03 → 02`. | First observation of an **empty Equipped position**. The strict parser must accept mask holes and work out the meaning of that byte (Task 12). Until then the editor correctly opens such a character with its inventory read-only. Owner to confirm what was unequipped. |
| **Test Overspend …** | The 40-character name shows in full in the character list. On load the game **reset the whole tree**: 30 entries → 20, every rank 0 or removed. | **Drop Unlimited** (owner's decision, spec §10.5). Overspend can't be kept. |

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

- The in-game name box limit (owner to report). The game displays a 40-character name in full.
- How the game accepts an edited **Steam** character, i.e. whether Steam Cloud keeps the edit. It can't be tested on a copy, because Steam never lists copies. It needs either the owner's own Save on a Steam character after the editor is finished, or a game-made Steam throwaway (owner to decide).
- The meaning of the Equipped header byte (`03`/`02`/`05`) and the emptied positions.
