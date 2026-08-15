# HKSaveBackup

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

**Manual DLL drop:** copy the `HKSaveBackup` folder (containing `HKSaveBackup.dll` and
`HKSaveBackup.Core.dll`) into:

```
<Steam>\steamapps\common\Hollow Knight\hollow_knight_Data\Managed\Mods\HKSaveBackup\
```

Launch the game; `HKSaveBackup` appears in the top-left mod list on the title screen.

## Using it

**Backups are automatic.** Rest at a bench, hit a story save, or quit to menu on a Steel
Soul run and a timestamped backup pair appears in the backup folder (default:
`Documents\HKSaveBackup\slotN\`):

```
user1_20260814-193042_Crossroads_19.dat    <- byte-for-byte copy of the save
user1_20260814-193042_Crossroads_19.json   <- metadata: scene, completion %, geo, playtime
```

The scene name in the filename is deliberate: when you want "the backup from before I
walked into the Colosseum", you can find it by eye.

**Restoring:** from the title screen, open *Options → Mods → HKSaveBackup → Save Manager*,
pick *Restore Slot N*, choose a backup (newest first, with completion/geo/scene shown), and
confirm. The save-select screen refreshes immediately — no game restart needed to see the
restored save. Restore is refused while a save is loaded, because the game would re-save
the in-memory state over your restored file.

### The Steam Cloud caveat (read this once)

Hollow Knight's saves live in a folder Steam Cloud syncs. After a Steel Soul death the
dead save syncs to the cloud, and a restore can race against it. The mod stores backups
**outside** the synced folder (that's why the default is `Documents\HKSaveBackup`), but the
restored file itself is subject to sync. After restoring:

1. **Fully exit** Hollow Knight, then relaunch and load the save.
2. If the restore didn't stick: quit, right-click Hollow Knight in Steam → *Properties* →
   disable *Steam Cloud*, restore again in-game, quit, launch once (local file now wins),
   then re-enable Steam Cloud.

The mod cannot control Steam; it can only warn — the same text is shown on the in-game
confirmation screen.

## Configuration

Settings are editable in-game (*Options → Mods → HKSaveBackup*) and persisted to
`%APPDATA%\..\LocalLow\Team Cherry\Hollow Knight\HKSaveBackupMod.GlobalSettings.json`:

| Setting | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `true` | Master switch for taking backups (restore always works). |
| `BackupDirectory` | `""` | Backup location; empty means `Documents\HKSaveBackup`. Environment variables are expanded. Keep it **outside** the game's save folder — that folder is Steam Cloud-synced. |
| `MaxBackupsPerSlot` | `20` | Ring buffer size per slot; oldest backup pair is pruned first. |
| `CooldownMinutes` | `0` | Minimum minutes between backups. `0` = every save. Saves are already naturally spaced (bench/story/quit), so this is an escape hatch, not a default. |
| `BackupNormalSaves` | `false` | Also back up non-Steel-Soul saves. |
| `BackupOnQuitOnly` | `false` | Only back up the save the game commits when you *Return to Menu*; bench, story and autosave commits are skipped. See the warning below. |
| `SlotEnabled` | `[true, true, true, true]` | Per-save-slot switch for automatic backups (index 0 = slot 1). Restore is unaffected — a disabled slot still lists and restores its existing backups. |

*Options → Mods → HKSaveBackup* opens the settings; the restore surface is one step deeper,
under *Save Manager*. Settings are safe to change from the pause menu; restoring is only
offered at the main menu.

**About `BackupOnQuitOnly`:** it does what it says — the only save it keeps is the one
`GameManager.ReturnToMainMenu` commits on the way out of gameplay. Quitting the game
outright (or a crash, or a power cut) writes no save at all in vanilla Hollow Knight, so it
produces no backup either. That makes this a "fewest possible backups" mode, not a safer
one: on a Steel Soul run your newest backup can be hours behind the death it is meant to
undo. Leave it off unless you specifically want that trade.

`BackupDirectory` has no in-game editor (no text input in the menu system) — edit the JSON
while the game is closed.

Every backup, skip (with reason), prune, and restore is logged to `ModLog.txt` in the saves
folder — if you ever wonder whether the mod is protecting you, the log is the evidence.

## What it deliberately does not do

- No savestates or mid-run rewind — backups happen at the game's own save commits only.
- No save editing: files are copied byte-for-byte, never parsed or rewritten.
- No cloud uploads, network calls, or telemetry. Local disk only.
- The mod also carries each save's `userN.modded.json` (the Modding API's per-save mod
  data, e.g. Benchwarp's unlocked benches) alongside the backup, so restoring doesn't
  desync other mods.

## Building from source

```
git clone https://github.com/ASThome00/HKSaveBackup
cd HKSaveBackup
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

Run the tests with `dotnet test`. The backup/retention/naming/policy logic is pure and
fully covered without the game; hook and menu code is exercised in-game (see
[docs/manual-test-script.md](docs/manual-test-script.md)).

`scripts\package.ps1` builds a release zip and prints its SHA256 for the modlinks manifest.
