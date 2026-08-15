# Manual test script

In-game verification for the acceptance criteria that cannot be unit tested. Run with the
mod installed (`dotnet build` with `InstallToGame=true` in `local.props`, or copy the DLLs
manually). Keep `ModLog.txt` open in a tail viewer if you can:
`%APPDATA%\..\LocalLow\Team Cherry\Hollow Knight\ModLog.txt`.

Default backup folder: `Documents\HKSaveBackup\`.

## A. Mod loads (acceptance 1)

1. Launch Hollow Knight.
2. Title screen, top-left mod list: `HKSaveBackup 1.0.0.0` is listed.
3. `ModLog.txt` contains `[HKSaveBackup] - Initialized. Backup root: ...` and no
   `[HKSaveBackup]` errors.

## B. Steel Soul save produces a backup (acceptance 2)

1. Start a new **Steel Soul** game in an empty slot (slot N).
2. Play to the first bench (Dirtmouth) and rest.
3. Check `Documents\HKSaveBackup\slotN\`: exactly one `.dat` + `.json` pair exists
   (filename like `userN_<timestamp>_Town.dat`).
4. Open the `.json`: `slot` = N, `scene` = `Town`, `permadeathMode` = 1,
   `preRestoreSnapshot` = false.
5. `ModLog.txt` has a `Backed up slot N` line naming both paths.

## C. Normal save is skipped by default (acceptance 3)

1. Start or load a **normal** (non-Steel-Soul) save in another slot M.
2. Rest at any bench.
3. `Documents\HKSaveBackup\slotM\` does not exist or gained no new files.
4. `ModLog.txt` has: `Skipped backup of slot M: normal (non-Steel-Soul) save and
   BackupNormalSaves is off`.
5. (Optional) Enable *Backup Normal Saves* in the mod menu, rest again, confirm a backup
   appears and the setting round-trips after a game restart.

## D. Retention prunes oldest (acceptance 4, in-game half)

1. In the mod menu set *Backups Kept Per Slot* to 5.
2. On the Steel Soul save, rest at a bench, walk away and return, repeat until 6+ saves
   have been committed (quit-to-menu also counts as a save).
3. The slot folder holds exactly 5 pairs; the oldest timestamp is gone (both files).
4. `ModLog.txt` has `Pruned oldest backup of slot N: ...` lines.

## E. Death + restore recovers the run (acceptance 5)

1. On the Steel Soul save, note current completion %/geo, then die deliberately.
2. Observe the shattered-slot behavior; `ModLog.txt` shows
   `Skipped backup of slot N: Steel Soul run already dead (permadeathMode=2 death save)` —
   the death save must NOT appear in the backup folder.
3. Return to the title screen (do not close the game).
4. *Options → Mods → HKSaveBackup → Restore Slot N* → newest backup → read the
   confirmation text → *Restore Now*.
5. Back out to the save select: slot N shows the pre-death save (not shattered), with the
   bench location — **without restarting the game**.
6. Load it: you spawn at the backed-up bench with the noted completion %/geo.

## F. Restore is refused in-game + cloud warning shown (acceptance 6)

1. Load any save (get in-game).
2. Pause → *Options → Mods → HKSaveBackup → Restore Slot N*.
3. The screen states restore is only available from the main menu and offers no backup
   list.
4. Quit to menu, open the same screen: the backup list appears. Select one and verify the
   confirmation screen shows the Steam Cloud warning paragraph before any restore happens.

## G. Pre-restore snapshot exists (acceptance 7)

1. After the restore in E, list `Documents\HKSaveBackup\slotN\`: a pair named
   `userN_<timestamp>_prerestore.dat/.json` exists with `preRestoreSnapshot: true`.
2. Its `.dat` is byte-identical to the dead save that was just overwritten (size match is
   sufficient evidence).
3. The restore menu lists it as "pre-restore snapshot".

## H. Crash-safety spot check

1. With the game running, make the backup directory read-only (folder properties), rest at
   a bench on the Steel Soul save.
2. The game saves normally (no hang, no error dialog); `ModLog.txt` shows
   `Backup of slot N failed (game save is unaffected): ...`.
3. Restore the folder's permissions and confirm the next bench rest backs up again.

## I. Death salvage — toggle OFF (vanilla death is untouched)

Do this one first, and on a throwaway Steel Soul save.

1. Confirm *Options → Mods → HKSaveBackup → Death Salvage Prompt* reads **Off** (the
   default; `DeathSalvagePrompt: false` in `HKSaveBackupMod.GlobalSettings.json`).
2. Rest at a bench on a Steel Soul save (this is the save you will get back in test J), then
   die deliberately.
3. Expected: **no prompt, no overlay, no pause** — the death plays exactly as vanilla:
   shatter, fade, PermaDeath cutscene, save select shows the slot shattered.
4. `ModLog.txt` shows the usual
   `Skipped backup of slot N: Steel Soul run already dead (permadeathMode=2 death save)`
   and *no* death-salvage lines at all.
5. Restore the pre-death backup from the mod menu (test E) so you have a live Steel Soul run
   again for test J.

## J. Death salvage — toggle ON (salvage)

1. Turn *Death Salvage Prompt* **On**.
2. Rest at a bench, note completion %/geo and the bench, then walk somewhere else and die
   deliberately.
3. Expected: at the moment of death the sequence stops and a black overlay reads
   `STEEL SOUL DEATH`, names the save you would get back, and counts down.
4. Press `Y` (or gamepad A). The game fades out and returns to the **main menu** on its own,
   with a notice line at the top of the screen.
5. `ModLog.txt` should show, in order: `offering salvage [source=LiveSlotFile, ...]`,
   `save commits are latched off`, `quitting to the main menu without saving`,
   `complete: the death save was prevented and the slot file was left untouched`,
   `save commits re-enabled`. There must be **no** `Backed up slot N` line for the death and
   **no** `Skipped backup ... permadeathMode=2` line — the death save never happened.
6. Save select: the slot is **not** shattered. Load it — you are back at the noted bench
   with the noted completion %/geo, and the run is still Steel Soul (steel mask HUD).
7. Rest at a bench and confirm a new backup appears in `Documents\HKSaveBackup\slotN\` —
   proof the save-suppression latch was released.

## K. Death salvage — toggle ON (let it die)

1. With the toggle still on, die again.
2. At the prompt press `N` (or gamepad B), or just wait out the countdown.
3. Expected: the vanilla death resumes from where it paused — shatter, PermaDeath cutscene,
   shattered save slot; `ModLog.txt` shows `Death salvage declined` followed by the usual
   `Skipped backup of slot N: Steel Soul run already dead`.
4. The pre-death backups are still listed in the mod menu, so the run is recoverable the
   manual way (test E).
