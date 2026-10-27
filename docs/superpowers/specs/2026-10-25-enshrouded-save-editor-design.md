# Enshrouded Save Editor — Design

Date: 2026-10-25
Status: built; final walkthrough passed 2026-10-26 (what is still open is marked in §10)

## 1. Intent

A save editor for **Enshrouded 1.0** that lets the owner change their character's inventory, equipped gear, skills, level, XP and name. It should look like the game and be effortless to run. It is a sibling of the owner's Stoneshard Save Editor (`D:\Projects\stoneshard-save-editor`) and follows the same shape.

**Why it exists:** the owner wants to:
- add gear and other items, and change stack sizes, including after adding;
- reskill the character within the points the game allows (unlimited points were asked for too, but the game resets a tree that spends more than it earned, §10.5);
- change level, XP and name.

**Prior art, and why it isn't enough:**

- **[Kimikozi/enshrouded-save-editor](https://codeberg.org/Kimikozi/enshrouded-save-editor)** (Codeberg, MIT, Go + JavaScript in WebView2). This is the only open-source editor for the 1.0 `.ksd` format found so far. It edits level, inventory, rarity, item level, durability, gem sockets and the skill tree. Its `lib/ksdcore.js` (container) and `lib/pc.js` (records) are the best format documentation available. They were checked against the owner's saves (§6). Its parser finds the inventory heuristically (by scanning), and it turns off item adding when the layout isn't recognised. It isn't the owner's stack and doesn't have the owner's look.
- **[RechiTaun/enshrouded-character-editor](https://github.com/RechiTaun/enshrouded-character-editor)** (MIT, Python/Tk). It targets the **Early Access** `KSC1` format (`characters`, `characters-index`), not 1.0's `.ksd`. Its notes on Steam Cloud handling and safety checks are still useful reading.
- **[svenbledt/enshrouded-save-unlocker](https://github.com/svenbledt/enshrouded-save-unlocker)** is a closed binary (`SaveUnlocker-0.1.2.zip`). It has no source to learn from.

## 2. Goals

1. **Effortless to run.** One `.exe`, double-click, nothing to install. It opens in its own window and finds the saves and the Enshrouded install by itself. It backs up a save automatically before changing it.
2. **Looks like Enshrouded** (owner). The style is dark and shrouded: deep blue-black panels with thin gold trim and diamond corner marks, Cinzel-style capitals for headings, the game's rarity colours (Common grey, Uncommon green, Rare blue, Epic purple, Legendary orange), and a faint red-purple shroud glow. The approved mockups (§8) set the look.
3. **Inventory** (owner):
   - add any item, found by category and sub-category, by search, or by rarity filter;
   - change a stack's size at any time, including right after adding it;
   - for gear, choose level and rarity when adding, and edit them later along with durability *(level and rarity: my guess, shown in the approved mockup)*;
   - put gear into equipped slots, move items by dragging, and remove items.
4. **Skills** (owner):
   - show the real skill tree, with zoom and pan so nodes are easy to click;
   - learn, rank up and unlearn by clicking nodes; Refund all; Undo; a skill search;
   - the game's points budget is enforced. *(Settled in session A: a points switch with an **Unlimited** mode was planned, but on load the game reset a tree that spent more than it earned, so Unlimited was dropped and only the game's rules remain (owner, §10.5).)*
5. **Character** (owner): level, XP and name.
6. **Saves the game accepts.** Every section has a valid checksum, and everything not edited is left byte-identical.

## 3. Non-goals (v1)

*(my calls; the owner didn't ask for these)*
- World saves (`worldN.ksd`): chests, bases, NPCs, the world itself.
- Fog of war (`fogofwar`), unlocking Elixir Wells and Shroud Roots, recipes and other `player_knowledge` edits. These are read only, for the points budget. *(Since 2026-10-27, clearing the fog of war and discovering the map's locations are in: see `2026-10-26-map-fog-and-locations-design.md`.)*
- Appearance and customization items (hair and similar). They are kept untouched and are not shown as inventory.
- `global.ksd`: known players, adventure subscriptions, user data.
- Status effects (buffs) and the `voiceover` section.
- Creating or deleting characters.
- Platforms other than Windows. Early Access `KSC1` saves.

## 4. Anti-goals: failure even if it "works"

- **Corrupting a save or losing progress.** Every write is preceded by a backup, every section carries a valid checksum, untouched data stays byte-identical, and the written file is read back and checked before the editor reports success.
- **Clunky.** If it looks like a developer tool, or needs setup steps such as installing .NET, Python or Node or running commands, it has failed.
- **Fighting Steam Cloud.** A save the editor writes must not be silently replaced by the cloud copy, and the editor must never leave Steam in a conflict state it can't explain to the owner (§10).
- **Writing what it doesn't understand.** If a section's layout isn't recognised, the editor opens it read-only with a clear reason. It never guesses on write.

## 5. Constraints

- **Stack: .NET with a Blazor Hybrid UI, like the Stoneshard project** (owner).
- **Machine:** Windows 11 (ships WebView2). Installed: .NET SDK 8.0.302 and 10.0.401. Stoneshard pins 10.0.401 in `global.json`.
- **Saves:** Steam Cloud is **on** for Enshrouded, so saves live in `C:\Program Files (x86)\Steam\userdata\43900557\1203620\remote\`. Find them by scanning `Steam\userdata\*\1203620\remote`; with several accounts, let the user choose. With Cloud off, the game uses `%USERPROFILE%\Saved Games\Enshrouded\`. That folder currently only holds `migrated\enshrouded_user.json`; support it if saves appear there.
- **Local saves** (not Steam): `%USERPROFILE%\Saved Games\keen games\Enshrouded\<profile>\`, for example `default`. The game lists Steam characters and local characters separately, and numbers them separately (a local character created first is `character0.ksd` there, whatever the Steam folder holds). The editor scans every `<profile>` folder that holds `character*.ksd`.
- **Game install:** `D:\SteamLibrary\steamapps\common\Enshrouded\` on this machine. Find it through Steam's `libraryfolders.vdf`; if it isn't found, let the user browse once and remember the choice. Contents: `enshrouded.exe` (33.7 MB), `enshrouded.kfc` (9 MB), `enshrouded.kfc_resources` (205 MB), and `enshrouded_000.dat` … `enshrouded_054.dat` (about 50 GB).
- **Game version:** 1.0, which launched 2026-10-15. The owner's saves were written by this build.

## 6. What we know: save format (verified on the owner's saves)

All checks below ran on **copies** of the owner's files. `docs/research/ksd_probe.py` is a throwaway probe that parses the container. It documents the layout below and is not product code.

### 6.1 Files

```
Steam\userdata\43900557\1203620\
  remotecache.vdf          Steam Cloud catalogue: per file size, sha (SHA-1), localtime/remotetime, syncstate
  remote\
    character0.ksd …       one file per character
    world0.ksd …           worlds (not touched)
    global.ksd             known_players, adventure_subscriptions, user_data (not touched)
    user_settings.json
    backup\                the game's own rotating backups: <file>.backup-0 … backup-9
```

The owner's characters are **all named "Amadis"**, so the picker must tell them apart by other details:

| File | Level | XP | Last saved (header time) | Revision | Skill entries | Skill points |
|---|---|---|---|---|---|---|
| `character0.ksd` | 12 | 2913 | 2026-10-15 22:59 | 1 | 0 | 22 + 18 = 40 unspent |
| `character1.ksd` | 18 | 5802 | 2026-10-18 15:52 | 18 | 0 | 34 + 35 = 69 unspent |
| `character2.ksd` | 7 | 6500 | 2026-10-20 12:02 | 991 | 30 | 12 + 10 − 22 = 0 |

The game's `remote\backup\` folder holds ten rotating copies of the most recently played character, `global.ksd` and the world. The editor never writes there. *(my call)*

### 6.2 Container `SV2C` (verified: all three character files and `global.ksd` parse, and every checksum matches)

Little-endian throughout.

| Offset | Type | Field |
|---|---|---|
| 0 | 4 bytes | magic `SV2C` |
| 4 | u16 | version = 2 |
| 6 | u16 | header size = 48 |
| 8 | u32 | file size, equal to the real length |
| 12 | 16 bytes | GUID; the same GUID appears in `character_data` |
| 28 | u32 | revision: a save counter (1, 18, 991) |
| 32 | u32 + u32 | last-saved time, Unix seconds (low, high) |
| 40 | u32 | section table offset, **relative to offset 40** |
| 44 | u32 | section count |
| 48 | … | section blocks |

- **Section table entry** (20 bytes): u32 name offset (relative to the entry), u32 name length, u32 data offset (absolute), u32 data size, u32 checksum.
- **Block:** a 14-byte header, then the body.
  - Header: `S2BH`, u32 block version (2), u8 type id, u8 compressed (1 = zstd, 0 = stored), u32 data version.
  - Body: a zstd frame, or raw bytes when stored.
- **Checksum** = the low 32 bits of **XXH3-64, seed 0**, over the whole block (header plus body).
- **Character sections:**

| Name | Type | Data version | Raw size (c0 / c1 / c2) |
|---|---|---|---|
| `character_data` | 10 | 3 | 69 / 133 / 117 |
| `player_character` | 9 | 5 | 498 / 786 / 706 |
| `voiceover` (character2 only, stored) | 20 | 1 | 388 |
| `player_knowledge` | 15 | 100 | 8,760 / 15,992 / 7,272 |
| `fogofwar` | 16 | 1 | 1,638,413 |

- **Writer requirements:**
  - Copy unchanged blocks byte-for-byte.
  - Recompress changed blocks with real zstd. Kimikozi writes raw or RLE zstd blocks; the game accepted those, but either way must be confirmed in-game.
  - Recompute their checksums, and rebuild the offsets, the section table and the file size.
  - Read the result back and verify it before replacing the live file.
  - Whether the game cares about block order or alignment isn't known. Keep the original order. *(my call)*

### 6.3 `character_data`

`[u8 flag][4-byte storage tag][u32 name length][UTF-8 name][rest…]`. The rest holds the header GUID, a timestamp and further unmapped data. Bytes 1–4 are a storage tag, not a fixed signature: Steam characters carry `93 7B 40 B6`, a local character made in the game carries `a8 23 b2 28` (verified on the local "Test" character). The editor keeps whatever tag it reads, and a copy placed in another storage takes the tag of a game-made character there. The name was verified as "Amadis" on all three files. Kimikozi's `setName` rewrites the length and the bytes, so names can change length.

**The name is stored nowhere else.** This was verified by searching every decompressed section, in UTF-8 and UTF-16, of all three character files, of `global.ksd` (`known_players`, `adventure_subscriptions`, `user_data`) and of `world2.ksd` (13 sections, including `known_users`). The only hits were in `character_data`. A rename therefore touches only the character file.

### 6.4 `player_character`

The layout below comes from Kimikozi's `pc.js`. The points marked verified were checked on the owner's saves.

- **Header:** a short header and five f32 multipliers (all 1.0 on the owner's saves), then **level, u32 at offset 58** (verified on all three: 12, 18, 7). Kimikozi scans nearby when 58 doesn't fit; our reader must instead recognise the header and refuse when it doesn't.
- **XP:** u32 immediately after level. Its meaning (XP into the current level, or total XP) is unknown (§10).
- **Skills:** u32 count, then `count × [u32 node id][u8 rank]`.
  - Verified: all 30 of character2's ids are skill-tree nodes (§7).
  - Fifteen of character2's entries have **rank 0**. All of them are three-rank passives (Wizard, Snake Eater, Dark Arts …), and they cost nothing in the points maths. Treat rank 0 as **not learned** for display and points, and preserve such entries unless the user changes that node. *(Settled in session A: the game shows rank-0 entries as not learned and not connected.)*
  - characters 0 and 1 have no entries. A web guide says 1.0 wiped every character's tree and left the points unspent, which fits.
- **Status effects:** u32 count, then about 27 bytes each. Some entries are longer, and Kimikozi scans forward past them. Preserve them untouched. (Each entry describes its own length through a field mask: `docs/research/player-character.md` §2.)
- **Inventory:** containers in order: **Equipped**, then a header holding the record counts of **Inventory** and **Extra slots**, then their records. The full layout is in `docs/research/player-character.md`.
  - **Equipped** starts with a u32 field mask. Bits 0–15 are the equipment positions, one record each in bit order, with slot bytes 0…n−1. Each set bit 16–18 adds a u16 after the records, meaning unknown (§10.3).
  - **Record:** `[u8 slot][u32 item id][u16 count][u8 field flags]`, then optional fields in bit order:
    - bit 0: u8 item level;
    - bit 1: u32 durability, u32 max durability;
    - bit 2: 8 bytes, unknown;
    - bit 3: 4 bytes, an appearance (transmog) reference;
    - bit 4: u8 rarity (1 Common … 5 Legendary), u8 unknown;
    - bit 5: 5 bytes, a gem socket `[u32 gem item id][u8 marker 0x37]`, all zero when empty.
  - **Verified:** item ids in all three saves resolve to real items (§7). Examples: Adventurer armour pieces, Lesser Ring of Health, Hero's Shield (fields `0x3`/`0x13`), Wolf's Snarl Longbow (`0x33`), Copper Arrow ×88 (max stack 500), Scorching Wand.
  - **Equipped positions** (mask bits; settled in session A): 0–4 appearance (vanity) helmet, chest, arms, legs and boots (Exclusive Hollow on character0); 5 helmet, 6 chest, 7 arms, 8 legs, 9 boots (Adventurer); 10–11 rings; 12 shield or ward; 13 grappling hook; 14 glider; 15 backpack. The mask marks the occupied positions. When the owner took gear off in-game, its bits cleared and the later records moved up. Weapons aren't equipped records: they sit in the action bar.
  - **Customization items** (hair and beard, for example `VC_Hair_04_06_slick_fade`) are **not** inventory records. Their ids are in the record's header, before the level (bytes 9–16). Loose scans from the start of the record mistook them for records with odd counts (T3-R4, `docs/research/player-character.md` §1). The header is kept untouched. No inventory record in the owner's saves is a Customization item, and the editor never adds one (§8.5).
  - **Slot map** (settled in session A): Inventory 0–15 are backpack rows 1–2, 16–23 the action bar (keys 1–8), 24–31 the second action bar row, 32–39 backpack row 3. Extra 0… are the backpack-extension rows: 8 × the rows the equipped backpack gives (§7).

### 6.5 `player_knowledge`

`[u32 version][u32 n][n × u32 id][n × u32 value]`. Verified: the length equals `8 + 8n` on the owner's saves (1,094 and 1,998 entries). It holds known items, recipes, and the ids of **Elixir Wells, Shroud Roots and the starting bonus** that grant skill points. Read-only in v1.

### 6.6 Skill points (verified)

- **Earned** = `2 × (level − 1)` + the points from every reward id (Elixir Well, Shroud Root, starting bonus) present in `player_knowledge`. The reward table maps each id to a point count (§7).
- **Spent** = Σ `cost × rank` over the skill entries.
- On character2, 12 earned from levels plus 10 from rewards equals the 22 spent, which matches a character that has spent everything. The other two characters are in §6.1.

## 7. What we know: game data

**Needed:**
- **Items:** name, category, sub-category (weapon type, equipment slot), base rarity, max stack, level range, internal name and icon.
- **Skill tree:** node ids, names, descriptions, type (attribute, ability, passive, Spark), class (mage, ranger, warrior), cost, max rank, per-rank values, positions, links, the 12 archetype labels, and icons.
- **Rewards:** the table of skill-point rewards.
- **Levels:** the level cap and the XP-per-level table.

**Plan: read it from the install at runtime**, the same principle as Stoneshard. It stays correct after patches, and no game content is committed (one exception, by the owner's decision: the app icon is the game's icon recoloured, see THIRD_PARTY.md).
- `enshrouded.kfc` is the game's resource database, with `enshrouded.kfc_resources` and the `.dat` files beside it.
- **[Brabb3l/kfc-parser](https://github.com/Brabb3l/kfc-parser)** (Rust, **GPL-3.0**) exports and unpacks these files. v0.2.0 was released 2026-10-16, a day after 1.0. It is the reference for the format. Don't copy its code into this repo, because of its licence; study it and implement in C#. *(my call)*
- **Icons are not proven yet.** Textures sit in the `.dat` files, probably GPU block-compressed. If icon extraction can't be made to work, fall back to category glyphs like the mockup's line icons, and tell the owner. *(my call)*

**Reference data, used to check our extraction:** Kimikozi's `data/` folder (MIT code; the data itself belongs to Keen Games). It was built by scripts that download from shrouded.tools and the wikily.gg wiki (`tools/data/dl.py`, `fetchwiki.py`).
- **`itemdb2.json`:** 3,526 items keyed by 8-hex item id.
  - Each row: name, category, rarity, max stack, min and max level, slot, internal name (for example `Armor_T3_Warrior_Tank_Arms_Loot`), description and more.
  - **Categories:** Ammunition, AnimalFood, Blueprints, BuildTools, Collectibles, ColorPalette, Consumables, Currency, Customization, Equipment, FishingRod, Instrument, Materials, Tools, WeaponGem, Weapons.
  - **Slots:** arms, boots, chest, helmet, legs, ring, shield, weapon.
  - **Weapon types:** Dagger, GreatSword, OneHandAxe, OneHandClub, OneHandSword, OneHandWeapon, RangedBow, RangedStaff, RangedWand, Throwable, TwoHandAxe, TwoHandClub, TwoHandHammer, UnarmedFists.
- **`progress.json`** ("Enshrouded game data (build of 2026-10-15)"):
  - 243 nodes with id, x/y, name, type (`R` Spark ×12, `T` attribute ×65, `A` ability ×43, `P` passive ×123), class (`M`/`R`/`W`/`N`), cost (0–5), max rank (0 or 3), sub-tree and per-rank values;
  - 312 links;
  - 12 archetype labels with positions: Wizard, Assassin, Ranger, Guardian, Healer, Trickster, Beastmaster, Warrior, Survivor, Barbarian, Battlemage, Athlete;
  - the reward table (id, points, region, for example "Springlands Well II", 3).

**Not in the reference data:**
- **The level cap and the XP-per-level table.** The 1.0 patch notes put the level cap at **50** (gear goes up to 55 when fully upgraded). Use 50 until the install reader confirms it. The XP table comes from the install; until then, show XP as a raw number without a progress bar. *(my call)*
- **Backpack size.** Rows per backpack are in the item descriptions ("enlarges your inventory by one row … four rows", for Tiny, Small, Medium and Large, old and `_NEW` versions): `Backpack_T0` 1, `T1` 1, `T1_NEW` 2, `T2` 2, `T2_NEW` 3, `T3` 3, `T3_NEW` 4. *(Settled in session A: the grid is 8 columns, the base backpack is 3 rows, and the extension adds 8 × the equipped backpack's rows; Small Backpack `_NEW` shows "Backpack Extension 16" in-game.)*

**Sequencing and fallback** *(my calls)*:
- All game data sits behind one interface in `GameData`.
- **The first build the owner can test runs on embedded reference data:** Kimikozi's `itemdb2.json` and `progress.json`, credited in `THIRD_PARTY.md`. This keeps the install reader off the critical path.
- **The install reader is a parallel track.** It replaces the embedded data piece by piece as each piece is proven equal to the reference (same ids, names, stacks and node layout).
- If the reader can't produce the item catalog and the tree after a focused attempt, the embedded data stays, and the owner is told. The owner approved this fallback for the skill tree, and it is extended to items by analogy.
- If icons can't be extracted either, the editor uses category glyphs.

## 8. The product (owner-approved screens)

The approved mockups are in `docs/superpowers/specs/mockups/`. Open them in a browser. They render without the brainstorm frame's styling but stay readable.
- **`inventory-and-picker.html`:** the app shell, the Inventory tab, and the Add item browser. Item names, icons and slot labels are placeholders. The "Neck" slot, for instance, doesn't exist in Enshrouded; use the real slots (§6.4).
- **`skills-tree.html`:** the Skills tab, drawn from the real tree layout with character2's skills. It is interactive: scroll to zoom, drag to pan, +/−/Fit, click an archetype name to fly there, click nodes. Its learn/unlearn behaviour is a rough demo with no path rules. It lights rank-0 entries as learned, which §6.4 corrects.

### 8.1 Launch: choose a character

- List every `characterN.ksd` with its name, level, file name and last-saved time, plus unspent points when the game data is loaded. All three of the owner's characters are called Amadis, so the other details tell them apart.
- One click opens a character.
- If `enshrouded.exe` is running, a banner says so and **Save changes** is disabled: block, not just warn *(my call, as in Stoneshard)*.

### 8.2 Editor frame

- **Top bar:** ENSHROUDED / SAVE EDITOR wordmark, the character's name, level and file, **Backups**, and **Save changes**.
- **Tabs:** Character · Inventory · Skills.
- **Unsaved edits bar** along the bottom lists the pending changes ("Copper Ore 12 → 37 · Added Hunting Bow"). Nothing touches the save until Save changes is pressed.
- **Save changes:**
  1. re-checks that the game isn't running and that the file hasn't changed on disk since it was opened;
  2. backs up the file;
  3. writes it through a temporary file and an atomic rename;
  4. reads it back and verifies it;
  5. reports the result.
- **Messages along the top** (owner, 2026-10-27): a one-off message (a save's result, a character that couldn't be opened) has a close button (×). Info and warning messages close themselves after 6 seconds; a faint bar along the bottom edge shrinks right to left to show the time left, and hovering pauses it. Errors, and messages with a button (Reload), stay until closed, so a failure isn't missed *(my call)*. Banners for a state (the game running, a read-only save) and questions (leave without saving?) can't be closed; they go when the state ends or the question is answered.
- **Backups view:** lists this character's backups and can restore one. A restore also takes a fresh backup of the current file first.
- **Backup location:** `%LOCALAPPDATA%\EnshroudedSaveEditor\backups\characterN\<timestamp>.ksd`. That is outside the Steam folder, so neither the game nor Steam Cloud sees it. *(my call)*

### 8.3 Character tab

- **Name:** editable, capped at the in-game character creator's limit: **15 characters** (owner, session A). The game displays a longer name written directly, but the editor matches the creator.
- **Level:** − / + and typing, from 1 to the game's level cap.
  - Raising the level adds 2 points per level automatically, because the game derives points from level (§6.6).
  - Lowering the level below what learned skills cost shows the overspend in red, and Save asks the user to refund skills first *(owner approved this guess; Unlimited was dropped, §10.5)*.
- **XP:** editable, shown against the next level's requirement once the XP table and its meaning are known (§10).
- **Points summary:** read-only, the same numbers as the Skills tab.
- **Size** (owner, 2026-10-26): grows with the window against the default 1440 × 900 one (about 1.6× on 2560 × 1440), but doesn't fill it and never shrinks.

### 8.4 Inventory tab (`inventory-and-picker.html`, top)

- **Left: Equipped.** The 16 positions from the save around a figure (§6.4): armour pieces, rings, shield, grappling hook, glider and backpack, plus the five appearance slots (confirmed in session A).
- **Middle: Backpack grid,** 8 wide: the three base rows, then the extension rows of the equipped backpack. Below it is the **Hotbar**, the two action bar rows (§6.4).
  - Each stack shows its icon on a rarity-tinted background and a count badge.
  - Empty slots show a faint +, and clicking one opens Add item for that slot.
- **Right: Selected panel.**
  - **Stack:** a stepper with typing, plus 1 / Half / Max chips. It is capped at the item's max stack from game data *(my guess, owner approved)*.
  - **Gear:** level (within the item's range, never past 55), rarity and durability.
    - **Fixed-level items** (game data min level = max level) show their level read-only. The game sets it to the base level plus upgrades and rewrites any other value (session B, `docs/research/in-game-findings.md`).
    - An item missing from the game data takes levels 1–55.
  - **Remove from backpack.**
- **Drag** to move or swap stacks, including onto equipped slots. Only items whose slot matches can go there.
  - **For now that is a swap only:** an item replaces the equipped item of its kind, and that item goes where the new one was. Equipping into an empty slot and unequipping change the Equipped mask, and when the game changes its byte 2 alongside isn't known yet (§10.3). Until session B settles it, they are refused with a message.
  - A smaller backpack can't be swapped in while its extension would leave items outside the grid.
- Records that aren't shown (customization, unknown) are carried through untouched.
- **Size** (owner, 2026-10-26): the whole tab scales with the window, keeping its proportions, as large as fits without scrolling (it also shrinks for a small window). The Add item dialog scales the same way.

### 8.5 Add item (`inventory-and-picker.html`, bottom)

- Opens from **+ Add item** or an empty slot.
- **Left: categories** from game data, with sub-categories.
  - Weapons split by weapon type; Equipment by slot.
  - Customization is left out; everything else is in, including Currency *(my call)*.
  - Counts appear beside each.
- **Middle:** search across all items, rarity filter chips, and item tiles with icon, name, level range and base rarity. Level and rarity tell apart items that share a name ("Bow Of The Serpent" is five items).
- **Right: Adding panel.**
  - **How many:** up to the max stack; gear is always 1.
  - **Gear:** level (default: the character's level, clamped to the item's range) and rarity (default: the item's base rarity) *(my calls)*.
    - A fixed-level item shows its base level read-only and is added at it (session B).
  - **Goes to:** the slot that was clicked, or the first empty backpack slot: the base rows (Inventory 0–15, then 32–39), then the extension. It goes to the action bar only when that slot was clicked *(owner ruling T12-R2)*.
  - **Add to backpack.** If there's no room, it says so and adds nothing.
- An added stack is selected straight away, so its count can be changed at once (owner).

### 8.6 Skills tab (`skills-tree.html`)

- **Main area:** the real tree at the game's positions, with links and the 12 archetype labels.
  - **Node states:** learned (filled, glowing, in its class colour), can learn next (coloured outline), locked (dim).
  - Shape reflects type: abilities are diamonds; attributes and passives are circles of different sizes.
  - **Zoomed in (2× and closer; owner, 2026-10-26):** abilities show their picture from the install (the game has none for other nodes); attribute nodes show the attribute they raise (STR, DEX, INT, CON, SPI, END); skills with 3 ranks show learned/max (2/3) under the node. From 3×, named skills show their title above the node (not Sparks, nor attribute nodes named after their attribute).
- **Navigation** (owner): wheel zoom at the cursor, drag to pan, + / − / Fit buttons with a zoom readout, and click an archetype name to glide there. **Find a skill** flies to the match and highlights it.
- **Hover card:** name, rank (n/max), cost, description, and what a click will do.
- **Click:** learn → rank up (for 3-rank nodes) → unlearn. **Undo** reverses the last change.
- **Skill points panel:**
  - Shows from levels, from Wells and Roots, spent, and available. A node can be learned only when it links to a learned node or a Spark, and only if enough points are available.
  - There is no Unlimited mode: the game resets an overspent tree (session A, §10.5).
  - **Unlearning** a node that others depend on lists those dependents and unlearns them too, after a confirmation. *(my call; Stoneshard "re-locks what depended on them")*
- **Refund all** clears every learned node back to the Sparks and returns every point.

## 9. How it's built: decided parts

- **.NET 10 with a Blazor Hybrid UI** (owner), as in Stoneshard.
  - Razor components run in a WinForms window through `BlazorWebView` (WebView2). There is no local web server.
  - Solution `EnshroudedSaveEditor.slnx` mirrors Stoneshard's split:
    - `EnshroudedSaveEditor.Core` (container, records, edits, backups, locating saves);
    - `EnshroudedSaveEditor.GameData` (install discovery, kfc reading, catalog, skill tree, icons);
    - `EnshroudedSaveEditor.UI` (Razor);
    - `EnshroudedSaveEditor.App` (WinForms host);
    - tests per project;
    - `tools/EnshroudedSaveEditor.Lab` for in-game experiments.

    *(my call: same names and pattern, owner asked for "like our StoneShard project")*
  - `Directory.Build.props` matches Stoneshard: nullable on, warnings as errors.
- **A single self-contained `.exe`** using Stoneshard's `BundleStaticWebAssets` publish target.
- **Libraries** are the builder's choice. Notes: `System.IO.Hashing` has `XxHash3`, and zstd needs a managed library such as ZstdSharp.
- **Game data is read from the install at runtime** (§7) and cached under `%LOCALAPPDATA%\EnshroudedSaveEditor\cache\`, keyed to the game files' size and timestamp. The first launch shows progress.
- **Credits:** if any Kimikozi code is ported (it's MIT), keep its notice in `THIRD_PARTY.md`.
- **Repo:** private GitHub repo `MrAmadis/enshrouded-save-editor` (owner's standing rule).
- **Workflow** (owner's standing rules):
  - work on feature branches, merged back to main when done;
  - the owner tests locally before anything is pushed.
- **Distribution:** for the owner's personal use, built on the owner's machine. There's no public release, release pipeline or code signing. *(my call, as in Stoneshard)*

## 10. Risks and unknowns to settle during the build

Every item below is verified **in the real game**: edit the save, load it in Enshrouded, check the result.

**Owner's rule (2026-10-25): never change any existing save file. Only create a copy of a character save, and modify that copy.**
- This applies to every file in `remote\` (characters, worlds, `global.ksd`, `user_settings.json`, `backup\`) and to `remotecache.vdf`.
- The builder (human or agent) never opens a real save for writing, never overwrites one, and never deletes one.
- Automated tests and the Lab tool work on copies outside the Steam folder.
- **In-game checks run on a copied character:**
  - copy one real `characterN.ksd` to the next free number **in the local saves folder** (`Saved Games\keen games\Enshrouded\default\`, after the game-made local character), not in `remote\`. See the session A finding below: copies in `remote\` are invisible to the game;
  - write experiments only into that copy;
  - nothing in `remote\` is touched.
- The copy needs its own identity so the game doesn't confuse it with the original. Give it a new GUID in both the header and `character_data`, and a recognisable name such as "Amadis Test".
- **Session A finding (2026-10-25):** the game never sees copies placed in the Steam folder. It lists Steam characters through Steam's own catalogue (`remotecache.vdf`), which only knows files the game wrote, so a copy there is invisible. In-game checks therefore use **local copies**: the owner creates one local character in the game (storage tag `a8 23 b2 28`), and the Lab tool writes the experiment copies into the local folder (`Saved Games\keen games\Enshrouded\default\`, numbered after the game-made character), taking the storage tag from that game-made character. The Steam folder is not written at all.
- The first in-game session confirms that the game lists the copy. If it doesn't, stop and ask the owner. Don't fall back to writing anything else.
- The Lab tool refuses any write whose target already exists and isn't a copy it created itself. The finished editor's own **Save changes** is the only code path that may replace a real character file. It is used only by the owner, after the checks pass.

**How in-game checks work** *(my call, modelled on Stoneshard)*:
- The test characters are local, so Steam Cloud never sees them. They can be deleted in-game when the checks are done. (The six copies session A first wrote into `remote\` were never listed by the game, and are left in place unused until the owner decides on a cleanup.)
- Give the owner a short numbered checklist per session, and batch experiments so sessions are few. Work that doesn't depend on an answer (the container, the UI, game data) proceeds in parallel.
- Results are recorded in `docs/research/in-game-findings.md`. Items marked **Settled** below were answered in session A (2026-10-25).

1. **Round trip:** open and save with no edits gives a file the game loads identically. Then one changed block recompressed with real zstd. **Settled:** "Test Plain" (identity only) and "Test Repack" (every section recompressed with zstd level 3, new revision and time) both load as written.
2. **Steam Cloud:**
   - Does an edited save survive the next game launch, with Steam closed and with Steam open?
   - Is it uploaded rather than replaced by the cloud copy, and does Steam raise a conflict?
   - Does the game ever fall back to its own `remote\backup\` copies?
   - The editor never edits `remotecache.vdf` or the game's `remote\backup\` files. *(my call)*
   - If Steam must be closed while saving, the editor checks for `steam.exe` and says so before writing.
   - **Still open:** every in-game check so far ran on local characters, which Steam Cloud never sees. Testing an edited Steam character needs the owner's own Save on one, or a game-made Steam throwaway (owner to decide).
   - **If edits are still lost with Steam closed,** the owner turns Steam Cloud off for Enshrouded (owner). The game then keeps saves in `%USERPROFILE%\Saved Games\Enshrouded\`, which the editor already supports (§5). Check that the game moves or copies the characters there, and that the editor finds them.
3. **Inventory map:** the equipped slot numbers (is 0–4 appearance and 5–9 worn armour?), the hotbar's location, how the backpack size follows the equipped backpack, and the Inventory and Extra slots containers. **Settled:** the slot map in §6.4, and the backpack size in §7. **Still open (session B):** when the game sets or clears the Equipped header's byte 2 (mask bits 16–18) as gear goes on and off. Until then the editor only swaps equipped items, and doesn't equip into empty slots or unequip.
4. **New items:**
   - Which optional fields does the game need on a fresh weapon or armour piece (level, durability, rarity, bits 2 and 3)? Compare with items the game itself created. **Settled:** the game kept the editor's field sets for a bow, an armour piece and a ring.
   - Does a stack above its max get clamped? The UI caps it anyway. **Settled:** yes, the game clamped Fireball I 600 to 500.
   - Does gear added straight to an equipped slot apply its stats? (The editor adds new items to the backpack only; they are equipped by a swap.) **Not applicable:** the editor never adds into Equipped.
   - **Item levels, settled (session B):** the game sets the level of fixed-level gear (min level = max level) to its base plus upgrades and rewrites any other value; ranged items keep the written level. The editor follows this (§8.4), and the final walkthrough's Bow Of The Serpent, added at its base level, was kept. **No level goes past 55** (leftover checks): the game rewrote gems written at 80 and 105 to 55, though their data range is 1–105.
   - **Lore shields and wards, settled (leftover checks):** the game keeps both in the shield position.
5. **Skills:**
   - What do rank-0 entries mean? **Settled:** not learned, and not connected in the tree.
   - Does the game check paths or points on load? **Points, settled:** it resets an overspent tree (below). Paths weren't tested apart.
   - What happens when more points are spent than earned (Unlimited mode)? If the game resets, clamps or rejects such a tree, **drop Unlimited** and keep Game rules only (owner). Don't add points by writing reward ids into `player_knowledge`; the owner declined that. **Settled:** on load the game reset the whole tree, so **Unlimited is dropped**.
   - Does a learned passive's bonus apply, or does the game need extra state? **Settled:** passives learned by the editor show on the stats, and the points left match (69 − 13 = 56). A node the owner learned in-game was appended at rank 1, exactly as the editor does it.
   - Do Refund all and a rebuild hold after a reload? **Settled (final walkthrough):** yes, the rebuilt tree loaded and was kept.
6. **Level and XP:**
   - What does the XP value mean, what is the level cap, and where is the XP table in game data?
   - Does the game recompute level from XP?
   - Does a level change keep the points maths in §6.6? **Settled:** "Test Repack" at level 9 showed 4 points available, as §6.6 gives. XP 0 shows an empty bar, which fits XP counting into the current level (still to confirm with a non-zero value).
   - **Still open:** the install reader confirms the level cap of 50, but the install holds no XP-per-level table, so XP stays a raw number without a progress bar.
7. **Name:** is any length accepted, and what is the creator's limit? (Storage elsewhere is ruled out, §6.3.) **Settled:** the game displays a 40-character name, and the creator accepts 15. The editor caps names at 15 (§8.3).
8. **Game updates:** if `SV2C` isn't version 2, or a section's data version differs from those in §6.2, or a checksum fails, or a record layout isn't recognised, open read-only with a clear message. Never write in that state. **Built and tested.** Game data that can't be read from a patched install falls back to the reference data, and the character list says which source is in use.
9. **Icons:** find and decode item and skill icon textures (§7). **Settled (Task 20):** item icons are BC7 textures under each item's GUID (4,158 of 4,262 items); the 43 ability nodes have icons; everything else keeps its category glyph.

## 11. Success looks like

- Double-click the `.exe` and see the three Amadis characters, told apart by level and last-played time. The first launch may show a short "reading game files" progress bar.
- Open `character1` (level 18, 69 unspent points), then:
  - add materials from the Materials category and change the stack right after;
  - change the Copper Arrow stack from 88 to 500;
  - add a bow at a chosen level and rarity, and equip a ring;
  - in Skills, zoom into Ranger, spend points, Refund all, and rebuild;
  - change level, XP and name;
  - save.
- Load it in Enshrouded: every change is there, nothing else changed, and Steam Cloud keeps the edit.
- Opening a character and saving with no edits gives a save that plays identically.

## 12. Left to the builder

- Project internals, libraries, state management (a service the components call, as in Stoneshard), and component design.
- Styling details beyond the mockups, keeping the Enshrouded look.
- How the kfc format is read (§7 licence note) and the cache format.
- **Test strategy.**
  - Automated tests run on copies of the owner's saves, kept in `tests/fixtures/real/` (git-ignored).
  - Golden round-trip tests are strongly suggested: parse, then write unchanged, and the bytes must be identical.
  - The only writes into the live saves folder go to the test character (§10).
- The exact path-connectivity algorithm, and how dependents are found on unlearn.
