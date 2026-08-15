using System;
using System.Collections.Generic;
using ToolAssistedSteelsoul.Core;
using Modding;

namespace ToolAssistedSteelsoul
{
    internal sealed class RestoreResult
    {
        public bool Success;
        public string Message = "";
    }

    /// <summary>
    /// The one code path allowed to overwrite a live save file, and only after the menu's
    /// explicit confirmation step. Always snapshots the current slot contents into the
    /// backup store first — restoring must never be the operation that loses data.
    /// </summary>
    internal sealed class RestoreService
    {
        private readonly ILogger _log;
        private readonly BackupService _backups;

        public RestoreService(ILogger log, BackupService backups)
        {
            _log = log;
            _backups = backups;
        }

        /// <summary>
        /// Restore is only safe while no save is loaded: the game keeps loaded save state
        /// in memory and re-serializes it over the slot on the next save commit, which
        /// would silently discard the restore.
        /// </summary>
        public static bool IsAtMainMenu()
        {
            GameManager gm = GameManager.instance;
            return gm != null && gm.sceneName == "Menu_Title";
        }

        public RestoreResult Restore(BackupEntry entry)
        {
            try
            {
                if (!IsAtMainMenu())
                {
                    return new RestoreResult
                    {
                        Success = false,
                        Message = "Restore refused: a save is loaded. Return to the main menu first.",
                    };
                }

                string liveDat = SavePaths.GetLiveSavePath(entry.Slot);
                string liveModdedJson = SavePaths.GetModdedJsonPath(entry.Slot);
                BackupStore store = _backups.CreateStore();

                // Snapshot whatever currently occupies the slot before touching it.
                if (System.IO.File.Exists(liveDat))
                {
                    var snapshotMeta = new BackupMetadata
                    {
                        Slot = entry.Slot,
                        TimestampUtc = DateTime.UtcNow,
                        Scene = BackupNaming.PreRestoreScene,
                        // The slot's contents are opaque here (we never parse .dat files),
                        // so the gameplay fields are unknown; -1 keeps them visibly so.
                        CompletionPercent = -1,
                        PlaytimeSeconds = -1,
                        Geo = -1,
                        PermadeathMode = -1,
                        PreRestoreSnapshot = true,
                        GameVersion = "",
                    };
                    BackupEntry snapshot = store.WriteBackup(liveDat, snapshotMeta, int.MaxValue,
                        out IReadOnlyList<string> _, liveModdedJson);
                    _log.Log($"Pre-restore snapshot of slot {entry.Slot}: {liveDat} -> {snapshot.DatPath}");
                }
                else
                {
                    _log.Log($"Slot {entry.Slot} is empty; restoring into it without a snapshot.");
                }

                store.RestoreBackup(entry, liveDat, liveModdedJson);
                _log.Log($"Restored slot {entry.Slot}: {entry.DatPath} -> {liveDat}" +
                         (entry.ModdedJsonPath != null ? $" (with modded data -> {liveModdedJson})" : ""));

                RefreshSaveSlotUI();

                return new RestoreResult
                {
                    Success = true,
                    Message = "Backup restored. The save slot will show the restored file.",
                };
            }
            catch (Exception ex)
            {
                _log.LogError($"Restore of slot {entry.Slot} failed: {ex}");
                return new RestoreResult
                {
                    Success = false,
                    Message = "Restore failed - see ModLog.txt. The slot was snapshotted before any change.",
                };
            }
        }

        /// <summary>
        /// Force the save-select screen to re-read slot files, the same way the game does
        /// on user switch (UIManager.UIExplicitSwitchUser): clear each button's cached
        /// SaveStats so the next Prepare() call hits the disk again.
        /// </summary>
        private void RefreshSaveSlotUI()
        {
            try
            {
                UIManager ui = UIManager.instance;
                if (ui == null)
                    return;
                if (ui.slotOne != null) ui.slotOne.ClearCache();
                if (ui.slotTwo != null) ui.slotTwo.ClearCache();
                if (ui.slotThree != null) ui.slotThree.ClearCache();
                if (ui.slotFour != null) ui.slotFour.ClearCache();
                _log.Log("Save-select slot cache cleared; slots will re-read on next open.");
            }
            catch (Exception ex)
            {
                _log.LogError($"Could not refresh save-select UI (restart the game to see the restored save): {ex}");
            }
        }
    }
}
