# Map: Fog of War and Locations — Design

Date: 2026-10-26
Status: **Clear fog of war is built** and works in-game (owner, 2026-10-27; §8, check 1). **Discover all locations is on hold** (owner, 2026-10-27: "just the fog of war removal, not location discoveries"); its findings stay below for later.

## 1. Intent

The owner asked for "an option to unlock all fog of war (have the whole map explored for a character)", then for **separate options**: one to remove all fog of war, and one to discover all locations (owner, 2026-10-26).

This revisits the main spec (`2026-10-25-enshrouded-save-editor-design.md`), whose §3 lists fog of war and `player_knowledge` edits as v1 non-goals.

## 2. Goals

1. **Clear fog of war** (owner): one action reveals the whole map for the open character.
2. **Discover all locations** (owner): one action marks the map's locations as discovered for the open character. **On hold** (owner, 2026-10-27).
3. The two are separate options (owner).

## 3. Non-goals *(my calls)*

- Writing world saves (`worldN.ksd`), as in the main spec. Both options touch only the character file.
- Setting a marker's **completed** state. For an Elixir Well, completed is its skill-point reward, and the owner declined adding points through reward ids (main spec §10.5).
- Revealing part of the map (by region or radius), putting fog back, or a map preview. Restoring a backup is the way back.
- Creating a `fogofwar` section for a character that has none (§5).

## 4. Anti-goals

- Granting skill points, achievements or quest progress as a side effect.
- Weakening the main spec's write guarantees (backup first, valid checksums, untouched data byte-identical, read back before success).

## 5. What we know: fog of war (verified on the owner's saves)

- **Section:** `fogofwar`, type 16, data version 1, 1,638,413 bytes raw on all three Steam characters (main spec §6.2).
- **Layout:**
  - a 13-byte header, identical on every save: `05 00 19 00 00 00 00 00 46 47 4F 57 02` (`FGOW` at offset 8). Its meaning is unknown; keep it byte-identical;
  - then a **1280 × 1280 grid, one byte per cell**, row-major (13 + 1,638,400 = 1,638,413).
- **Values:** 0 is fogged and 255 is cleared. The values in between are the soft edges of the cleared circles along the paths the character walked (seen by rendering the grid as an image).
- **How much is cleared:** about 5.7 % of the cells on character0, 11.4 % on character1, 2.5 % on character2. The world's playable area doesn't fill the grid, so these aren't "percent explored".
- **Orientation:** which axis is north isn't known, and it doesn't matter for clearing everything.
- **Where else:** the world save has no fog section (`world2.ksd` has 13 sections, none of them fog). Fog is per character.
- **Missing section:** a character made in the game and never played in a world has no `fogofwar` section. The local "Test" character has only `character_data` and `player_character`.

## 6. What we know: locations (from the install and the owner's saves)

All of this was read from the install on this machine with throwaway readers built on the editor's own `KsdFile` and kfc reader (`docs/research/kfc-format.md`). The readers aren't in the repo.

### 6.1 Markers

- **Templates:** a map marker is an entity template (`keen::ecs::TemplateResource`) with a `keen::ecs::MapMarker` component. **753** templates have one.
- **Marker types:** they come from `keen::MapMarkerRegistryResource`, which has 49 types. A marker's type id is FNV-1a of its `markerType` reference's 16 bytes, the same rule as loca tags. Each type has a sorting category, an `isFastTravelDestination` flag and a `showAboveFogOfWar` flag.

  | Category | Templates |
  |---|---|
  | Locations | 270 |
  | None | 178 + 26 without `showAboveFogOfWar` |
  | Dungeons | 128 |
  | FlameRelated | 74 |
  | Fast-travel destinations | 21 (Ancient Spires, home-base altars, Flame Altar, Cinder Vault) |
  | Type not in the registry (NPC workshops and similar) | 56 |

- **Names:** they come from the marker's loca tag, or from the template's `LocaTagComponent`. Examples: "Midhollow", "Fox Chase", "Elixir Well", "Gem Forge", "Ancient Obelisk", "Springlands" (the Springlands Ancient Spire).

### 6.2 Unlock rules

- **413 templates** carry `keen::ecs::RuntimeMapMarkerUnlockRequirements`. It holds three conditions: **hint**, **visible** and **completed**. Each condition is a `KnowledgeQueryData` (knowledge or query id, type, compare operator, compare value, explicit-player flag).
- **Visible conditions:**
  - 307 are `SimpleBool Equals 1` on one knowledge id: 275 in the player registry, and 32 listed in both the player and the world registry;
  - 69 are `Extern`, a query from `keen::GameKnowledgeQueryResourceDb`;
  - 37 are empty.
- **The ids a "discover all" would set:** across the hint and visible conditions there are **456 distinct `SimpleBool` knowledge ids**.
  - All 456 are `Bool` entries in `keen::GameKnowledgeResource.playerKnowledge`.
  - **None** is in its `skillPoints` list or its `achievements` list (58 ids).
  - **288** have an `experienceGainId`, so they give XP when discovered normally.
- **In the saves:** these ids sit in `player_knowledge` with value 1. character0 has 49 of them, character1 96, character2 30.
- **No rules (340 templates):** most named places, for example Midhollow, Fox Chase and Springlands Sky Port. Also every Ancient Spire (`MapMarker_Tower_*_FastTravel`) except Springlands', the home-base altars, and teleport gates. What makes these markers show isn't known. **My guess:** they show once the fog over them is cleared.

### 6.3 How the game records a discovery

- Placed entities carry a `keen::ecs::SceneKnowledgeTrigger` spawn component, in `keen::SceneEntityChunkResource` spawn data.
- The component holds up to eight operations. Each operation has a context (Interact, Kill, Toggle, Range, MiningNode) and sets a knowledge id with a type and a value.
- Walking into range runs the Range operations.
- This hasn't been read further. It may be where the rule-less markers and Ancient Spire activation get their knowledge.

### 6.4 `player_knowledge` order

- The entries aren't sorted by id in any of the three saves.
- The order is probably the order the knowledge was learned.
- The editor already snapshots and writes this section (`SaveSession`), but no edit changes it yet.

## 7. The product

§7.2 is built as described; §7.3 is on hold and was never approved.

### 7.1 Where *(my guess)*

A **Map** panel on the Character tab, below Skill points, with one row per option. Each row shows the character's current state and a button. Only the Fog row exists for now.

### 7.2 Clear fog of war

- **What it does:** sets every grid cell to 255 and leaves the header untouched. The section is recompressed and written under the main spec's writer rules. A grid that is all 255 compresses to almost nothing.
- **Status:** "Partly explored", or "The whole map is revealed". When the grid is fully cleared, the button is disabled.
- **No fog section:** the button is disabled, with the reason ("This character hasn't been in a world yet, so it has no map.").
- **A fog section in another layout** (another length, or no `FGOW`): the button is disabled, with the reason, and the section is left untouched. The rest of the character stays editable. A different data version already opens the whole character read-only (`SupportedVersions`).
- **Pending line:** "Cleared fog of war".
- **Undo:** goes back to the original grid. The fog record is immutable, so an undo snapshot keeps a reference to it rather than a 1.6 MB copy *(my call)*.

### 7.3 Discover all locations

- **What it does:** adds each of the §6.2 hint and visible ids that the character doesn't have yet, with value 1.
  - New entries go after the existing ones. The game appended a learned skill the same way (main spec §10.5) *(my call)*.
  - The version and the existing entries stay untouched.
  - Completed conditions are never set.
- **Status and pending line:** how many of the markers the option can discover are already discovered; pending line "Discovered N locations" *(my guess at the wording)*.
- **Game data:** the ids come from the install, as a new piece of `GameData` cached with the rest. The embedded reference data has no markers, so when the install can't be read, this option is disabled, with the reason *(my call)*.
- **Known trade-off:** for 288 of the ids, the game normally gives discovery XP when you arrive. If the editor sets them first, that XP is probably not given later.

### 7.4 Both options

- They follow the editor's usual flow:
  - nothing is written until **Save changes**;
  - each option adds a line to the unsaved-edits bar;
  - **Undo** reverts it;
  - Backups restore the file after saving.
- A read-only session disables both, like every other edit.

## 8. Risks and in-game checks

Every check runs on a **local test copy** made with the Lab tool, in a new throwaway world. That is the owner's rule from main spec §10.

1. **Fog fully cleared:** the game loads the grid, the whole map shows clear, and the result is kept after the game saves. Cells outside the playable area cause no problem. **Works** (owner, 2026-10-27; `docs/research/in-game-findings.md`).
2. **Rule-less markers** (not reported yet): after clearing the fog alone, are Midhollow, Fox Chase and the Ancient Spires shown? This settles the guess in §6.2.
3. **Discover:**
   - the game loads the appended entries and shows the markers;
   - it keeps them after it saves;
   - nothing else changes: quests, skill points and achievements.
4. **The 32 ids in both registries:** is the player side enough?
5. **Ancient Spires:** are they usable for fast travel after Discover? This only matters if the owner wants fast travel (§9).
6. **Discovery XP:** none is granted on load. Check whether visiting a discovered place later gives any.

## 9. Open questions for the owner

On hold with Discover all locations (owner, 2026-10-27).

1. **Fast travel:** is being able to fast-travel to every Ancient Spire part of "discover all locations", or is seeing every place on the map enough? If fast travel is in, finding where spire activation is stored is more research (§6.3), and it may turn out to be in the world save, which the editor doesn't write.

## 10. Left to the builder

- How the marker ids are read and cached, and how the panel is laid out.
- How the fog edit and the knowledge edit fit `SaveSession`, `SessionSnapshot` and undo, keeping the main spec's guarantees.
- Tests: on fixture copies, clearing the fog changes only the grid bytes and Discover only appends entries; a round trip is byte-identical when neither option is used.
