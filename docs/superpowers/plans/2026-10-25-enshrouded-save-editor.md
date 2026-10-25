# Enshrouded Save Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A double-click Windows `.exe`, styled like Enshrouded, that edits an Enshrouded 1.0 character's inventory, gear, skills, level, XP and name, and writes `.ksd` saves the game accepts.

**Architecture:**
- All save logic lives in a tested .NET library, `Core`: the `SV2C` container, the records, the editors, and the session with backups and guards.
- Game data sits behind one interface in `GameData`. The first build embeds the reference data from Kimikozi's editor. A later phase adds a reader for the owner's install, which takes over piece by piece.
- A Razor class library, `UI`, holds the screens and an in-process `EditorService`.
- A WinForms window, `App`, hosts them through `BlazorWebView`.
- A console tool, `Lab`, makes test copies of characters and runs the in-game experiments.

**Tech Stack:** .NET 10 (C#), Blazor Hybrid (WinForms `BlazorWebView`, WebView2), ZstdSharp.Port (zstd), System.IO.Hashing (XXH3), xunit.v3, bUnit.

**Spec:** `docs/superpowers/specs/2026-10-25-enshrouded-save-editor-design.md`. Read it first. Section numbers below (§) refer to it. The sibling project `D:\Projects\stoneshard-save-editor` is the pattern for the solution layout, the publish target and the UI shell. Copy from it freely.

## Global Constraints

- **Owner's rule (verbatim intent, §10): never change any existing save file. Only create a copy of a character save, and modify that copy.**
  - Tests write only inside temp copies (`Fixtures.CopyToTemp()`).
  - The Lab writes only files listed in its copy registry, and only when the file's GUID still matches the registry.
  - Nothing ever writes to `remote\backup\` or `remotecache.vdf`.
  - The app's **Save changes** is the only code path that may replace a real character file. The builder never presses it on a real character. During development, the app is pointed at a Lab copy.
- **Platform:** Windows 11 x64.
- **Target frameworks:** `net10.0` for libraries and tests, `net10.0-windows` for `App`. Nullable enabled, `TreatWarningsAsErrors` true, `LangVersion` latest. `global.json` pins SDK `10.0.401` with `rollForward: latestFeature` and `"test": { "runner": "Microsoft.Testing.Platform" }`, as in Stoneshard.
- **NuGet packages allowed:**
  - `ZstdSharp.Port`, `System.IO.Hashing`, `Microsoft.AspNetCore.Components.WebView.WindowsForms` (10.0.120);
  - for tests: `xunit.v3` (4.0.2), `xunit.runner.visualstudio` (4.0.1), `Microsoft.NET.Test.Sdk` (18.11.0), `bunit` (2.12.5).
  - Any other dependency needs a one-line reason in its commit message.
- **Container facts (§6.2):**
  - magic `SV2C`, version 2, header size 48;
  - blocks contiguous from offset 48;
  - the section table starts at the next 16-byte boundary after the last block, padded with zero bytes;
  - 20-byte entries, then all names back-to-back, ending at EOF;
  - the table offset at 0x28 is relative to 0x28;
  - checksum = `(uint)(XxHash3.HashToUInt64(block) & 0xFFFFFFFF)` over the whole block (14-byte `S2BH` header plus body).
- **Supported data versions:** `character_data` 3, `player_character` 5, `player_knowledge` 100, `voiceover` 1, `fogofwar` 1. Any other version, any other `SV2C` version, or any checksum mismatch opens **read-only**.
- **Game facts used before the install reader exists:**
  - level cap **50** (1.0 patch notes);
  - skill points = `2 × (level − 1)` + rewards found − Σ `cost × rank` (§6.6).
- **App data root:** `%LOCALAPPDATA%\EnshroudedSaveEditor\`, holding `backups\`, `cache\`, `webview2\`, `lab\copies.json` and `settings.json`.
- **Process names:** the game is `enshrouded`; Steam is `steam`.
- **Real fixtures:** a snapshot taken 2026-10-25 lives in `tests/fixtures/real/` (git-ignored; never re-copy after Task 1).
  - `remote/` holds `character0.ksd`, `character1.ksd`, `character2.ksd`, `global.ksd` and `world2.ksd`.
  - `backup/` holds `character2.ksd.backup-0` … `-9`.
  - Tests that need them call `Assert.Skip` when they are missing.
- **Reference data:** from Kimikozi's repo at commit `1f9699717b5fbc1ea35df94abebadc0c2a83a9da`: `data/itemdb2.json` and `data/progress.json`, embedded in `GameData`. MIT notice in `THIRD_PARTY.md`.
  - **itemdb2 row layout** (from Kimikozi `ui/main.js`): `[name, catIndex, rarityIndex, maxStack, minLevel, maxLevel, slotIndex, internalName, perks, noRarity, durMod, weaponTypeIndex, description, named]`. The index lists are `cats`, `rars`, `slots` and `wcts` in the same file.
- **UI copy, exact:**
  - Product name "Enshrouded Save Editor"; wordmark "ENSHROUDED" over "SAVE EDITOR".
  - Tabs: "Character", "Inventory", "Skills".
  - Buttons: "Save changes", "Backups", "+ Add item", "Remove from backpack", "Add to backpack", "Close", "Refund all", "Undo".
  - Points switch: "Game rules" / "Unlimited". Chips: "1", "Half", "Max".
  - Placeholders: "Search all items…", "Find a skill…".
  - Errors: "No room in the backpack", "This save is read-only: {reason}", "Enshrouded is running. Close it to save."
- **Style tokens** (from the approved mockups):

  | Token | Value |
  |---|---|
  | `--bg` | `#0b0f14` |
  | `--panel` | `rgba(20,27,36,.92)` |
  | `--panel-2` | `#141b24` |
  | `--line` | `#2c3746` |
  | `--line-2` | `#3d4a5c` |
  | `--text` | `#e7e1d3` |
  | `--dim` | `#a9a594` |
  | `--muted` | `#6f7680` |
  | `--gold` | `#d8bf86` |
  | `--gold-2` | `#f1dca4` |
  | `--shroud` | `#b23a5c` |
  | rarity Common / Uncommon / Rare / Epic / Legendary | `#8c949e` / `#5bb35a` / `#4b8fe2` / `#a45ee8` / `#e8a33c` |
  | class Mage / Ranger / Warrior / none | `#86a8ff` / `#7fc76a` / `#e58a5a` / `#d8bf86` |

  - Headings use Cinzel and body text uses Source Sans 3. Both are bundled as woff2 in `UI/wwwroot/fonts/` (OFL), because the app must work offline.
  - Slot cell 54 px. Dark theme only. The body background uses the two radial glows from the mockups.
- **Git:** all work is on branch `feature/save-editor`. No push until the owner has tested (owner's standing rule).

## Review Focus

Failure modes the spec implies but no feature test exercises directly, most likely first. Each has a test in the task that owns the code.

1. **The character file changed on disk after it was opened** (the game saved, or Steam synced). Save refuses with `ChangedOnDisk` and offers a reload. It never overwrites. Tests: Task 5 `Save_refused_when_file_changed_on_disk`; Task 13 `Save_reports_changedOnDisk`.
2. **Enshrouded is running.** Save is blocked and the UI says why. Tests: Task 5 `Save_refused_while_game_running`; Task 14 `Editor_shows_game_running_banner_and_disables_save`.
3. **Damaged or unsupported file** (bad checksum, unknown data version, unrecognised `player_character` header). The picker lists it with a warning, it opens read-only, and it is never written. Tests: Task 1 `Checksum_mismatch_is_reported_not_thrown`; Task 4 `Damaged_file_is_listed_not_thrown`, `Unknown_data_version_is_unsupported`; Task 5 `ReadOnly_session_never_writes`.
4. **Invalid numbers and names:**
   - a stack of 0, above max, or negative; text in a number field;
   - level outside 1–50; negative XP;
   - a name that is empty, longer than 32 characters, or contains control characters.

   Each is rejected with a message, and nothing changes. Tests: Task 8 `SetName_rejects_invalid`, `SetLevel_rejects_out_of_range`; Task 10 `SetCount_rejects_invalid`; Task 14 `NumberField_rejects_non_numeric_input`.
5. **A Lab or test write aimed at a real save** (the owner's rule). It is refused before any byte is written. Tests: Task 6 `Lab_refuses_unregistered_target`, `Lab_refuses_when_guid_no_longer_matches`, `Copy_never_overwrites_an_existing_file`.

---

## File Structure

```
enshrouded-save-editor/
  EnshroudedSaveEditor.slnx  global.json  Directory.Build.props  .gitignore  THIRD_PARTY.md
  src/
    EnshroudedSaveEditor.Core/            save format + editing (no game-file access)
      Container/  KsdFile.cs  KsdSection.cs  KsdChecksum.cs  KsdFormatException.cs
      Records/    CharacterData.cs  PlayerKnowledge.cs  PlayerCharacter.cs  ItemRecord.cs  InventoryScanner.cs
      Model/      Items.cs  SkillGraph.cs                  (types GameData fills; Core never reads game files)
      Files/      SaveLocator.cs  SaveSession.cs  BackupService.cs  ProcessProbe.cs  CharacterCopier.cs  LabCopyRegistry.cs
      Editing/    EditRejectedException.cs  CharacterEditor.cs  SkillPoints.cs  SkillsEditor.cs
                  InventoryEditor.cs  InventoryLayout.cs  NewItemFields.cs
    EnshroudedSaveEditor.GameData/
      IGameData.cs
      Reference/  ReferenceGameData.cs  itemdb2.json  progress.json        (embedded resources)
      Install/    GameInstall.cs  SteamLibraries.cs
      Kfc/        (Task 19: names chosen by the implementer inside this folder)
      Icons/      IconService.cs  CategoryGlyphs.cs
    EnshroudedSaveEditor.UI/              Razor class library
      Services/   EditorService.cs  GameDataProvider.cs  Settings.cs  Views.cs  IAssetUrls.cs  ChangeLog.cs
      App.razor   Screens/ CharacterPicker.razor  Editor.razor
      Tabs/       CharacterTab.razor  InventoryTab.razor  SkillsTab.razor
      Components/ NumberField.razor  Stepper.razor  ItemSlot.razor  Banner.razor  BackupsDialog.razor
                  AddItemDialog.razor  SkillTree.razor
      wwwroot/    app.css  skilltree.js  fonts/
    EnshroudedSaveEditor.App/             WinForms window hosting the UI
      Program.cs  MainForm.cs  wwwroot/index.html
  tools/EnshroudedSaveEditor.Lab/         copies, dump, diff, watch, experiments
      Program.cs  Experiments.cs
  tests/
    EnshroudedSaveEditor.Core.Tests/      (TestSupport/Fixtures.cs, shared by link)
    EnshroudedSaveEditor.GameData.Tests/
    EnshroudedSaveEditor.Lab.Tests/
    EnshroudedSaveEditor.UI.Tests/        (bUnit)
    fixtures/real/                        git-ignored snapshot (Task 1)
    fixtures/experiments/                 git-ignored captures (Task 11)
  docs/research/  player-character.md  in-game-findings.md  kfc-format.md
```

---

## Phase 1: Save foundation

### Task 1: Scaffold, fixtures and the `SV2C` container (byte-exact round trip)

**Files:**
- Create:
  - `global.json`, `Directory.Build.props`, `EnshroudedSaveEditor.slnx`, `THIRD_PARTY.md`
  - `src/EnshroudedSaveEditor.Core/EnshroudedSaveEditor.Core.csproj`
  - `src/EnshroudedSaveEditor.Core/Container/KsdFile.cs`, `KsdSection.cs`, `KsdChecksum.cs`, `KsdFormatException.cs`
  - `tests/EnshroudedSaveEditor.Core.Tests/EnshroudedSaveEditor.Core.Tests.csproj`
  - `tests/EnshroudedSaveEditor.Core.Tests/TestSupport/Fixtures.cs`
  - `tests/EnshroudedSaveEditor.Core.Tests/Container/KsdFileTests.cs`
- Modify: `.gitignore` (it already lists `tests/fixtures/real/`, `tests/fixtures/experiments/`, `bin/`, `obj/`, `.superpowers/`)

**Interfaces:**
- Produces (namespace `EnshroudedSaveEditor.Core.Container`):
  - `static class KsdChecksum`: `uint Compute(ReadOnlySpan<byte> block)`.
  - `sealed class KsdSection`:
    - `string Name`, `byte TypeId`, `bool Compressed`, `uint BlockVersion`, `uint DataVersion`;
    - `bool ChecksumValid`, `ReadOnlyMemory<byte> Block`;
    - `byte[] GetData()`: decompressed, or the raw body when stored;
    - `void SetData(byte[] data)`: rebuilds the block with the same header fields and compression mode, and recomputes the checksum.
  - `sealed class KsdFile`:
    - `ushort Version`, `byte[] Guid` (16 bytes), `uint Revision`, `DateTimeOffset SavedAt` (u32 low + u32 high Unix seconds at offset 32);
    - `IReadOnlyList<KsdSection> Sections`, `KsdSection this[string name]`, `KsdSection? Find(string name)`;
    - `static KsdFile Parse(byte[] file)`: throws `KsdFormatException` on structural errors; a checksum mismatch only sets `ChecksumValid = false`;
    - `byte[] ToBytes()`.
  - `sealed class KsdFormatException : Exception`.
- Produces (tests, namespace `EnshroudedSaveEditor.Tests`), `static class Fixtures`:
  - `string RepoRoot`;
  - `string RealRemote` and `string RealBackup`: both skip if missing;
  - `string CopyToTemp()`: copies `RealRemote` to a fresh temp folder and returns it;
  - `byte[] Read(string fileName)`: from `RealRemote`;
  - `byte[] PatchBlock(byte[] file, string section, Action<byte[]> patch)`: patches that section's raw block (header included), then rewrites its table checksum. Used to fake unknown versions;
  - `IReadOnlySet<uint> ItemIds()`: the ids in `tests/fixtures/real/itemdb2.json` (added in Task 3).
  - Later test projects include this file with `<Compile Include="..\EnshroudedSaveEditor.Core.Tests\TestSupport\Fixtures.cs" Link="TestSupport\Fixtures.cs" />`.

- [ ] **Step 1: Branching and solution.**
  - Merge `feature/design` into `main` (the owner approved the spec and this plan). Branch `feature/save-editor` from `main`.
  - Scaffold `EnshroudedSaveEditor.slnx`, the Core classlib and an xunit.v3 test project. Copy `global.json` and `Directory.Build.props` from Stoneshard.
  - Add `ZstdSharp.Port` and `System.IO.Hashing` to Core.

- [ ] **Step 2: Snapshot the fixtures (read-only copy, never a write to the source).**

  ```powershell
  $r = "C:\Program Files (x86)\Steam\userdata\43900557\1203620\remote"
  New-Item -ItemType Directory -Force tests\fixtures\real\remote, tests\fixtures\real\backup | Out-Null
  Copy-Item "$r\character0.ksd","$r\character1.ksd","$r\character2.ksd","$r\global.ksd","$r\world2.ksd" tests\fixtures\real\remote\
  Copy-Item "$r\backup\character2.ksd.backup-*" tests\fixtures\real\backup\
  ```

  Expected: 5 files plus 10 backups. `git status` shows none of them.

- [ ] **Step 3: Write the failing tests**

```csharp
[Fact] public void Parses_character2_header_and_sections() {
    var f = KsdFile.Parse(Fixtures.Read("character2.ksd"));
    Assert.Equal(2, f.Version); Assert.Equal(991u, f.Revision);
    Assert.Equal(1792490571, f.SavedAt.ToUnixTimeSeconds());
    Assert.Equal(new[] { "character_data", "player_character", "voiceover", "player_knowledge", "fogofwar" }, f.Sections.Select(s => s.Name));
    Assert.All(f.Sections, s => Assert.True(s.ChecksumValid));
    Assert.False(f["voiceover"].Compressed); Assert.Equal(5u, f["player_character"].DataVersion);
    Assert.Equal(706, f["player_character"].GetData().Length); Assert.Equal(1_638_413, f["fogofwar"].GetData().Length);
}
[Theory, InlineData("character0.ksd"), InlineData("character1.ksd"), InlineData("character2.ksd"), InlineData("global.ksd"), InlineData("world2.ksd")]
public void Unchanged_file_round_trips_byte_for_byte(string name) {
    var bytes = Fixtures.Read(name); Assert.Equal(bytes, KsdFile.Parse(bytes).ToBytes());
}
[Fact] public void SetData_recompresses_and_keeps_other_blocks_identical() {
    // character2: data = player_character.GetData(); data[58] = 8 (level 7 → 8); SetData(data)
    // reparse ToBytes(): player_character.GetData()[58] == 8, ChecksumValid; every other section's Block bytes identical to the original;
    // table offset (0x28 + u32 at 0x28) % 16 == 0; u32 at 8 == file length
}
[Fact] public void Checksum_mismatch_is_reported_not_thrown() { /* flip one body byte of fogofwar → Parse ok, fogofwar.ChecksumValid false, others true */ }
[Fact] public void Parse_rejects_garbage() { /* wrong magic; header size ≠ 48; size field ≠ length; table offset past EOF; count > 4096; entry pointing past EOF → KsdFormatException */ }
```

- [ ] **Step 4: Run** `dotnet test --project tests/EnshroudedSaveEditor.Core.Tests`. Expected: FAIL to compile.

- [ ] **Step 5: Implement the container.**
  - `ToBytes`: header, then blocks in their original order, then zero padding to 16, then entries, then names. Rewrite the file size at 8, the table offset at 0x28 and the count at 0x2C.
  - Name offsets are relative to each entry.
  - `SetData` with compression uses `ZstdSharp.Compressor` at level 3.

- [ ] **Step 6: Run tests.** Expected: all PASS, including 5 byte-exact round trips.

- [ ] **Step 7: Commit** `feat(core): SV2C container with byte-exact round trip`.

### Task 2: `character_data`, `player_knowledge`, and the `player_character` header and skills

**Files:**
- Create: `src/EnshroudedSaveEditor.Core/Records/CharacterData.cs`, `PlayerKnowledge.cs`, `PlayerCharacter.cs`
- Test: `tests/EnshroudedSaveEditor.Core.Tests/Records/CharacterDataTests.cs`, `PlayerKnowledgeTests.cs`, `PlayerCharacterHeaderTests.cs`

**Interfaces** (namespace `EnshroudedSaveEditor.Core.Records`):
- Consumes: `KsdFile` (Task 1).
- Produces:
  - `sealed class CharacterData`:
    - `byte SlotIndex` (byte 0; equals N in `characterN.ksd`, verified 0/1/2);
    - `string Name`;
    - `byte[] Guid`: the 16 bytes right after the name, equal to the header GUID;
    - `static CharacterData Parse(byte[] data)`: requires the signature `93 7B 40 B6` at bytes 1–4 and a name length ≤ 256; otherwise `KsdFormatException`;
    - `byte[] ToBytes()`;
    - `CharacterData With(string? name = null, byte? slotIndex = null, byte[]? guid = null)`.
  - `sealed class PlayerKnowledge`:
    - `uint Version`, `IReadOnlyList<(uint Id, uint Value)> Entries`, `bool Contains(uint id)`;
    - `static PlayerKnowledge Parse(byte[] data)`: requires `8 + 8n == length`;
    - `byte[] ToBytes()`.
  - `readonly record struct SkillEntry(uint NodeId, byte Rank)`.
  - `sealed class PlayerCharacter`:
    - `const int LevelOffset = 58`;
    - `uint Level`, `uint Xp`, `List<SkillEntry> Skills`;
    - `static PlayerCharacter Parse(byte[] data, Func<uint, bool> isItem)`.
      - It throws `UnsupportedLayoutException` (a `KsdFormatException` subclass) unless: the five f32s at 38, 42, 46, 50 and 54 are each finite and in [0, 50]; the level is 1–200; the skill count is ≤ 400 and every rank is ≤ 15; and the status count after the skills is ≤ 64.
      - Character0's fifth float is 0.0, so Kimikozi's lower bound of 0.05 would wrongly reject it.
      - The inventory part arrives in Task 3. Until then, everything after the skills is kept as one opaque tail.
    - `byte[] ToBytes()`.

- [ ] **Step 1: Write failing tests**

```csharp
[Theory, InlineData("character0.ksd", 0), InlineData("character1.ksd", 1), InlineData("character2.ksd", 2)]
public void CharacterData_reads_name_slot_and_guid(string file, int slot) {
    var k = KsdFile.Parse(Fixtures.Read(file)); var cd = CharacterData.Parse(k["character_data"].GetData());
    Assert.Equal("Amadis", cd.Name); Assert.Equal(slot, cd.SlotIndex); Assert.Equal(k.Guid, cd.Guid);
    Assert.Equal(k["character_data"].GetData(), cd.ToBytes());
}
[Fact] public void CharacterData_rename_changes_length() { /* With(name:"Amadis the Bold") → reparse Name, Guid unchanged, length +9 */ }
[Theory, InlineData("character0.ksd", 1094), InlineData("character1.ksd", 1998)]
public void Knowledge_parses_and_round_trips(string file, int count) { /* Entries.Count == count; ToBytes equals input */ }
[Theory, InlineData("character0.ksd", 12u, 2913u, 0), InlineData("character1.ksd", 18u, 5802u, 0), InlineData("character2.ksd", 7u, 6500u, 30)]
public void Header_level_xp_skills(string file, uint level, uint xp, int skills) { /* plus ToBytes == input for each */ }
[Fact] public void Character2_has_15_rank0_entries() { /* Skills.Count(s => s.Rank == 0) == 15; Count(s => s.Rank >= 1) == 15 */ }
[Fact] public void Skill_list_can_shrink_and_grow() { /* remove 3 entries, add 1 → reparse equals edited list; level/xp intact */ }
[Fact] public void Unrecognised_header_is_rejected() { /* data[38..42] = f32 1e9 → UnsupportedLayoutException */ }
```

- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement** the three readers. In `PlayerCharacter`, keep bytes 0–57 as an opaque prefix.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(core): character name, knowledge and player header/skills`.

### Task 3: Inventory records inside `player_character`

**Files:**
- Create: `src/EnshroudedSaveEditor.Core/Records/ItemRecord.cs`, `InventoryScanner.cs`, `docs/research/player-character.md`
- Modify: `src/EnshroudedSaveEditor.Core/Records/PlayerCharacter.cs`
- Test: `tests/EnshroudedSaveEditor.Core.Tests/Records/InventoryTests.cs`

**Interfaces:**
- Consumes: Task 2.
- Produces:
  - `enum ContainerKind { Equipped, Inventory, Extra }`
  - `enum InventoryStatus { Full, InPlaceOnly, NotFound }`:
    - `Full`: the containers and their count bytes were recognised, so add, move and remove are allowed;
    - `InPlaceOnly`: records were located but the inventory header wasn't, so only same-size edits are allowed;
    - `NotFound`: no inventory edits.
  - `sealed class ItemRecord`:
    - `byte Slot`, `uint ItemId`, `ushort Count`, `byte Flags`;
    - optional fields: `byte? Level` (bit 0); `(uint Current, uint Max)? Durability` (bit 1); `byte[]? Bit2` (8 bytes); `byte[]? Transmog` (bit 3, 4 bytes); `(byte Rarity, byte Extra)? Rarity` (bit 4); `(uint GemId, byte Marker)? Gem` (bit 5);
    - `bool IsGhost` = `Count == 0 && Flags == 0`;
    - `int EncodedLength`, `void WriteTo(Span<byte>)`;
    - `static ItemRecord? TryRead(ReadOnlySpan<byte> data, int at, Func<uint, bool> isItem)`.
  - `sealed record ItemContainer(ContainerKind Kind, List<ItemRecord> Records)`
  - On `PlayerCharacter`:
    - `InventoryStatus InventoryStatus`;
    - `IReadOnlyList<ItemContainer> Containers` (empty when `NotFound`);
    - `ItemContainer? Container(ContainerKind k)`.
    - `ToBytes()` re-encodes records in place. Every byte that isn't a located record or count byte is preserved as an opaque segment.

- [ ] **Step 1: Research and record the layout.**
  - Download the reference data, git-ignored, from the pinned commit (Global Constraints) into `tests/fixtures/real/`: `itemdb2.json` (669,917 bytes) and `progress.json` (70,018 bytes). `Fixtures.ItemIds()` reads the first.
  - Port Kimikozi's `parsePlayer` approach (`lib/pc.js`, MIT, credit it in `THIRD_PARTY.md`):
    - equipped records start at slot 0;
    - the inventory header ends `[type][n2][type][n3]`;
    - then come n2 Inventory records and n3 Extra records;
    - field sizes by bit: {0:1, 1:8, 2:8, 3:4, 4:2, 5:5}.
  - Then study the status-effect list using all fixtures, including the 10 backups. Character1's two status entries aren't 27 bytes each, and its equipped run starts at slot 5, not 0. Aim for a structural rule that finds the containers without scanning.
  - Write `docs/research/player-character.md` with the layout as understood, what's still a guess, and per-fixture results.

- [ ] **Step 2: Write failing tests** (ids are little-endian `uint`)

```csharp
[Fact] public void Character2_inventory_is_fully_recognised() {
    var pc = Load("character2.ksd");
    Assert.Equal(InventoryStatus.Full, pc.InventoryStatus);
    var eq = pc.Container(ContainerKind.Equipped)!.Records;
    Assert.Equal(11, eq.Count); Assert.Equal((0, 0xe5a9ac59u, (byte)0x11), (eq[0].Slot, eq[0].ItemId, eq[0].Flags)); Assert.Equal(0xde38058eu, eq[10].ItemId);
    var inv = pc.Container(ContainerKind.Inventory)!.Records;
    Assert.Equal(21, inv.Count); Assert.Equal(15, inv[0].Slot); Assert.True(inv[0].IsGhost);          // Bronze Sword ×0
    Assert.Contains(inv, r => r.Slot == 30 && r.ItemId == 0xe988b200u && r.Count == 87);              // Fireball I ×87
    Assert.Contains(inv, r => r.ItemId == 0xe589d7a4u && r.Flags == 0x33 && r.Gem is not null);        // Scorching Wand
    Assert.Empty(pc.Container(ContainerKind.Extra)!.Records);
}
[Fact] public void Character0_equipped_has_16_records() { /* status ≥ InPlaceOnly; Equipped count 16; slot 15 = 0xfc3d5c4c (Small Backpack) */ }
[Fact] public void Character1_copper_arrows_are_found() { /* status ≥ InPlaceOnly; some record ItemId 0xf12faa48 Count 88 */ }
[Theory, MemberData(nameof(AllPlayerCharacters))] // 3 fixtures + 10 backups
public void Every_player_character_round_trips(byte[] data) { /* Parse(isItem).ToBytes() == data */ }
[Fact] public void Count_edit_changes_only_two_bytes() { /* character2 Fireball 87 → 500: ToBytes differs from input in exactly the 2 count bytes */ }
```

  `isItem` in these tests is `Fixtures.ItemIds().Contains`.

- [ ] **Step 3: Run.** Expected: FAIL.
- [ ] **Step 4: Implement** `ItemRecord`, `InventoryScanner` and the `PlayerCharacter` wiring. If Step 1 found a structural rule that makes character0 or character1 `Full`, raise those tests to `Full` and say so in the research note.
- [ ] **Step 5: Run.** Expected: PASS.
- [ ] **Step 6: Commit** `feat(core): inventory records in player_character`.

### Task 4: Find characters

**Files:**
- Create: `src/EnshroudedSaveEditor.Core/Files/SaveLocator.cs`
- Test: `tests/EnshroudedSaveEditor.Core.Tests/Files/SaveLocatorTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces (namespace `EnshroudedSaveEditor.Core.Files`):
  - `enum CharacterStatus { Ok, Damaged, Unsupported }`
  - `sealed record CharacterInfo(string Path, string FileName, int SlotIndex, string Name, int Level, uint Xp, uint Revision, DateTimeOffset SavedAt, CharacterStatus Status, string? StatusMessage)`
  - `static class SupportedVersions`: `ushort Container = 2`, and `IReadOnlyDictionary<string, uint> Sections` with the five values from Global Constraints.
  - `sealed class SaveLocator`:
    - `static IReadOnlyList<string> DefaultRoots()`: every existing `<Steam>\userdata\*\1203620\remote` (Steam from the registry key `HKCU\Software\Valve\Steam\SteamPath`, falling back to `C:\Program Files (x86)\Steam`), plus `%USERPROFILE%\Saved Games\Enshrouded` if it holds `character*.ksd`;
    - `IReadOnlyList<CharacterInfo> Scan(string root)`: files matching `^character(\d+)\.ksd$`, newest `SavedAt` first.

- [ ] **Step 1: Write failing tests** (on `Fixtures.CopyToTemp()`)

```csharp
[Fact] public void Scan_lists_three_amadis_newest_first() {
    var list = new SaveLocator().Scan(tmp);
    Assert.Equal(new[] { "character2.ksd", "character1.ksd", "character0.ksd" }, list.Select(c => c.FileName));
    Assert.All(list, c => { Assert.Equal("Amadis", c.Name); Assert.Equal(CharacterStatus.Ok, c.Status); });
    Assert.Equal(new[] { 7, 18, 12 }, list.Select(c => c.Level));
}
[Fact] public void Damaged_file_is_listed_not_thrown() { /* junk bytes in character0.ksd → Damaged with message; others Ok */ }
[Fact] public void Unknown_data_version_is_unsupported() { /* Fixtures.PatchBlock(character1, "player_character", b => b[10] = 6) → Unsupported */ }
[Fact] public void Checksum_mismatch_is_damaged() { /* flip a fogofwar body byte → Damaged, message mentions checksum */ }
[Fact] public void Ignores_world_and_global_files() => /* only character*.ksd listed */;
[Fact] public void Missing_root_returns_empty() => Assert.Empty(new SaveLocator().Scan(@"C:\does\not\exist"));
```

- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
  - Listing parses `player_character` with `isItem = _ => false`, because only the header is needed.
  - A file whose `player_character` throws `UnsupportedLayoutException` is `Unsupported`, not `Damaged`.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(core): locate character saves`.

### Task 5: Save session: open, save with backup, guards

**Files:**
- Create: `src/EnshroudedSaveEditor.Core/Files/SaveSession.cs`, `BackupService.cs`, `ProcessProbe.cs`
- Test: `tests/EnshroudedSaveEditor.Core.Tests/Files/SaveSessionTests.cs`

**Interfaces:**
- Consumes: Tasks 1–4.
- Produces:
  - `interface IProcessProbe { bool IsGameRunning(); bool IsSteamRunning(); }` and `sealed class ProcessProbe : IProcessProbe`, using the process names from Global Constraints.
  - `sealed record BackupInfo(string Path, DateTimeOffset Created)`
  - `sealed class BackupService(string backupRoot)`:
    - `static string DefaultRoot` = `%LOCALAPPDATA%\EnshroudedSaveEditor\backups`;
    - `string Backup(string characterPath)`: copies to `<root>\<fileStem>\yyyyMMdd-HHmmss-fff.ksd`;
    - `IReadOnlyList<BackupInfo> List(string characterPath)`: newest first;
    - `void Restore(BackupInfo b, string characterPath)`: backs up the current file first.
  - `enum SaveResult { Saved, GameRunning, ChangedOnDisk, ReadOnly, VerifyFailed }`
  - `sealed class SaveSession`:
    - `static SaveSession Open(string path, Func<uint, bool> isItem)`;
    - `string Path`, `KsdFile File`, `CharacterData Character`, `PlayerCharacter Player`, `PlayerKnowledge Knowledge`;
    - `bool ReadOnly`, `string? ReadOnlyReason`;
    - `int ChangeCount`, `void MarkChanged()`;
    - `SaveResult Save(BackupService backups, IProcessProbe probe)`.

- [ ] **Step 1: Write failing tests** (temp copy, `FakeProbe(game, steam)`)

```csharp
[Fact] public void Open_reads_records_and_starts_clean() { /* character2: Character.Name "Amadis", Player.Level 7, ChangeCount 0, ReadOnly false */ }
[Fact] public void Save_writes_valid_file_with_backup_and_new_revision() {
    // Player.Level = 8; MarkChanged(); Save → Saved
    // reparse: all ChecksumValid; level 8; Revision 992; SavedAt within 5 s of now; one backup whose bytes equal the original file
}
[Fact] public void Save_refused_while_game_running() { /* FakeProbe(game:true) → GameRunning; bytes unchanged; no backup */ }
[Fact] public void Save_refused_when_file_changed_on_disk() { /* after Open, write another valid file over it → ChangedOnDisk; that file untouched */ }
[Fact] public void ReadOnly_session_never_writes() { /* Unsupported file opens ReadOnly with reason; Save → ReadOnly; bytes unchanged */ }
[Fact] public void Unchanged_save_keeps_every_section_block() { /* MarkChanged only; Save; every section's Block identical to the original; only Revision (+1) and SavedAt differ */ }
[Fact] public void Restore_brings_back_backup_and_backs_up_current_first() { }
```

- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
  - **Change detection:** SHA-256 of the file at open, checked just before writing.
  - **Edited records** are written back through `SetData` only for sections whose `ToBytes()` differs from the original data.
  - **Header on save:** `Revision + 1`, and `SavedAt` set to now.
  - **Atomic write:** `<file>.tmp` in the same folder, then `File.Replace`.
  - **Verify:** read the file back, parse it, and compare every section's data with what was intended. On a mismatch, restore the backup and return `VerifyFailed`.
  - `IsSteamRunning` isn't used here yet. Task 12 decides whether it blocks.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(core): save session with backups and guards`.

### Task 6: Character copies and the Lab tool

**Files:**
- Create:
  - `src/EnshroudedSaveEditor.Core/Files/CharacterCopier.cs`, `LabCopyRegistry.cs`
  - `tools/EnshroudedSaveEditor.Lab/EnshroudedSaveEditor.Lab.csproj`, `Program.cs`
  - `tests/EnshroudedSaveEditor.Lab.Tests/…` (project and `CopyTests.cs`)

**Interfaces:**
- Consumes: Tasks 1–5.
- Produces:
  - `sealed class LabCopyRegistry(string registryPath)`:
    - `static string DefaultPath` = `%LOCALAPPDATA%\EnshroudedSaveEditor\lab\copies.json`;
    - `void Register(string path, byte[] guid)`;
    - `bool IsWritableCopy(string path)`: true only when the path is registered **and** the file's header GUID still equals the registered one;
    - `void EnsureWritable(string path)`: throws `InvalidOperationException("Refusing to write {path}: not a Lab copy")`.
  - `static class CharacterCopier`:
    - `string Copy(string remoteDir, string fromFile, string newName, LabCopyRegistry registry)`.
    - It writes `character{max+1}.ksd`: same sections, a new random GUID in the header and in `character_data`, slot byte = N, name = `newName`.
    - It uses `FileMode.CreateNew` and registers the copy. It returns the new path.
  - **Lab CLI** (plain `args`, no packages; every write goes through `EnsureWritable`):
    - `copy --remote <dir> --from character1.ksd --name "Test Items"`
    - `dump --file <ksd> [--section player_character]`: read-only; prints the header, sections, records, skills and points.
    - `diff --a <ksd> --b <ksd>`: read-only; prints changed sections, then the level, XP, skill and record differences.
    - `watch --remote <dir> --out tests/fixtures/experiments`: copies each registered copy to `<out>/<stem>/<yyyyMMdd-HHmmss>.ksd` when it changes.
    - `list-copies`

- [ ] **Step 1: Write failing tests** (temp copy of the fixtures, temp registry)

```csharp
[Fact] public void Copy_creates_next_file_with_new_identity() {
    var path = CharacterCopier.Copy(tmp, "character1.ksd", "Test Items", reg);
    Assert.Equal("character3.ksd", Path.GetFileName(path));
    var src = KsdFile.Parse(File.ReadAllBytes(Path.Combine(tmp, "character1.ksd"))); var cp = KsdFile.Parse(File.ReadAllBytes(path));
    var cd = CharacterData.Parse(cp["character_data"].GetData());
    Assert.Equal("Test Items", cd.Name); Assert.Equal(3, cd.SlotIndex); Assert.Equal(cp.Guid, cd.Guid); Assert.NotEqual(src.Guid, cp.Guid);
    foreach (var n in new[] { "player_character", "player_knowledge", "fogofwar" }) Assert.Equal(src[n].Block.ToArray(), cp[n].Block.ToArray());
    // every source file in tmp byte-identical to before (hash all 5 before/after)
}
[Fact] public void Copy_never_overwrites_an_existing_file() { /* pre-create character3.ksd (random bytes) → Copy writes character4.ksd; character3.ksd bytes unchanged */ }
[Fact] public void Lab_refuses_unregistered_target() { /* EnsureWritable(tmp/character2.ksd) throws; file unchanged */ }
[Fact] public void Lab_refuses_when_guid_no_longer_matches() { /* register copy, then overwrite it with character0 bytes → IsWritableCopy false */ }
```

- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement** the copier, the registry and the CLI.
- [ ] **Step 4: Run.** Expected: PASS. Then run `dotnet run --project tools/EnshroudedSaveEditor.Lab -- diff --a tests/fixtures/real/backup/character2.ksd.backup-9 --b tests/fixtures/real/remote/character2.ksd`. Expected: a non-empty diff, and no write happened.
- [ ] **Step 5: Commit** `feat(lab): character copies, registry guard, dump/diff/watch`.

---

## Phase 2: Game data (reference first)

### Task 7: Game data model and embedded reference data

**Files:**
- Create:
  - `src/EnshroudedSaveEditor.Core/Model/Items.cs`, `SkillGraph.cs`
  - `src/EnshroudedSaveEditor.GameData/EnshroudedSaveEditor.GameData.csproj` (references Core; embeds `Reference/*.json`)
  - `src/EnshroudedSaveEditor.GameData/IGameData.cs`, `Reference/ReferenceGameData.cs`, `Reference/itemdb2.json`, `Reference/progress.json`
  - `tests/EnshroudedSaveEditor.GameData.Tests/ReferenceGameDataTests.cs`
- Modify: `THIRD_PARTY.md` (Kimikozi MIT notice; the data belongs to Keen Games)

**Interfaces:**
- Produces (Core, namespace `EnshroudedSaveEditor.Core.Model`):
  - `enum ItemCategory`: the 16 `cats` in file order (Ammunition … Weapons).
  - `enum Rarity : byte { Common = 1, Uncommon, Rare, Epic, Legendary }`: the save values.
  - `enum EquipSlot { None, Arms, Boots, Chest, Helmet, Legs, Ring, Shield, Weapon }`: the `slots` order.
  - `sealed record ItemInfo(uint Id, string Name, ItemCategory Category, Rarity BaseRarity, int MaxStack, int MinLevel, int MaxLevel, EquipSlot Slot, string? WeaponType, string InternalName, string Description, int Perks, bool NoRarity)`
  - `interface IItemCatalog { ItemInfo? Find(uint id); IReadOnlyList<ItemInfo> All { get; } }`
  - `enum SkillNodeType { Spark, Attribute, Ability, Passive }`: `R`, `T`, `A`, `P`.
  - `enum SkillClass { None, Mage, Ranger, Warrior }`: `N`, `M`, `R`, `W`.
  - `sealed record SkillNode(uint Id, string Name, string Description, SkillNodeType Type, SkillClass Class, int Cost, int MaxRank, double X, double Y, IReadOnlyList<string> RankValues)`. `MaxRank` is the data's `max`, with 0 mapped to 1.
  - `sealed record ArchetypeLabel(string Name, SkillClass Class, double X, double Y)`
  - `sealed record SkillReward(uint Id, int Points, string Region, string Name)`
  - `sealed class SkillGraph(IReadOnlyList<SkillNode> nodes, IReadOnlyList<(uint A, uint B)> links, IReadOnlyList<ArchetypeLabel> labels)`. Links are node ids; the loader converts `progress.json`'s index pairs.
    - `SkillNode? Find(uint id)`;
    - `IReadOnlyList<SkillNode> Neighbors(uint id)`;
    - `IEnumerable<SkillNode> Sparks`;
    - `Nodes`, `Links`, `Labels`.
- Produces (GameData):
  - `interface IGameData { IItemCatalog Items { get; } SkillGraph Tree { get; } IReadOnlyDictionary<uint, SkillReward> Rewards { get; } int LevelCap { get; } IReadOnlyList<uint>? XpPerLevel { get; } string Source { get; } }`. `XpPerLevel[i]` is the XP needed to go from level i+1 to i+2; it is null when unknown.
  - `sealed class ReferenceGameData : IGameData`: `static ReferenceGameData Load()`, with `LevelCap = 50`, `XpPerLevel = null`, and `Source = "Reference data (Kimikozi, build of 2026-10-15)"`.
- From Task 8 on, `EnshroudedSaveEditor.Core.Tests` also references GameData (test-only), so the editor tests can use `ReferenceGameData.Load()`.

- [ ] **Step 1: Copy the two files** that Task 3 downloaded (`tests/fixtures/real/itemdb2.json`, `progress.json`) into `src/EnshroudedSaveEditor.GameData/Reference/`. These copies are committed and credited.

- [ ] **Step 2: Write failing tests**

```csharp
[Fact] public void Items_load_with_categories_and_limits() {
    var g = ReferenceGameData.Load();
    Assert.Equal(3526, g.Items.All.Count);
    var arrow = g.Items.Find(0xf12faa48)!; Assert.Equal("Copper Arrow", arrow.Name); Assert.Equal(ItemCategory.Ammunition, arrow.Category); Assert.Equal(500, arrow.MaxStack);
    Assert.Equal(250, g.Items.Find(0x2b9d5b6f)!.MaxStack);                                   // Copper Ore
    var bow = g.Items.Find(0x29433573)!; Assert.Equal((EquipSlot.Weapon, "RangedBow", 1, 50), (bow.Slot, bow.WeaponType, bow.MinLevel, bow.MaxLevel));
    Assert.Equal(EquipSlot.Ring, g.Items.Find(0xf29b7aa9)!.Slot);                            // Ring of Health
}
[Fact] public void Tree_loads_nodes_links_labels_sparks() {
    var t = ReferenceGameData.Load().Tree;
    Assert.Equal(243, t.Nodes.Count); Assert.Equal(312, t.Links.Count); Assert.Equal(12, t.Labels.Count); Assert.Equal(12, t.Sparks.Count());
    Assert.Equal(65, t.Nodes.Count(n => n.Type == SkillNodeType.Attribute)); Assert.Equal(43, t.Nodes.Count(n => n.Type == SkillNodeType.Ability));
    Assert.Contains(t.Labels, l => l.Name == "WIZARD" && l.Class == SkillClass.Mage);
}
[Theory, InlineData("character0.ksd", 18), InlineData("character1.ksd", 35), InlineData("character2.ksd", 10)]
public void Rewards_found_in_knowledge_sum_as_observed(string file, int points) { }
[Fact] public void Every_saved_skill_and_item_id_is_known() { /* all 3 fixtures: every SkillEntry.NodeId in Tree; every located ItemRecord.ItemId in Items */ }
```

- [ ] **Step 3: Run.** Expected: FAIL.
- [ ] **Step 4: Implement.** Parse with `System.Text.Json`.
- [ ] **Step 5: Run.** Expected: PASS.
- [ ] **Step 6: Commit** `feat(gamedata): game data model and embedded reference data`.

---

## Phase 3: Editing

### Task 8: Character edits and skill points

**Files:**
- Create: `src/EnshroudedSaveEditor.Core/Editing/EditRejectedException.cs`, `CharacterEditor.cs`, `SkillPoints.cs`
- Test: `tests/EnshroudedSaveEditor.Core.Tests/Editing/CharacterEditorTests.cs`, `SkillPointsTests.cs`

**Interfaces:**
- Consumes: `SaveSession`, `SkillGraph`, `SkillReward`, `PlayerKnowledge`.
- Produces (namespace `EnshroudedSaveEditor.Core.Editing`):
  - `sealed class EditRejectedException(string message) : Exception(message)`
  - `sealed record PointsSummary(int FromLevels, int FromRewards, int Spent, int Available)`
  - `static class SkillPoints`: `PointsSummary Compute(uint level, IEnumerable<SkillEntry> skills, SkillGraph tree, PlayerKnowledge knowledge, IReadOnlyDictionary<uint, SkillReward> rewards)`. Unknown node ids cost 0.
  - `sealed class CharacterEditor(SaveSession s)`:
    - `void SetName(string name)`: trimmed, 1–32 characters, no control characters;
    - `void SetLevel(int level, int cap)`: 1..cap;
    - `void SetXp(long xp)`: 0..`uint.MaxValue`.
    - Each call is one `MarkChanged()`. A no-op is rejected ("Already {value}").

- [ ] **Step 1: Write failing tests**

```csharp
[Theory, InlineData("character0.ksd", 22, 18, 0, 40), InlineData("character1.ksd", 34, 35, 0, 69), InlineData("character2.ksd", 12, 10, 22, 0)]
public void Points_match_the_saves(string f, int lv, int rw, int sp, int av) { }
[Fact] public void SetName_rejects_invalid() { /* "", "   ", 33 chars, "a\u0001b" → EditRejectedException; nothing changed (ChangeCount 0) */ }
[Fact] public void SetName_writes_new_name() { /* "Amadis the Bold" → Character.Name; Save → reparse */ }
[Fact] public void SetLevel_rejects_out_of_range() { /* 0, 51 with cap 50 → rejected */ }
[Fact] public void Raising_level_raises_available_points() { /* character2 level 7→10: Available 0 → 6 */ }
```

- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(core): character edits and skill points`.

### Task 9: Skills editing with paths and points

**Files:**
- Create: `src/EnshroudedSaveEditor.Core/Editing/SkillsEditor.cs`
- Test: `tests/EnshroudedSaveEditor.Core.Tests/Editing/SkillsEditorTests.cs` (uses `ReferenceGameData.Load().Tree`)

**Interfaces:**
- Consumes: Tasks 2, 7, 8.
- Produces:
  - `enum PointsMode { GameRules, Unlimited }`
  - `enum NodeState { Learned, Available, Locked }`
  - `sealed class SkillsEditor(SaveSession s, SkillGraph tree, IReadOnlyDictionary<uint, SkillReward> rewards)`:
    - `IReadOnlySet<uint> Learned`: entries with rank ≥ 1;
    - `int RankOf(uint id)`;
    - `NodeState StateOf(uint id)`: Sparks are always `Learned`;
    - `PointsSummary Points()`;
    - `void Learn(uint id, PointsMode mode)`: learns at rank 1 or ranks up;
    - `IReadOnlyList<uint> DependentsOf(uint id)`;
    - `void Unlearn(uint id)`: removes `id` and every dependent;
    - `void RefundAll()`.
  - **Rules** (*my calls where marked, from spec §8.6*):
    - A node is connected when it links to a Spark or to a learned node. Learn requires that in **both** modes.
    - `GameRules` also requires `Available ≥ cost`. Rejections: "Connect it to a learned skill first", "Not enough skill points", "Already at max rank", "Sparks are always active".
    - **Learn** on a node whose entry exists with rank 0 sets that entry's rank to 1 in place. Otherwise it appends `(id, 1)`.
    - **Dependents:** the learned nodes that lose every path to a Spark once `id` is removed, found by BFS from the Sparks over learned nodes (`Kimikozi main.js` 350–354 uses the same reachability).
    - **Unlearn and RefundAll** remove entries entirely. Rank-0 entries are never touched unless that node is learned again. An empty list is a valid state (§6.4).
    - Each public edit is one `MarkChanged()`.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Character2_has_15_learned_and_no_points() { /* Learned.Count 15; Points().Available 0 */ }
[Fact] public void Learn_requires_a_connection_in_both_modes() { /* pick a Locked node with no learned/Spark neighbour → rejected in GameRules and Unlimited */ }
[Fact] public void GameRules_blocks_overspend_Unlimited_allows_it() { /* character2: Available node with cost ≥1 → GameRules rejected "Not enough skill points"; Unlimited ok, Available becomes negative */ }
[Fact] public void Learning_a_rank0_entry_sets_rank_in_place() { /* character2: first rank-0 entry whose node is connected; Learn(Unlimited) → same list index, rank 1; Skills.Count still 30 */ }
[Fact] public void Rank_up_until_max() { /* MaxRank 3 node: Learn×3 ok, 4th rejected "Already at max rank" */ }
[Fact] public void Unlearn_cascades_to_disconnected_nodes() { /* learn chain S→a→b→c from a Spark on character1; DependentsOf(a) == {b,c}; Unlearn(a) → none of a,b,c learned */ }
[Fact] public void RefundAll_keeps_only_rank0_entries() { /* character2 → Learned empty; Skills are exactly the 15 rank-0 entries; Available 22 */ }
[Fact] public void Edited_skills_survive_save_and_reload() { }
```

- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(core): skill learning with paths, points and cascade unlearn`.

### Task 10: Inventory editing

**Files:**
- Create: `src/EnshroudedSaveEditor.Core/Editing/InventoryEditor.cs`, `InventoryLayout.cs`, `NewItemFields.cs`
- Test: `tests/EnshroudedSaveEditor.Core.Tests/Editing/InventoryEditorTests.cs`

**Interfaces:**
- Consumes: Tasks 3, 5, 7.
- Produces:
  - `static class InventoryLayout`. These are **provisional** until Task 12, and each const carries a `// confirmed in-game: no` comment that Task 12 flips:
    - `int InventorySlots = 40`, `int ExtraSlots = 8`, `int Columns = 8`;
    - `bool EquipMapKnown = false`;
    - `IReadOnlyDictionary<EquipSlot, byte[]> EquippedSlotsFor`: empty until Task 12.
  - `readonly record struct ItemRef(ContainerKind Kind, byte Slot)`
  - `static class NewItemFields`: `ItemRecord Create(ItemInfo info, byte slot, int count, int? level, Rarity? rarity, PlayerCharacter pc)`.
    - It copies the field set (flags plus the bit 2/3 bytes) of an existing record of the same `ItemId`, or else of the same category and slot, from `pc`. Then it sets level and rarity.
    - Otherwise: armour and rings get level + rarity (`0x11`); weapons get level + durability + rarity + an empty gem socket (`0x33`). Durability is current = max, copied from the max of any weapon record in `pc`, or else `(100, 100)`.
    - Non-gear gets no fields (`0x00`). Rarity `Extra` byte = 0. *(my calls, checked in Task 11 "Test Items")*
  - `sealed class InventoryEditor(SaveSession s, IItemCatalog items)`:
    - `ItemRecord? At(ItemRef r)`. Ghost records count as empty.
    - `void SetCount(ItemRef r, int count)`: 1..MaxStack; an unknown item allows 1..65535.
    - `void SetItemLevel(ItemRef r, int level)`: MinLevel..MaxLevel, and the field must exist.
    - `void SetRarity(ItemRef r, Rarity v)`: the field must exist.
    - `void SetDurability(ItemRef r, uint current, uint max)`: current ≤ max.
    - `void Remove(ItemRef r)`: `Full` only.
    - `void Move(ItemRef from, ItemRef to)`: `Full` only; swaps when the target is occupied. Moving into `Equipped` requires `EquipMapKnown` and a matching slot.
    - `ItemRef Add(ItemInfo info, int count, int? level, Rarity? rarity, ItemRef? target)`: `Full` only. The target must be an empty or ghost Inventory slot, or else the first empty slot in `0..InventorySlots-1` is used. "No room in the backpack" when none is free. A ghost record in the target slot is replaced in place.
  - Edits that need `Full` reject on other statuses with "Adding and moving items isn't available for this character yet".

- [ ] **Step 1: Write failing tests** (character2 unless stated)

```csharp
[Fact] public void SetCount_edits_and_caps_at_max_stack() { /* Fireball I slot 30: 87 → 120 ok (reference max); > MaxStack rejected */ }
[Fact] public void SetCount_rejects_invalid() { /* 0, -1, MaxStack+1 → EditRejectedException; ChangeCount 0 */ }
[Fact] public void Add_puts_stack_in_first_free_slot_and_increments_count_byte() { /* Copper Ore ×250 → Inventory records 22; reparse finds it; count byte n2 == 22 */ }
[Fact] public void Add_into_ghost_slot_replaces_it() { /* target slot 15 (Bronze Sword ghost) → slot 15 now Copper Ore, records still 21 */ }
[Fact] public void Add_gear_uses_level_and_rarity() { /* Wolf's Snarl Longbow level 20 Epic → flags include bits 0 and 4; Level 20; Rarity Epic */ }
[Fact] public void Add_rejects_when_backpack_full() { /* fill every free slot, then Add → "No room in the backpack"; bytes unchanged */ }
[Fact] public void Move_swaps_two_inventory_stacks() { }
[Fact] public void Equip_is_refused_until_the_slot_map_is_known() { }
[Fact] public void InPlaceOnly_allows_count_but_not_add() { /* character1: SetCount on copper arrows 88 → 500 ok; Add → rejected message */ }
[Fact] public void Edits_survive_save_and_reload() { }
```

- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(core): inventory editing`.

---

## Phase 4: In-game checks (session A)

### Task 11: Experiment session A, run with the owner

**Files:**
- Create: `tools/EnshroudedSaveEditor.Lab/Experiments.cs`, `docs/research/in-game-findings.md`
- Test: `tests/EnshroudedSaveEditor.Lab.Tests/ExperimentsTests.cs`

**Interfaces:**
- Consumes: everything in Phases 1–3.
- Produces: the Lab command `make-experiments --remote <dir>`. It makes these **copies** (never touching `character0–2`), each named so that it shows in the game's character list. It then prints a table of file ↔ name ↔ purpose.

| Copy name | Source | Setup | Owner reports |
|---|---|---|---|
| **Test Plain** | character2 | identity only (new GUID, slot, name) | listed? loads? same gear, skills and level as Amadis level 7? In the skill menu, do Wizard, Snake Eater and Dark Arts (rank-0 entries) show as learned? |
| **Test Repack** | character2 | every section recompressed by our writer; level 9, XP 0 | loads; level 9; XP bar at 0; skill points shown in-game (expect 4 available) |
| **Test Slots** | character2 | Inventory and Extra cleared; Copper Arrow (`f12faa48`, max 500) in every Inventory slot 0–39 with count = slot+1, and in Extra 0–7 with count 100+slot | screenshot of inventory and hotbar: which counts appear where, which are missing (gives the slot ↔ grid ↔ hotbar map and capacity) |
| **Test Items** | character2 | Health Potion 9→20; Fireball I set to its max stack + 100 (the Lab writes the record directly, bypassing the editor's cap); add Copper Ore ×250, Wolf's Snarl Longbow level 20 Epic, Adventurer Helmet (`ff1b25e5`) level 10 Rare, Ring of Health; move one inventory stack to an empty slot | each item visible and usable, with that level and rarity; bow tooltip damage sane; what the over-max stack shows |
| **Test Skills** | character1 (69 points) | learn 10 connected nodes under Game rules, including one passive and one 3-rank node at rank 2 | skills shown learned; points left in-game = 69 − spent; the passive's bonus shows on the character stats; then learn one more in-game and quit |
| **Test Overspend With A Forty Char Name 40** | character2 | every rank-0 entry set to rank 1 (spent > earned); 40-character name written directly with `CharacterData.With`, bypassing the editor's 32 cap | does the game keep, clamp or reset the tree; does the full name show; also, in a new-character screen, how many characters the game's name box accepts (cancel afterwards) |

  - **Steam Cloud check:** after Test Repack is confirmed, fully exit the game and Steam, restart both, and reload Test Repack. The level must still be 9.
  - **Second Steam Cloud check:** with Steam **running** but the game closed, the Lab applies level 10 to Test Repack. Load it: is it 10, does Steam show a conflict, or is it reverted?

- [ ] **Step 1: Implement `make-experiments`** using the Core editors. Every write goes through `LabCopyRegistry.EnsureWritable`. Add `Experiments_build_expected_copies`: run against a temp copy of the fixtures, then check that six new files exist, the sources are byte-identical, and each setup is visible by re-parsing.
- [ ] **Step 2: Commit** `feat(lab): experiment builder for session A`.
- [ ] **Step 3: Run `make-experiments` against the live `remote` folder.** It only adds new files. Check that `character0–2.ksd` hashes are unchanged before and after. Start `watch` in the background.
- [ ] **Step 4: Send the owner the checklist** (the table, plus the two Steam Cloud checks, one numbered line each). **Stop and wait** for their report.
- [ ] **Step 5: Record every observation** in `docs/research/in-game-findings.md` as experiment → observation → decision. Include `watch` captures, and `diff` each capture against what the Lab wrote.
- [ ] **Step 6: Commit** `docs: in-game findings session A`.

### Task 12: Apply session A findings

**Files:**
- Modify: `InventoryLayout.cs`, `NewItemFields.cs`, `SkillsEditor.cs`, `SaveSession.cs`, `CharacterEditor.cs`, and the tests that pin them; spec §10 (mark settled items)

**Interfaces:**
- Consumes: `docs/research/in-game-findings.md`.
- Produces:
  - **`InventoryLayout`**: the confirmed capacity, columns, `EquipMapKnown`, `EquippedSlotsFor`, and a `HotbarSlots` range (an empty range if the hotbar isn't in the save).
  - **Overspend:** if the game resets, clamps or rejects the overspent tree, delete `PointsMode.Unlimited` everywhere (owner's decision, spec §10.5). The points switch is then removed from the UI tasks.
  - **Steam:** if the Steam-running check reverted or conflicted, `Save` returns a new `SaveResult.SteamRunning` when `IsSteamRunning()` is true. The UI message is "Close Steam before saving, so Steam Cloud keeps your change". If the edit was lost even with Steam closed, stop and tell the owner (spec §10.2: they turn Cloud off).
  - **Name limit** and **level cap** from the observations, if they differ from 32 and 50.
  - **`NewItemFields`** adjusted to whatever field sets the game kept or rewrote (from the `watch` captures).

- [ ] **Step 1: Update the tests to the confirmed values first.** Run them. Expected: FAIL where values changed.
- [ ] **Step 2: Apply the changes.** Run `dotnet test`. Expected: PASS.
- [ ] **Step 3: Commit** `feat: apply in-game findings from session A`.

---

## Phase 5: App (Blazor Hybrid)

How it fits together:
- The screens are Razor components in `UI`. They call an in-process `EditorService` that wraps the Core editors and `IGameData`.
- A WinForms window (`App`) hosts them in `BlazorWebView`, as Stoneshard does. There is no HTTP API.
- **Icons** are files served through the virtual host `https://icons.ese/…`, mapped to the icon cache. Components get URLs from `IAssetUrls`, so tests need no WebView.
- **Until Task 20,** `IconService` returns category glyph URLs (`CategoryGlyphs`: the inline SVG line icons from the mockup, one per `ItemCategory` or `EquipSlot`, written as `.svg` files to the cache).

### Task 13: Editor service

**Files:**
- Create:
  - `src/EnshroudedSaveEditor.UI/EnshroudedSaveEditor.UI.csproj` (`Microsoft.NET.Sdk.Razor`, `net10.0`; references Core and GameData)
  - `Services/EditorService.cs`, `GameDataProvider.cs`, `Settings.cs`, `Views.cs`, `ChangeLog.cs`, `IAssetUrls.cs`
  - `src/EnshroudedSaveEditor.GameData/Icons/IconService.cs`, `CategoryGlyphs.cs`
  - `tests/EnshroudedSaveEditor.UI.Tests/…` (project and `EditorServiceTests.cs`)

**Interfaces** (namespace `EnshroudedSaveEditor.UI.Services`):
- Consumes: all Core and GameData types.
- Produces:
  - `sealed record EditorOptions(IReadOnlyList<string> SaveRoots, string AppDataRoot, IProcessProbe Probe, Func<string?>? PickFolder)`
  - `sealed class GameDataProvider(Func<IGameData> load)`: `IGameData Data` (lazy, loaded once), `string Source`.
  - `sealed class Settings { string? GameDir; string? SaveRoot; PointsMode Mode; static Settings Load(string path); void Save(string path); }`: JSON, camelCase.
  - **Views:**
    - `CharacterView(string Path, string FileName, string Name, int Level, DateTime SavedAtLocal, CharacterStatus Status, string? StatusMessage, int? UnspentPoints)`
    - `ItemView(uint Id, string Name, ItemCategory? Category, Rarity? Rarity, int Count, int MaxStack, int? Level, int? MinLevel, int? MaxLevel, uint? Durability, uint? MaxDurability, bool Known, string IconUrl)`
    - `SlotView(ItemRef Ref, string? Label, ItemView? Item)`
    - `SkillNodeView(uint Id, string Name, string Description, SkillNodeType Type, SkillClass Class, int Cost, int Rank, int MaxRank, NodeState State, double X, double Y)`
    - `SessionView(string Path, string FileName, string Name, int Level, uint Xp, int LevelCap, bool ReadOnly, string? ReadOnlyReason, InventoryStatus Inventory, IReadOnlyList<SlotView> Equipped, IReadOnlyList<SlotView> Backpack, IReadOnlyList<SlotView> Hotbar, PointsSummary Points, PointsMode Mode, IReadOnlyList<SkillNodeView> Nodes, IReadOnlyList<(uint A, uint B)> Links, IReadOnlyList<ArchetypeLabel> Labels, IReadOnlyList<string> Pending, bool CanUndo)`
    - `CatalogEntryView(uint Id, string Name, ItemCategory Category, string SubCategory, Rarity BaseRarity, int MaxStack, int MinLevel, int MaxLevel, string IconUrl)`. `SubCategory` is the weapon type for Weapons, the `EquipSlot` name for Equipment, and "" otherwise.
  - `sealed class IconService(string iconCacheRoot)` (GameData): `string ItemIcon(uint id, ItemInfo? info)` and `string SkillIcon(SkillNode node)`. Until Task 20 both return the category or type glyph, `https://icons.ese/glyph/<name>.svg`, writing the SVG to the cache on first use.
  - `sealed class EditorService(EditorOptions o, GameDataProvider g, IconService icons)`:
    - `event Action? Changed`;
    - `IReadOnlyList<CharacterView> Characters()`;
    - `bool IsOpen`, `SessionView Current`;
    - `SessionView Open(string path)`, `SessionView Reload()`;
    - `SaveResult Save()`, `IReadOnlyList<BackupInfo> Backups()`, `SessionView Restore(string backupPath)` (listed backups only);
    - `bool GameRunning()`, `bool SteamRunning()`;
    - character: `SetName(string)`, `SetLevel(int)`, `SetXp(long)`;
    - inventory: `SetCount(ItemRef, int)`, `SetItemLevel(ItemRef, int)`, `SetRarity(ItemRef, Rarity)`, `SetDurability(ItemRef, uint, uint)`, `RemoveItem(ItemRef)`, `MoveItem(ItemRef, ItemRef)`, `(SessionView View, ItemRef Added) AddItem(uint itemId, int count, int? level, Rarity? rarity, ItemRef? target)`;
    - `IReadOnlyList<CatalogEntryView> Catalog()`: excludes `Customization`;
    - skills: `Learn(uint)`, `IReadOnlyList<uint> DependentsOf(uint)`, `Unlearn(uint)`, `RefundAll()`, `SetMode(PointsMode)` (saved in settings);
    - `SessionView Undo()`.
    - Each edit returns a `SessionView` and raises `Changed`. A read-only session rejects edits with "This save is read-only: {reason}".
  - **`ChangeLog`:** each edit appends one human line to `Pending`:
    - "Copper Ore 12 → 37", "Added Hunting Bow", "Removed Torch";
    - "Learned Blink", "Unlearned Blink (+2 more)", "Refunded all skills";
    - "Level 7 → 10", "XP 6500 → 0", "Name Amadis → X".
  - **`Undo`** restores a snapshot of the three records taken before the last edit. The stack depth is 50, and it is cleared on Save and Reload.
  - **Views hide** records whose item is `Customization`. Unknown items show as "Unknown item {id:x8}" with `Known = false`.

- [ ] **Step 1: Write failing tests** (a temp copy of the fixtures as the save root, `FakeProbe`, a temp app data root)

```csharp
[Fact] public void Characters_list_three_with_unspent_points() { /* order c2,c1,c0; UnspentPoints 0, 69, 40 */ }
[Fact] public void Open_edit_save_round_trip() { /* c2: SetLevel(10) → Pending ["Level 7 → 10"], Points.Available 6; Save → Saved; reopen → Level 10, Pending empty */ }
[Fact] public void Undo_reverts_last_edit_only() { }
[Fact] public void Reload_discards_unsaved_edits() { }
[Fact] public void Edit_rejections_carry_the_message() { /* SetCount 0 → EditRejectedException with "1"; SetName 33 chars */ }
[Fact] public void Save_reports_gameRunning() { }
[Fact] public void Save_reports_changedOnDisk() { }
[Fact] public void Backups_are_listed_and_restorable() { /* unlisted path rejected */ }
[Fact] public void Catalog_excludes_customization_and_has_icons() { /* no Customization entries; every IconUrl starts "https://icons.ese/" */ }
[Fact] public void AddItem_returns_ref_for_immediate_stack_edit() { /* c2 Copper Ore ×10 → Added; SetCount(Added, 37) ok; Pending ["Added Copper Ore ×10", "Copper Ore 10 → 37"] */ }
[Fact] public void Skills_view_marks_states_and_rank0_as_not_learned() { /* c2: 15 Learned nodes + 12 Sparks Learned; "Wizard" Available or Locked, Rank 0 */ }
[Fact] public void Customization_records_are_hidden() { /* no SlotView item with Category Customization */ }
```

- [ ] **Step 2: Run** `dotnet test --project tests/EnshroudedSaveEditor.UI.Tests`. Expected: FAIL (compile).
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(ui): editor service`.

### Task 14: Window, character picker, editor frame and packaging

**Files:**
- Create:
  - `src/EnshroudedSaveEditor.App/EnshroudedSaveEditor.App.csproj`: copy Stoneshard's, including the `BundleStaticWebAssets` target and `IncludeAllContentForSelfExtract`; set `AssemblyName` `EnshroudedSaveEditor`.
  - `src/EnshroudedSaveEditor.App/Program.cs`, `MainForm.cs`, `wwwroot/index.html`
  - `src/EnshroudedSaveEditor.UI/App.razor`, `Screens/CharacterPicker.razor`, `Screens/Editor.razor`
  - `Components/Banner.razor`, `BackupsDialog.razor`, `NumberField.razor`, `Stepper.razor`
  - `wwwroot/app.css`; `wwwroot/fonts/` (Cinzel and Source Sans 3 woff2 + `OFL.txt`)
  - `tests/EnshroudedSaveEditor.UI.Tests/ShellTests.cs`
- Modify: `THIRD_PARTY.md` (fonts, OFL)

**Interfaces:**
- Consumes: `EditorService` (Task 13).
- Produces:
  - **`MainForm`:**
    - size 1440×900, title "Enshrouded Save Editor";
    - the WebView2 user data folder is `<AppDataRoot>\webview2`;
    - it maps `icons.ese` with `SetVirtualHostNameToFolderMapping` to `<AppDataRoot>\cache\icons`;
    - `EditorService` is registered as a singleton.
  - **`CharacterPicker`:** one card per `CharacterView` showing name, "Level N", file name, last saved (local time), "N points to spend", and a status chip on Damaged or Unsupported. Click opens it. If the save root isn't found, a "Choose saves folder" button calls `PickFolder`. While the game is running, the same banner as the editor shows here.
  - **`BackupsDialog`:** lists `Backups()` newest first with local time. **Restore** asks for confirmation, then calls `Restore`.
  - **`Editor`** frame per the mockup top bar:
    - wordmark; the character pill "Name · Level N · file";
    - "Backups" and "Save changes";
    - tabs "Character", "Inventory", "Skills";
    - the bottom unsaved-edits bar "Unsaved: …" listing `Pending` joined with " · ".
  - **Save results** shown in a `Banner`: Saved → "Saved. Backup kept."; GameRunning → the exact copy from Global Constraints; ChangedOnDisk → "The file changed since you opened it." plus a **Reload** button; VerifyFailed → "Save check failed; your file was restored from the backup."
  - **`NumberField`** rejects non-numeric input and shows the message inline. **`Stepper`** is NumberField with − / + and optional chips.
  - **Single-file publish:** `dotnet publish src/EnshroudedSaveEditor.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o artifacts/publish`.

- [ ] **Step 1: Write failing bUnit tests**

```csharp
[Fact] public void Picker_shows_three_amadis_cards_with_levels() { /* "Level 7", "Level 18", "Level 12"; "69 points to spend" on character1 */ }
[Fact] public void Editor_shows_game_running_banner_and_disables_save() { /* FakeProbe(game:true) → banner text exact; Save changes disabled */ }
[Fact] public void Pending_bar_lists_unsaved_edits() { /* SetLevel 10 → bar contains "Level 7 → 10" */ }
[Fact] public void ChangedOnDisk_offers_reload() { }
[Fact] public void NumberField_rejects_non_numeric_input() { /* "12a" → message, no service call */ }
[Fact] public void ReadOnly_session_shows_reason_and_no_save() { }
```

- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement** the shell and the CSS tokens. Font files come from Google Fonts' woff2 downloads (latin subset).
- [ ] **Step 4: Run** the tests. Expected: PASS. Then publish. Expected: `artifacts/publish/EnshroudedSaveEditor.exe` exists. Copy it alone to a temp folder and run it: the window opens on the picker, listing the live characters read-only. **Don't open and save a real character.**
- [ ] **Step 5: Commit** `feat(app): window, character picker, editor frame, single-file publish`.

### Task 15: Character tab

**Files:**
- Create: `src/EnshroudedSaveEditor.UI/Tabs/CharacterTab.razor`; test `CharacterTabTests.cs`

**Interfaces:**
- Consumes: `SetName`, `SetLevel`, `SetXp`, `SessionView.Points`.
- Produces:
  - **Name field;** level `Stepper` (1..`LevelCap`); XP `NumberField`. When `XpPerLevel` is known, it is followed by "of {need} to level {N+1}".
  - A read-only points summary: "From levels", "Elixir Wells & Shroud Roots", "Spent", "Available". Available is red when negative.
  - **Lowering the level into overspend** shows "Your skills cost more than this level gives. Refund skills, or switch to Unlimited." The Unlimited clause is omitted if Task 12 dropped it.
  - **Save with overspend under Game rules** asks for confirmation first.

- [ ] **Step 1: Write failing tests:** `Level_change_updates_points`, `Overspend_warning_appears_below_spent_level`, `Name_validation_message_shows`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(ui): character tab`.

### Task 16: Inventory tab and Add item

**Files:**
- Create: `Tabs/InventoryTab.razor`, `Components/ItemSlot.razor`, `Components/AddItemDialog.razor`; tests `InventoryTabTests.cs`, `AddItemDialogTests.cs`

**Interfaces:**
- Consumes: the inventory and catalog methods of `EditorService`; `docs/superpowers/specs/mockups/inventory-and-picker.html` as the visual reference.
- Produces:
  - **Layout:** Equipped panel (left, labelled slots from `InventoryLayout`), Backpack grid (`Columns` wide) and Hotbar (hidden when empty), Selected panel (right).
  - **Slots:** rarity-tinted background, icon, count badge; ghost and empty slots show a faint "+".
  - **Selected panel:** name, category · rarity; Stack `Stepper` with chips "1", "Half" (⌈max/2⌉) and "Max"; for gear, Level stepper, Rarity select and Durability current/max; "Remove from backpack".
  - **Drag and drop** with HTML5 drag events moves or swaps (`MoveItem`). Disallowed drops show the rejection message in a banner. Use the Stoneshard inventory drag code as the pattern.
  - **AddItemDialog:**
    - **Categories (left):** "All", then each category with its count. Weapons expand into weapon types, and Equipment into slots.
    - **Middle:** a search over name and internal name, rarity chips, and item tiles showing the level range.
    - **Right:** "How many" (capped at the max stack; gear fixed at 1), Level (default: the character level clamped to the item's range), Rarity (default: base), "Goes to" text, and "Add to backpack".
    - **After adding,** the dialog closes and the new stack is selected, so its count can be changed straight away (owner).
  - **Read-only states:** with `InventoryStatus` not `Full`, the Add, Remove and drag controls are disabled with the service's message. `NotFound` shows "Inventory couldn't be read for this character."

- [ ] **Step 1: Write failing tests:**
  - `Selecting_a_stack_shows_stepper_with_max`;
  - `Max_chip_sets_count_to_max_stack`;
  - `Add_dialog_filters_by_category_and_search`: Weapons → RangedBow shows "Wolf's Snarl Longbow"; search "copper ore" finds 1;
  - `Adding_selects_the_new_stack`;
  - `Add_disabled_for_InPlaceOnly_character` (character1);
  - `Customization_never_listed`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(ui): inventory tab and add-item browser`.

### Task 17: Skills tab with zoomable tree

**Files:**
- Create: `Tabs/SkillsTab.razor`, `Components/SkillTree.razor`, `wwwroot/skilltree.js`; tests `SkillsTabTests.cs`

**Interfaces:**
- Consumes: the skills methods and views of `EditorService`; `docs/superpowers/specs/mockups/skills-tree.html` as the visual and behavioural reference (its inline script is the approved interaction).
- Produces:
  - **`SkillTree.razor`** renders an SVG: links (gold when both ends are learned), nodes (abilities as diamonds; attributes, passives and Sparks as circles with radii 3.4, 4.4 and 4.2 in tree units scaled to a 640-unit view), archetype labels and a selection ring.
  - **`skilltree.js`** exports `attach(svg, dotnetRef)`:
    - wheel zoom at the cursor (factor `exp(deltaY × 0.0015)`, from 1× to 10×);
    - drag to pan with pointer capture; a click is a press without more than 3 px of movement;
    - click a node → `dotnetRef.invokeMethodAsync("NodeClicked", id)`;
    - click an archetype label → a 350 ms ease to a view of width 640/3.2 centred on it;
    - hover → `dotnetRef.invokeMethodAsync("NodeHovered", id | 0)`;
    - also exports `zoomBy(svg, factor)`, `fit(svg)` and `flyTo(svg, x, y)`.
  - **`SkillsTab`:**
    - tree panel with "+", "−", "FIT" and a zoom readout;
    - hover card: name, "rank r/max · cost c", description, and the click action ("Click to learn", "Click to rank up", "Click to unlearn");
    - right column: the "Skill points" panel with the "Game rules" / "Unlimited" switch and From levels / Wells & Roots / Spent / Available, then the "Reskill" panel with "Find a skill…" (it flies to the first match by name and highlights it), "Refund all" (with confirmation) and "Undo".
  - **Clicking a learned node** at max rank unlearns it. When `DependentsOf` isn't empty, a confirmation lists the dependents by name first.
  - **Rejected learns** show the service message in the hover card for 3 s.

- [ ] **Step 1: Write failing bUnit tests** (JS interop in loose mode; call `NodeClicked` directly):
  - `Tree_renders_243_nodes_and_12_labels`;
  - `Learned_nodes_have_learned_class` (c2 → 15 + 12 Sparks);
  - `NodeClicked_learns_connected_node_and_updates_points`;
  - `Unlearn_with_dependents_asks_first`;
  - `Unlimited_switch_allows_overspend` (removed if Task 12 dropped Unlimited);
  - `Find_a_skill_calls_flyTo`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS. Then run the app on a **Lab copy** (Test Skills) and check by hand that wheel, drag, labels and node clicks behave like the mockup. Don't save.
- [ ] **Step 5: Commit** `feat(ui): skills tab with zoomable tree`.

### Task 18: Owner tries the app (first build, reference data)

**Files:**
- Modify: `docs/research/in-game-findings.md` (owner feedback section)

- [ ] **Step 1: Publish** (Task 14 command). Use `copy` to make a fresh **"Try Me"** copy of character1 for the owner.
- [ ] **Step 2: Tell the owner** where the `.exe` is, that **Try Me** is the character to edit, and what to try: each §11 bullet, with Save. Then load Try Me in Enshrouded. **Stop and wait** for their report.
- [ ] **Step 3: Record their feedback,** fix anything they flag (each fix with its own test), and commit `fix: owner feedback on first build` per fix. Then ask whether to push. Push only on a yes (standing rule).

---

## Phase 6: Reading the install (parallel track)

This phase can start any time after Task 7, in parallel with Phases 3–5. Nothing in Phases 3–5 depends on it.

### Task 19: Install discovery and the kfc reader

**Files:**
- Create:
  - `src/EnshroudedSaveEditor.GameData/Install/GameInstall.cs`, `SteamLibraries.cs`
  - `src/EnshroudedSaveEditor.GameData/Kfc/…`
  - `docs/research/kfc-format.md`
  - `tests/EnshroudedSaveEditor.GameData.Tests/InstallTests.cs`, `KfcGameDataTests.cs`
- Modify: `UI/Services/GameDataProvider.cs` (prefer the install, piece by piece); `Settings` (`GameDir`)

**Interfaces:**
- Produces:
  - `static class SteamLibraries`: `IReadOnlyList<string> Find()`, parsing `steamapps\libraryfolders.vdf` (on this machine `C:\Program Files (x86)\Steam` and `D:\SteamLibrary`).
  - `sealed class GameInstall`:
    - `static GameInstall? Find(string? configuredDir)`;
    - `string Dir`;
    - `static bool IsGameDir(string dir)`: `enshrouded.exe`, `enshrouded.kfc` and `enshrouded.kfc_resources` all exist.
  - `sealed class KfcGameData : IGameData`: `static KfcGameData Load(GameInstall g, string cacheDir)`, with `Source = "Your Enshrouded install"`.
    - Any piece it can't produce throws `NotSupportedException(piece)` from that property.
  - `sealed class CompositeGameData(IGameData primary, IGameData fallback) : IGameData`: each property tries `primary`, and on `NotSupportedException` uses `fallback`. `Source` lists which pieces came from where.
  - `GameDataProvider` loads `CompositeGameData(KfcGameData, ReferenceGameData)` when an install is found, and `ReferenceGameData` otherwise.
- **Rules:**
  - Study `Brabb3l/kfc-parser` (GPL-3.0) to learn the format. **Don't copy its code.** Write the format notes in `docs/research/kfc-format.md` in your own words, and implement in C#.
  - Game files are opened read-only, with `FileShare.ReadWrite`.
  - The cache lives in `<AppDataRoot>\cache\gamedata\`, keyed to the size and timestamp of `enshrouded.kfc` and `enshrouded.exe`.
  - **Time box:** if after the resource index and type metadata work, item names still can't be matched to the reference for ≥ 95 % of ids, stop. Record the findings in `kfc-format.md`, keep the reference data, and tell the owner (spec §7).

- [ ] **Step 1: Write failing tests** (skip if the install is missing)

```csharp
[Fact] public void Finds_install_through_steam_libraries() { /* Dir == D:\SteamLibrary\steamapps\common\Enshrouded */ }
[Fact] public void Items_match_reference() { /* ≥ 95% of reference ids present with equal Name, MaxStack, Category, MinLevel, MaxLevel */ }
[Fact] public void Tree_matches_reference() { /* same 243 node ids, costs, max ranks, links; positions within 0.5 */ }
[Fact] public void Rewards_and_level_table_present() { /* Rewards ⊇ reference reward ids with equal points; LevelCap == 50 */ }
```

- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Research, write `kfc-format.md`, implement** `KfcGameData` and the provider fallback.
- [ ] **Step 4: Run.** Expected: PASS, or the time box was hit and recorded. With the time box hit, `Items_match_reference` and the related tests are marked `Skip` with the findings' reason.
- [ ] **Step 5: Commit** `feat(gamedata): read items, tree and rewards from the install`.

### Task 20: Icons from the install

**Files:**
- Modify: `GameData/Icons/IconService.cs`
- Create: `GameData/Kfc/…` (texture lookup and decode); test `IconTests.cs`

**Interfaces:**
- Produces: `IconService.ItemIcon(uint id)` and `IconService.SkillIcon(uint nodeId)`, returning `https://icons.ese/<kind>/<id:x8>.png`. Each PNG is extracted on first use into `<AppDataRoot>\cache\icons\`. When a texture can't be found or decoded, it falls back to the category glyph.
- **Rules:**
  - Block-compressed formats are decoded in C#. Any added package needs its reason in the commit.
  - Nothing extracted is ever committed.
  - **Time box:** if no item icon decodes correctly after the texture index works, stop, record it, and keep the glyphs (spec §7).

- [ ] **Step 1: Write failing tests:**
  - `Copper_arrow_icon_is_a_png_of_expected_size`: the dimensions come from the texture header, about 128–256 px square;
  - `Unknown_id_falls_back_to_glyph`;
  - `Icons_are_cached_once`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS, or the time box was recorded.
- [ ] **Step 5: Commit** `feat(gamedata): item and skill icons from the install`.

---

## Phase 7: Finish

### Task 21: Final acceptance with the owner

**Files:**
- Modify: `docs/research/in-game-findings.md`; spec §10 (final status of each risk)

- [ ] **Step 1: Make a fresh copy for the walkthrough.** Run `dotnet test` (all projects). Expected: PASS. Publish. Make a fresh **"Final Check"** copy of character1.
- [ ] **Step 2: Send the owner the §11 walkthrough** on Final Check, including the Steam Cloud restart. **Stop and wait.**
- [ ] **Step 3: Record the results.** Fix what fails (each fix with a test). Then use `superpowers:finishing-a-development-branch`: merge `feature/save-editor` into `main`, and push only after the owner says yes.
