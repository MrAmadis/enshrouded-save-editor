# To do

## 1. Remaining plan work

- **In-game checks still left over from the final walkthrough** (icons after a game patch, the first launch after a reboot, a Steam update while the editor is open): see "Left over from the final walkthrough (Task 21)" in `docs/research/in-game-findings.md`.
- **Session B in-game checks** (see `docs/research/in-game-findings.md`, "For session B"):
  - how the game sets equipped header byte 2;
  - then enabling equip into an empty slot, and unequip.
- **Steam characters:** check that an edited Steam character survives Steam Cloud (owner to decide how). Then decide whether saving needs Steam closed (spec §10.2).
- **Clear fog of war:** works in-game (owner, 2026-10-27). Not reported yet: whether Midhollow, Fox Chase and the Ancient Spires show without being visited (map spec `2026-10-26-map-fog-and-locations-design.md` §8, check 2).
- **Discover all locations:** works in-game (owner, 2026-10-27). Still open (map spec §8): whether discovered places show under the fog (check 2), and whether visiting one later still gives discovery XP (check 6). **The Ancient Spires** aren't discovered by it: unlocking them for fast travel would need research into how the game records an activated spire (map spec §6.3, §9).
- **Later: editing upgrades** (the rarity field's second byte), once their rules are known: the maximum, and how rarity changes. See "Upgrades on ranged items" in `docs/research/in-game-findings.md`.

## 2. Cleanup

- **The test character "Places Test"** (`keen games\Enshrouded\default\character3.ksd`, a Lab copy of Steam character2): delete it in-game when testing is done.
- **The test character "Fog Test"** (`keen games\Enshrouded\default\character2.ksd`, a Lab copy of Steam character2): delete it in-game when testing is done.
- **The test character "Leftover Checks"** (`keen games\Enshrouded\default\character1.ksd`): delete it in-game when testing is done. Every earlier test character, and the six unused copies in the Steam folder, were removed on 2026-10-26 (to the Recycle Bin).
- **In-game checks:** always use a new throwaway world. Test characters were played in Steam `world0` (session A, and "Final Check"), and the game saved that world.
