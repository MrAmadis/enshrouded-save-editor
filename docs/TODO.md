# To do

## 1. Remaining plan work

- **In-game checks still left over from the final walkthrough** (icons after a game patch, the first launch after a reboot, a Steam update while the editor is open): see "Left over from the final walkthrough (Task 21)" in `docs/research/in-game-findings.md`.
- **Session B in-game checks** (see `docs/research/in-game-findings.md`, "For session B"):
  - how the game sets equipped header byte 2;
  - then enabling equip into an empty slot, and unequip.
- **Steam characters:** check that an edited Steam character survives Steam Cloud (owner to decide how). Then decide whether saving needs Steam closed (spec §10.2).
- **Later: editing upgrades** (the rarity field's second byte), once their rules are known: the maximum, and how rarity changes. See "Upgrades on ranged items" in `docs/research/in-game-findings.md`.

## 2. Cleanup

- **The test character "Leftover Checks"** (`keen games\Enshrouded\default\character1.ksd`): delete it in-game when testing is done. Every earlier test character, and the six unused copies in the Steam folder, were removed on 2026-10-26 (to the Recycle Bin).
- **In-game checks:** always use a new throwaway world. Test characters were played in Steam `world0` (session A, and "Final Check"), and the game saved that world.
