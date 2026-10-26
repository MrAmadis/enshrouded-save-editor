# To do

## 1. Remaining plan work

- **In-game checks left over from the final walkthrough** (the install's data changed them): see "Left over from the final walkthrough (Task 21)" in `docs/research/in-game-findings.md`.
- **Session B in-game checks** (see `docs/research/in-game-findings.md`, "For session B"):
  - how the game sets equipped header byte 2;
  - then enabling equip into an empty slot, and unequip.
- **Steam characters:** check that an edited Steam character survives Steam Cloud (owner to decide how). Then decide whether saving needs Steam closed (spec §10.2).
- **Later: editing upgrades** (the rarity field's second byte), once their rules are known: the maximum, and how rarity changes. See "Upgrades on ranged items" in `docs/research/in-game-findings.md`.

## 2. Cleanup

- **Six unused Lab copies in the Steam folder** (`remote\character3–8.ksd`, invisible to the game): delete them with the owner's OK.
- **Local test characters** (`keen games\Enshrouded\default\character1–8.ksd`: "Test …", "Try Me" and "Final Check"): delete them in-game when done.
- **In-game checks:** always use a new throwaway world. Session A's test characters were played in `world0`, and the game saved that world.
