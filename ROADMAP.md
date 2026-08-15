# Roadmap

## v1.0 — current

Automatic post-commit backups, ring retention, sidecar metadata, modded.json companion,
restore flow with pre-restore snapshot and Steam Cloud warning, mod-menu UI, title-screen
Save Backups button, load-after-restore, and the opt-in death-salvage prompt.

In-game verification status ([docs/manual-test-script.md](docs/manual-test-script.md)):
sections A (mod loads) and B (Steel Soul backup) verified; C–O pending.

## Features

### 1. Save manager on the main menu — done
A dedicated "Save Backups" button on the title screen (placed above Quit) opens the save
manager directly, with "back" returning to the main menu; the Options → Mods route stays.
Landed as `MainMenuButton.cs`: a `UIManager.EditMenus` injection into the vanilla button
column and its `MenuButtonList` navigation, fully guarded — any failure leaves the title
screen untouched. In-game verification pending.

### 2. Dedicated in-game mod settings — done
The mod's root screen is now the settings list, with the save manager (the restore surface)
one entry deeper, so the pause-menu path lands on settings and restore stays main-menu-only.
Added `SlotEnabled` (per-slot backup switch) and `BackupOnQuitOnly` (back up only the save
`GameManager.ReturnToMainMenu` commits). In-game verification: sections I–K of
[docs/manual-test-script.md](docs/manual-test-script.md), pending.

### 3. Load a save straight from the restore flow — done
After a successful restore the result screen offers "Load This Save Now", and the backup
list's top entry is "Restore Latest & Load" (reload the slot's newest non-snapshot backup
in one action). Landed as `SaveLoadService.cs`: re-reads the restored slot with
`GetSaveStatsForSlot`, applies the vanilla save-select guards (unreadable file, shattered
permadeathMode 2), fades the mod menu out, and hands off to `GameManager.LoadGameFromUI` —
every failure before the hand-off falls back to the result screen. In-game verification:
section L of [docs/manual-test-script.md](docs/manual-test-script.md), pending.

### 4. Death screen: salvage or let it die — *implemented, default-off*
On a Steel Soul death, before the shatter sequence commits, present a choice: salvage the
run or let it die (vanilla behavior). Behind `DeathSalvagePrompt` (off by default), because
it is the one feature that is not gameplay-inert. In-game verification pending (sections
M–O of [docs/manual-test-script.md](docs/manual-test-script.md)).

**How it landed:** an `On.GameManager.PlayerDead` hook, no PlayMaker FSM edits. The
interception sits between `HeroController.Die()`'s in-memory permadeathMode 1→2 flip and
`orig_PlayerDead`'s death save, which is the last moment the slot file on disk is still the
last good commit. "Let it die" simply runs the original enumerator. "Salvage" never invokes
it: save commits are latched off in the mod's own `SaveGame` hook, the game is sent to the
menu through `ReturnToMainMenu(DontSave)`, and the slot file is left exactly as it was —
zero writes in the common case. A backup is only restored (through the normal
`RestoreService`, once `Menu_Title` is reached, so the main-menu gate holds) when the slot
file is missing or has fallen behind the backup store; that choice is pure logic in
`Core/SalvagePolicy.cs`. Prompt UI is an IMGUI overlay with a timeout that defaults to
vanilla death, since the vanilla UI stack is mid-transition during a death.
