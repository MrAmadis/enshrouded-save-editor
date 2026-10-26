# The kfc format: how the editor reads game data from the install

The editor reads its items, skill tree, skill-point rewards and level cap from the Enshrouded install at runtime
(`src/EnshroudedSaveEditor.GameData/Kfc/`). This note describes the file format as we read it, the mapping onto the
editor's model, and how the result compares with the embedded reference data.

**Credit.** The format was learned by studying [Brabb3l/kfc-parser](https://github.com/Brabb3l/kfc-parser) (GPL-3.0,
commit `16708594`, 2026-10-17): its container and reflection readers and its format notes. No code was copied; the C#
reader was written from the description below, and checked against the files of this machine's install. Everything is
read-only: files are opened with `FileAccess.Read, FileShare.ReadWrite`, and nothing is written under the install.

Install used: game build `1273043|^/game38/branches/release_1.0|2026-10-20T14:55:29Z` (the version string in
`enshrouded.kfc`; file times 2026-10-21).

## Files

| File | What it holds |
| --- | --- |
| `enshrouded.kfc` (9.4 MB) | The index: header, resource ids and offsets, content hashes and locations, chunk table. |
| `enshrouded.kfc_resources` (215 MB) | Typed **resources**, zstd-compressed in chunks of 8 MiB (481 MB uncompressed). |
| `enshrouded_000.dat` … `_054.dat` | Untyped **content** blobs (textures, sound, the localisation tables …), stored raw. |
| `enshrouded.exe` | The **type metadata** (reflection tables) that describes every resource type's layout. |

All numbers are little-endian.

## The index: `enshrouded.kfc`

The header starts with the magic `KFC4`, the header size, the value 12 and four zero bytes. From offset `0x10` it is a
list of *locations*. A location is 8 bytes: a 32-bit offset **relative to the location field itself** (add the field's
file offset) and a 32-bit count.

| Offset | Points to | Entry |
| --- | --- | --- |
| `0x10` | version string | `count` ASCII bytes, no terminator |
| `0x18` | containers (`.dat` files) | 16 B: file size (u64), number of blobs (u64) |
| `0x20`, `0x28` | unused | — |
| `0x30` | resource locations | 12 B: uncompressed size, compressed size, count (one entry) |
| `0x38` | resource indices | u32 (index into the resource keys, grouped by type) |
| `0x40` / `0x48` / `0x50` | content hash map: buckets / keys / values | key 16 B (content hash); value 16 B: offset (u32), flags (u16), container index (u16), 8 zero bytes |
| `0x58` / `0x60` / `0x68` | resource map: buckets / keys / values | key 32 B: GUID (16), type hash (u32), part (u32), two reserved u32; value 8 B: offset, size in the uncompressed resource data |
| `0x70` / `0x78` / `0x80` | resource bundles: buckets / keys / values | per type: internal type hash, first index, count |
| `0x88` | resource chunks | 20 B: file offset in `kfc_resources`, stored size (4 KiB aligned), compressed size, uncompressed offset, uncompressed size |

The three maps are static hash maps (power-of-two bucket arrays over parallel key and value arrays). The editor doesn't
use the buckets: it reads every key and value into a dictionary, which takes about 50 ms for this build's 91,812
resources (146 types) and 101,459 content blobs.

**Resources.** A resource's bytes are `size` bytes from `offset` in the concatenated uncompressed chunks. Find the chunk
whose uncompressed range holds the offset, read `compressed size` bytes at the chunk's file offset, unwrap them with zstd,
and copy (a resource may continue into the next chunk). The reader keeps the last four decompressed chunks. Not handled:
sizes are stored in 24 bits, so a resource of 16 MiB or more has a truncated size (none of the resources we read is near
that).

**Resource ids.** The GUID identifies the resource; the type hash is the *qualified hash* of its type (below). Several
resources of different types can share a GUID (an item's `ItemInfo` and its entity template do), so a reference is
resolved by GUID **and** the referenced type, falling back to any resource with that GUID for subtypes.

**Content.** A content hash is 16 bytes: the blob's size (u32) followed by three u32 hash words. Look the 16 bytes up in
the content map; read `size` bytes at `offset` in `enshrouded_{container:000}.dat`.

## Type metadata: `enshrouded.exe`

Resources carry no field names; their layout is in the executable's reflection table (12,646 types in this build). The
executable is a 64-bit PE file; pointers in it are virtual addresses, mapped to file offsets through the section table.

**Finding the table.** In `.rdata` (and after it), find the zero-delimited strings `BlobString` and `uint32`. A type
record starts with a pointer to its name followed by the name's length, so the type records for those two types are the
8-byte-aligned places holding the string's address followed by its length. The table of type pointers is where a pointer
to the `BlobString` record is directly followed by a pointer to the `uint32` record (they are the first two types).
Somewhere else a (pointer to that table, count) pair points at it; that gives the table and its length.

**A type record** (0x90 bytes):

| Offset | Field |
| --- | --- |
| `0x00` | name (pointer, length) — e.g. `ItemInfo` |
| `0x10` | impact name (pointer, length) |
| `0x20` | qualified name (pointer, length) — e.g. `keen::ItemInfo` |
| `0x30` | namespace (pointer to a chain of name records) |
| `0x38` | inner type (pointer): base struct, aliased type, element type, enum storage or referenced type |
| `0x40` | size (u32); `0x44` alignment (u16); `0x46` element alignment (u16) |
| `0x48` | field count (u32): struct fields, enum values, or static array length |
| `0x4C` | primitive kind (u8, below); `0x4D` flags (u8) |
| `0x50` | qualified hash (u32) = FNV-1a (32-bit) of the qualified name — the type hash in resource ids |
| `0x54` | internal hash (u32) — the key of the resource bundles |
| `0x58` | struct fields (pointer to `count` records of 0x30 bytes: name (pointer, length), type pointer, byte offset (u64), attributes (pointer, count)) |
| `0x60` | enum values (pointer to `count` records of 0x28 bytes: name (pointer, length), value (u64), 16 zero bytes) |
| `0x68` | variant type; `0x70` default value (pointer, length); `0x80` attributes (pointer, count) |

Primitive kinds: 0 none, 1 bool, 2–9 u8/s8/u16/s16/u32/s32/u64/s64, 10 f32, 11 f64, 12 enum, 13–16 bitmask 8/16/32/64,
17 typedef, 18 struct, 19 static array, 20–23 `ds` containers (not in resources), 24 blob array, 25 blob string, 26 blob
optional, 27 blob variant, 28 object reference, 29 GUID.

## Values

- A **struct** is its fields at their recorded byte offsets; a struct with an inner type inherits that base struct's
  fields (at their own offsets). A **typedef** is read as its inner type. An **enum** or **bitmask** is an integer of the
  type's size; enum names come from the enum values. A **static array** is `count` elements inline.
- **Blob** values live after the fixed-size part. A blob array is (u32 offset, u32 count), a blob string (u32 offset, u32
  byte length, UTF-8), a blob optional (u32 offset, 0 = empty); each offset is **relative to the offset field itself**. A
  blob variant is (u32 qualified hash of the stored type, u32 relative offset, u32 size); the value is at the offset
  field's position plus the offset.
- An **object reference** is the 16-byte GUID of another resource (the reference type's inner type is the target type).
  A **GUID** field is 16 bytes; a content reference stores the 16-byte content hash.

## Localisation

`keen::LocaTagCollectionResource` names the English data (`languages[En_Us].data.hash`, else `keenglishData.hash`, the
developers' working English). That content blob is a `keen::LocaTagCollectionResourceData`: `tags` of (id, text,
arguments). A **loca tag reference** (an object reference to `keen::LocaTag`) has the tag id **FNV-1a of its 16 GUID
bytes**; the skill tree stores tag ids directly. Texts contain markup (`<lorename>…</lorename>`, `<b>`), which the editor
removes, and placeholders `%k` (one per argument, in order) and `%%` (a percent sign).

## Items

Every `keen::ItemInfo` resource (4,262, unique ids) becomes an `ItemInfo`, filled the way the reference fills it:

| Editor field | From |
| --- | --- |
| `Id` | `itemId.value` |
| `Name` | the `name` tag's text, else `debugName` (as the reference does for unnamed items) |
| `Category` | `category` by name (the enum's `PetFood` has no counterpart and no item uses it; it would read as Materials) |
| `BaseRarity` | `rarity` + 1 |
| `MaxStack` | `maxStackSize` |
| `MinLevel`, `MaxLevel` | `itemLevelRange`; a range ending at `BalancingTable.itemLevelCap` (55) ends at `playerLevelCap` (50) instead — the item's base level stops at the player cap and upgrades add the rest, exactly as in the reference (294 such items) |
| `Slot` | `equipment.slot` (`keen::EquipmentSlotType`): `Armour_Head/UpperBody/Arms/LowerBody/Feet/Ring` → Helmet/Chest/Arms/Legs/Boots/Ring, `Shield` → Shield, `Melee/MidRange/RangeWeapon` → Weapon; none for **vanity items** (`flags` bit 18, `IsVanityItem`), which the editor places by name |
| `WeaponType` | `weaponCategoryReference` looked up in `ItemRegistryResource.weaponCategories` (GUID → `categoryType` name) |
| `InternalName` | `debugName` |
| `Description` | the `description` tag's text, on one line |
| `Perks` | 1 when any `perkIds` entry is set, else 0 (the reference's 0/1 in 99.4 % of items) |
| `NoRarity` | `disableRarityGeneration` ≠ 0 (agrees with the reference in 90 %; the field isn't used by the editor) |

## Skill tree, rewards, level cap

- **Tree:** `keen::SkillTreeResource`: `nodes` (id, `uiPosition`, name and description tag ids, `costs`, `type`
  Root/Attribute/ActiveSkill/PassiveSkill → Spark/Attribute/Ability/Passive, `classAlignment`, `maxLevel` 0 or 3 → max
  rank 1 or 3) and `links` (source, target).
- **Skill text:** each `%k` takes the description tag's next argument. *Config*: the node's `configValues` entry with that
  `configId`; a scaled config (`Linear`) gives `value + scaleFactor × rank` for each rank, shown "10% / 20% / 30% (by
  level)"; `Percentage` values × 100 with "%", `Duration` in seconds (minutes when whole minutes). *Balancing*:
  `keen::BalancingLocaIds` (HealthPerAP, ManaPerAP, StaminaPerAP) → the `player…` field of the balancing table.
  *Input*: a `GameplayAction` (e.g. 15 ContextualAction), named by the skill screen's gameplay-action labels
  (`FbUiBundle.locatags.gameplayActions`, matched by field name, else the action's name in words). "(by level)" follows a
  percentage directly and another number's unit (the words of the node's `perLevelEffectDescription` between `%k` and
  "per level", else the next word). The node's per-rank values (`RankValues`) are those of its first scaling placeholder.
- **Archetype labels:** the skill screen's `classHints` (`FbUiBundle.menu.skilltree`): each label sits at
  `centerRingRadius + distanceFromRing` from the tree's centre (the middle of the Sparks) towards `directionDegrees`; its
  class is the class of the nearest class node; names are upper-cased like the reference's.
- **Rewards:** `keen::GameKnowledgeResource.skillPoints`: (knowledge id, amount). The install has no names or regions for
  them, so `Region` and `Name` are empty (the editor only uses ids and points).
- **Level cap:** `keen::BalancingTable.playerLevelCap` (50; `itemLevelCap` 55, `playerLevelMax` 100,
  `skillPointsPerLevel` 2).
- **XP per level: not in the install.** `BalancingTable.experienceNeedPerLevel` has only five values (1000, 2023, 3046,
  4069, 5092); the table for 49 levels isn't stored, so `XpPerLevel` stays unknown (from the reference: null).

## Match with the reference

The reference data (Kimikozi's `itemdb2.json`, `progress.json`) is labelled "build of 2026-10-15"; the install is the
2026-10-20 build. Measured on this machine:

| Piece | Result |
| --- | --- |
| Items present | 3,497 of the 3,526 reference ids (99.2 %). The 29 missing ones (e.g. Sharpshooter and Marksman armour, a `DEPRICATED` ring) are nowhere in the archive: a scan of every resource for three of their ids found nothing. The install has 765 items the reference lacks (e.g. T7 potions and materials). |
| Name, MaxStack, Category, MinLevel, MaxLevel all equal | 3,349 of the 3,497 present (95.8 %); 94.98 % of all 3,526 reference ids. Name 3,392, Category 3,496, MaxStack 3,497, MinLevel 3,441, MaxLevel 3,442. |
| Slot, WeaponType, BaseRarity | 3,413 of 3,497 (97.6 %); InternalName 3,283 (93.9 %). |
| Editing rules (`InventoryLayout.KindOf`, `FixedLevel`, `ExtraCapacity`, MaxStack, Category) | 3,480 of 3,497 (99.5 %). `KindOf` differs only for the 11 `Craft_Shield_*` lore shields: the install gives them the Shield slot, the reference none. `ExtraCapacity` agrees for every item. |
| Tree | 243 of 243 node ids, costs, max ranks, types, classes and positions (identical after rounding to 0.1, as the reference is); 312 of 312 links; names 240; descriptions 228 equal, every placeholder filled; 12 of 12 label names and classes (positions 50–400 units apart: the reference puts every label at about the same radius). |
| Rewards | 78 of 78 ids with equal points. |
| Saves | Every item and skill id in the three fixture characters is known (the reference misses six appearance items). |

The remaining differences are changes between the builds, not reading errors: renamed items ("Ghostly Shield" → "Ghostly
Ward", "Archer Gloves" → "Cutthroat Gloves"; the reference often has the old `keenglish` text), renamed internal names
(`Ammo_T6_Spell_MagicShield_lvl43` → `_lvl38`), rebalanced ammunition and spell levels, items the reference listed by
internal name that now have names (T7 materials), and items removed from the game.

## Cache and load time

The extracted pieces are cached as JSON in `<AppDataRoot>\cache\gamedata\install.json` (1.6 MB), keyed to the size and
last-write time of `enshrouded.kfc` and `enshrouded.exe` and to a format number in the code. A missing, stale or
malformed cache is rebuilt. A piece that can't be read is cached as missing, so the next start doesn't retry it until
the game (or the cache format) changes. On this machine (warm file cache) a cold load takes about 0.4 s and a cached
load about 0.09 s.
