# To do

## 1. Item levels on fixed-level gear (owner report, 2026-10-25)

**What the owner saw:** bows with a level range ("1–50") take the level set in the editor. "Bow Of The Serpent" always shows its own level in-game, whatever level the editor wrote.

**What the save captures show** (`tests/fixtures/experiments/local-b/character7/`, the "Try Me" copy):
- **"Bow Of The Serpent" is three separate items** in the game data, each with a fixed level and rarity: level 21 Rare, level 20 Epic and level 16 Rare.
- **The game overrides a written level on fixed-level gear.** The editor wrote level 22 on the level-21 one, and the game rewrote it to 21. It also recomputed the durability (381 → 356).
- **Range items keep the written level.** Talion's Bow (1–50) kept levels 50 and 18; the game only recomputed the durability.
- **On fixed-level gear in real saves, level = base level + the rarity field's second byte.** That byte looks like an upgrade count:
  - Alchemist Apprentice set: base 5 + 5 = level 10;
  - Lesser Ring of Health: base 5 + 4 = level 9;
  - the one exception is character0's Hero's Shield: level 14, no rarity field.

**Planned fix** (replaces ruling T10-R1's "1..55 for any record with a level field"):
- **Fixed-level items** (reference MinLevel == MaxLevel, including 0–0):
  - The level isn't editable. `SetItemLevel` rejects with "This item's level is set by the game: base {base} plus upgrades."
  - `Add` writes the base level, and the UI shows it read-only.
- **Ranged items** (MinLevel < MaxLevel): the level is editable within MinLevel..MaxLevel, defaulting to the character's level clamped to that range.
- **Upgrades** (rarity byte 2) stay as they are and aren't editable yet. Editing them is a later feature once the rules are known: max upgrades, and how rarity changes.
- **Telling variants apart:** the Add item tiles and the right panel show each variant's own level and base rarity.
- **Durability:** the game recomputes durability, so the copied value is fine.
- **Tests:**
  - adding the level-21 Bow Of The Serpent always writes 21;
  - `SetItemLevel` on a fixed-level item is rejected;
  - Talion's Bow accepts 1..50 and rejects 51;
  - the selected panel and the Add panel show a read-only level for fixed items.
- **This also closes a review note:** the Add dialog always sent an explicit level, which bypassed the save-faithful defaults.

## 2. Remaining plan work

- **Task 19:** read items, the tree and rewards from the install (`enshrouded.kfc`).
- **Task 20:** icons from the install.
- **Task 21:** final acceptance with the owner.
- **Final whole-branch review** (subagent-driven).
- **Fix before the next release** (review notes marked priority):
  - `BackupService.Backup`: a failure mid-copy (disk full) is caught as a name collision and retried with a new suffix, leaving partial backups behind.
  - `CharacterCopier`: the same catch can leave a truncated, unregistered `characterN.ksd` in a save folder.
- **Session B in-game checks** (see `docs/research/in-game-findings.md`, "For session B"):
  - how the game sets equipped header byte 2;
  - then enabling equip into an empty slot, and unequip.
- **Steam characters:** check that an edited Steam character survives Steam Cloud (owner to decide how). Then decide whether saving needs Steam closed (spec §10.2).

## 3. Cleanup

- **Six unused Lab copies in the Steam folder** (`remote\character3–8.ksd`, invisible to the game): delete them with the owner's OK.
- **Local test characters** (`keen games\Enshrouded\default\character1–7.ksd`: "Test …" and "Try Me"): delete them in-game when done.
- **In-game checks:** always use a new throwaway world. Session A's test characters were played in `world0`, and the game saved that world.
