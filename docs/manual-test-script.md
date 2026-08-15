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
4. *Options → Mods → HKSaveBackup → Save Manager → Restore Slot N* → newest backup → read the
   confirmation text → *Restore Now*.
5. Back out to the save select: slot N shows the pre-death save (not shattered), with the
   bench location — **without restarting the game**.
6. Load it: you spawn at the backed-up bench with the noted completion %/geo.

## F. Restore is refused in-game + cloud warning shown (acceptance 6)

1. Load any save (get in-game).
2. Pause → *Options → Mods → HKSaveBackup → Save Manager → Restore Slot N*.
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

## I. Settings screen is reachable from the pause menu (feature 2)

1. Load any save (get in-game), pause, *Options → Mods → HKSaveBackup*.
2. The screen that opens is the settings list — no restore buttons on it — ending in a
   *Save Manager* entry. Changing an option here does not require being at the title
   screen.
3. *Save Manager* → *Restore Slot N* still shows the main-menu gate message (this is
   section F).

## J. Per-slot backup switch (feature 2)

1. On the Steel Soul save in slot N, set *Back Up Slot N* to **Off** in the settings
   screen, back out to the title screen (settings persist on menu exit).
2. Load the save, rest at a bench.
3. No new pair appears in `Documents\HKSaveBackup\slotN\`; `ModLog.txt` has
   `Skipped backup of slot N: automatic backups are turned off for this slot`.
4. *Save Manager* still lists slot N and its existing backups, with the description
   noting backups are off for that slot; restoring one still works.
5. Set it back to **On**, rest again, confirm a backup appears.
6. Restart the game and confirm the toggle round-trips (check `SlotEnabled` in
   `HKSaveBackupMod.GlobalSettings.json`).

## K. Backup When = Quit To Menu (feature 2)

1. Set *Backup When* to **Quit To Menu**, load the Steel Soul save.
2. Rest at a bench: no new backup; `ModLog.txt` has
   `Skipped backup of slot N: not a quit-to-menu save and BackupOnQuitOnly is on`.
3. Pause → *Return to Menu*. Exactly one new backup pair appears for slot N, and
   `ModLog.txt` has the matching `Backed up slot N` line.
4. Set *Backup When* back to **Every Save** and confirm bench saves back up again — this
   proves the mark is not stuck raised after the quit.

## L. Load straight from the restore flow (feature 3)

1. From the title screen: *Options → Mods → HKSaveBackup → Save Manager → Restore Slot N*.
   The list's top entry is "Restore Latest & Load" and names the newest non-snapshot backup.
2. Select it. The confirmation screen still appears, still shows the Steam Cloud warning,
   and its button reads "Restore & Load".
3. Confirm. The mod menu fades out and the game loads slot N exactly as picking the slot on
   the save-select would (start-game sting, fade, Knight_Pickup). `ModLog.txt` shows
   `Loading slot N via GameManager.LoadGameFromUI`.
4. Verify the loaded run is the backed-up state (scene, geo, completion), then quit to menu:
   the title screen fades back in normally and the mod menu is reachable again.
5. Plain restore path: pick an individual backup and confirm with "Restore Now". The result
   screen now offers "Load This Save Now"; press it and repeat check 3.
6. Refusal path: after a restore, corrupt `userN.dat` (or restore a dead Steel Soul save,
   permadeathMode 2) and press "Load This Save Now". The result screen comes back saying
   the save was not loaded and why; the menu still works and the restore itself stands.
7. In-game path: pause → *Options → Mods → HKSaveBackup → Save Manager → Restore Slot N*.
   The screen still refuses with the main-menu-only message, and no load button is offered
   anywhere.
