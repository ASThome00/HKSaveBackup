# Roadmap

## v1.0 — current

Automatic post-commit backups, ring retention, sidecar metadata, modded.json companion,
restore flow with pre-restore snapshot and Steam Cloud warning, mod-menu UI.

In-game verification status ([docs/manual-test-script.md](docs/manual-test-script.md)):
sections A (mod loads) and B (Steel Soul backup) verified; C–H pending.

## Planned

### 1. Save manager on the main menu
A dedicated button on the title screen (alongside Start Game / Options) opening the save
manager directly, instead of the Options → Mods → HKSaveBackup path. Requires injecting a
MenuButton into UIManager's main menu screen and its navigation graph — the mod-menu route
stays as a fallback.

### 2. Dedicated in-game mod settings
Split settings out of the save-manager tree into their own screen so the pause-menu path
lands on settings (which are safe anywhere) and the save manager remains a main-menu-only
surface. Candidate additions: per-slot enable, backup-on-quit-only mode.

### 3. Load a save straight from the restore flow
After a successful restore, offer "Load this save now" — drive the save-select flow into
the restored slot (requeue GameManager.LoadGame path) instead of dropping the player back
to navigate save select manually. Also covers "reload": boot the current slot back to its
latest backup in one action.

### 4. Death screen: salvage or let it die
On a Steel Soul death, before the shatter sequence commits, present a choice: salvage the
run (restore the latest backup and reload) or let it die (vanilla behavior).

**Design note:** this deliberately crosses v1's gameplay-inert line — it intercepts the
death sequence (`HeroController` death coroutine flips permadeathMode 1→2, then
`GameManager.PlayerDead` saves and loads the PermaDeath scene). Doing it without PlayMaker
FSM edits means hooking the C# path around that flip/save, and the restore-while-loaded
rule still applies: salvage must happen before the death save commits, or route through a
forced quit-to-menu + restore + reload. Needs a design pass before implementation; ships
default-off if it ships at all, so the mod stays honest for players who want vanilla
permadeath stakes.
