# Tool Assisted Steelsoul (TAS)

Automatic save-file backups and in-game restore for **Hollow Knight 1.5.x** — insurance for
Steel Soul (permadeath) runs. A death no longer destroys hours of progress: the mod keeps a
rolling set of backups taken every time the game itself saves, and lets you restore one from
the main menu without leaving the game.

- **Gameplay-inert.** No gameplay changes of any kind; the mod copies save files at the
  moment the game finishes writing them, and never modifies or parses their contents.
- **Steel Soul focused.** By default only Steel Soul saves are backed up (normal saves can
  be enabled in the menu). The post-death "shattered" save is deliberately never backed up.
- **Restores never lose data.** Before a restore overwrites a slot, the slot's current
  contents are snapshotted into the same backup folder.

## Requirements

- Hollow Knight **1.5.x** on **Windows (Steam)**
- The [Hollow Knight Modding API](https://github.com/hk-modding/api) (installed
  automatically by [Lumafly](https://themulhima.github.io/Lumafly/))

Not for Silksong — Silksong uses a different loader (BepInEx) and a different mod ecosystem.

## Install

**With Lumafly (manual install):** open Lumafly, use *Manual Install* and select the
release zip, or drop the mod folder produced below into place.

**Manual DLL drop:** copy the `ToolAssistedSteelsoul` folder (containing `ToolAssistedSteelsoul.dll` and
`ToolAssistedSteelsoul.Core.dll`) into:

```
<Steam>\steamapps\common\Hollow Knight\hollow_knight_Data\Managed\Mods\ToolAssistedSteelsoul\
```

Launch the game; `Tool Assisted Steelsoul` appears in the top-left mod list on the title screen.

## Using it

**Backups are automatic.** Rest at a bench, hit a story save, or quit to menu on a Steel
Soul run and a timestamped backup pair appears in the backup folder (default:
`Documents\ToolAssistedSteelsoul\slotN\`):

```
user1_20260814-193042_Crossroads_19.dat          <- byte-for-byte copy of the save
user1_20260814-193042_Crossroads_19.json         <- metadata: scene, completion %, geo, playtime
user1_20260814-193042_Crossroads_19.modded.json  <- the Modding API's per-save mod data, if present
```

(Timestamps in filenames are UTC; the in-game menu shows your local time.) The scene name
in the filename is deliberate: when you want "the backup from before I walked into the
Colosseum", you can find it by eye. The `.modded.json` companion is other mods' per-save
data (e.g. Benchwarp's unlocked benches), carried along so a restore doesn't desync them.

**Restoring:** from the title screen, press the *Save Backups* button (added right above
*Quit*), or go the long way via *Options → Mods → Tool Assisted Steelsoul → Save Manager*.
Pick *Restore Slot N*, choose a backup (newest first, with completion/geo/scene shown), and
confirm. The top entry, *Restore Latest & Load*, rolls the slot back to its newest real
backup and starts playing it in one action; after any plain restore a *Load This Save Now*
button does the same. The save-select screen refreshes immediately — no game restart needed
to see the restored save. Restore is refused while a save is loaded, because the game would
re-save the in-memory state over your restored file.

### The Steam Cloud caveat (read this once)

Hollow Knight's saves live in a folder Steam Cloud syncs. After a Steel Soul death the
dead save syncs to the cloud, and a restore can race against it. The mod stores backups
**outside** the synced folder (that's why the default is `Documents\ToolAssistedSteelsoul`), but the
restored file itself is subject to sync. After restoring:

1. **Fully exit** Hollow Knight, then relaunch and load the save.
2. If the restore didn't stick: quit, right-click Hollow Knight in Steam → *Properties* →
   disable *Steam Cloud*, restore again in-game, quit, launch once (local file now wins),
   then re-enable Steam Cloud.

The mod cannot control Steam; it can only warn — the same text is shown on the in-game
confirmation screen.

## Configuration

Settings are editable in-game (*Options → Mods → Tool Assisted Steelsoul*) and persisted to
`%APPDATA%\..\LocalLow\Team Cherry\Hollow Knight\ToolAssistedSteelsoulMod.GlobalSettings.json`:

| Setting | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `true` | Master switch for taking backups (restore always works). |
| `BackupDirectory` | `""` | Backup location; empty means `Documents\ToolAssistedSteelsoul`. Environment variables are expanded. Keep it **outside** the game's save folder — that folder is Steam Cloud-synced. |
| `MaxBackupsPerSlot` | `20` | Ring buffer size per slot; oldest backup pair is pruned first. |
| `CooldownMinutes` | `0` | Minimum minutes between backups. `0` = every save. Saves are already naturally spaced (bench/story/quit), so this is an escape hatch, not a default. |
| `BackupNormalSaves` | `false` | Also back up non-Steel-Soul saves. |
| `BackupOnQuitOnly` | `false` | Only back up the save the game commits when you *Return to Menu*; bench, story and autosave commits are skipped. See the warning below. |
| `SlotEnabled` | `[true, true, true, true]` | Per-save-slot switch for automatic backups (index 0 = slot 1). Restore is unaffected — a disabled slot still lists and restores its existing backups. |
| `DeathSalvagePrompt` | `false` | **Off by default.** On a Steel Soul death, pause before the death is committed and offer *salvage the run* / *let it die*. See below. |
| `DeathSalvagePromptSeconds` | `20` | How long that prompt waits before choosing "let it die" on its own. Clamped to 5–120. |

*Options → Mods → Tool Assisted Steelsoul* opens the settings; the restore surface is one step deeper,
under *Save Manager* (or directly via the title screen's *Save Backups* button). Settings
are safe to change from the pause menu; restoring is only offered at the main menu.

`BackupDirectory` has no in-game editor (no text input in the menu system) — edit the JSON
while the game is closed.

Every backup, skip (with reason), prune, and restore is logged to `ModLog.txt` in the saves
folder — if you ever wonder whether the mod is protecting you, the log is the evidence.

**About `BackupOnQuitOnly`:** it does what it says — the only save it keeps is the one
`GameManager.ReturnToMainMenu` commits on the way out of gameplay. Quitting the game
outright (or a crash, or a power cut) writes no save at all in vanilla Hollow Knight, so it
produces no backup either. That makes this a "fewest possible backups" mode, not a safer
one: on a Steel Soul run your newest backup can be hours behind the death it is meant to
undo. Leave it off unless you specifically want that trade.

### Death salvage (opt-in)

Everything else in this mod is gameplay-inert: it copies files the game has already
written. `DeathSalvagePrompt` is the one setting that is not, which is why it ships off.

With it on, a Steel Soul death stops at `GameManager.PlayerDead` — *before* the game writes
the death save — and asks:

- **Salvage** (`Y` / `Enter` / gamepad A): the death save never happens, the game quits to
  the main menu **without saving**, and your save slot is left holding the last save it
  already had. In the normal case not a single file is written; a backup is only copied back
  if the slot file is missing or has fallen behind the backup store.
- **Let it die** (`N` / `Esc` / gamepad B, or the timeout): the vanilla death sequence runs
  untouched — death save, shatter, PermaDeath scene.

Salvage means "rewind to your last save", so it costs whatever you did since your last
bench/quit save. It never resurrects the run in place, and it never edits a save file.

## What it deliberately does not do

- No savestates or mid-run rewind — backups happen at the game's own save commits only.
- No save editing: files are copied byte-for-byte, never parsed or rewritten.
- No cloud uploads, network calls, or telemetry. Local disk only.

## Building from source

```
git clone https://github.com/ASThome00/ToolAssistedSteelsoul
cd ToolAssistedSteelsoul
dotnet build -c Release -p:HollowKnightRefs="C:\Program Files (x86)\Steam\steamapps\common\Hollow Knight\hollow_knight_Data\Managed"
```

`HollowKnightRefs` must point at a Hollow Knight 1.5.x `Managed` folder with the Modding
API installed (it provides `MMHOOK_Assembly-CSharp.dll`). Instead of passing the property
each time you can create a `local.props` next to the solution (gitignored):

```xml
<Project>
  <PropertyGroup>
    <HollowKnightRefs>G:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight_Data\Managed</HollowKnightRefs>
    <InstallToGame>true</InstallToGame> <!-- optional: copy the mod into the game on build -->
  </PropertyGroup>
</Project>
```

Run the tests with `dotnet test src/ToolAssistedSteelsoul.Tests` — they need no game
install. The backup/retention/naming/policy logic is pure and fully covered without the
game; hook and menu code is exercised in-game (see
[docs/manual-test-script.md](docs/manual-test-script.md)).

`scripts\package.ps1` builds a release zip and prints its SHA256 for the modlinks manifest.
