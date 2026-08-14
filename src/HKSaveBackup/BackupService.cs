using System;
using System.Collections.Generic;
using HKSaveBackup.Core;
using Modding;

namespace HKSaveBackup
{
    /// <summary>
    /// Orchestrates one backup per save-commit: policy check, metadata capture, copy, prune.
    /// Every public method is exception-safe — this code runs inside the game's save
    /// callback, and a mod failure must never corrupt or abort the game's own save.
    /// </summary>
    internal sealed class BackupService
    {
        private readonly ILogger _log;
        private readonly Func<GlobalSettings> _settings;
        private readonly IBackupFileSystem _fs;

        /// <summary>Per-slot time of last successful backup, for the cooldown. In-memory only:
        /// a game restart resets the cooldown, which errs toward taking a backup.</summary>
        private readonly Dictionary<int, DateTime> _lastBackupUtc = new Dictionary<int, DateTime>();

        public BackupService(ILogger log, Func<GlobalSettings> settings, IBackupFileSystem fs)
        {
            _log = log;
            _settings = settings;
            _fs = fs;
        }

        public BackupStore CreateStore() =>
            new BackupStore(_fs, SavePaths.ResolveBackupRoot(_settings().BackupDirectory));

        /// <summary>
        /// Called from the wrapped SaveGame callback, after the game reported the write result.
        /// </summary>
        public void OnSaveCommitted(GameManager gameManager, int saveSlot, bool didSave)
        {
            try
            {
                GlobalSettings settings = _settings();
                PlayerData playerData = gameManager != null ? gameManager.playerData : null;
                int permadeathMode = playerData != null ? playerData.permadeathMode : 0;

                _lastBackupUtc.TryGetValue(saveSlot, out DateTime last);
                BackupDecision decision = BackupPolicy.Decide(
                    settings.Enabled,
                    didSave,
                    permadeathMode,
                    settings.BackupNormalSaves,
                    settings.CooldownMinutes,
                    _lastBackupUtc.ContainsKey(saveSlot) ? last : (DateTime?)null,
                    DateTime.UtcNow);

                if (!decision.ShouldBackup)
                {
                    _log.Log($"Skipped backup of slot {saveSlot}: {DescribeSkip(decision.Reason)}");
                    return;
                }

                string livePath = SavePaths.GetLiveSavePath(saveSlot);
                if (!_fs.FileExists(livePath))
                {
                    _log.LogWarn($"Skipped backup of slot {saveSlot}: save file not found at {livePath}");
                    return;
                }

                var metadata = new BackupMetadata
                {
                    Slot = saveSlot,
                    TimestampUtc = DateTime.UtcNow,
                    Scene = gameManager != null ? gameManager.sceneName : "",
                    CompletionPercent = playerData != null ? Math.Round(playerData.completionPercentage, 1) : 0,
                    PlaytimeSeconds = playerData != null ? (long)playerData.playTime : 0,
                    Geo = playerData != null ? playerData.geo : 0,
                    PermadeathMode = permadeathMode,
                    PreRestoreSnapshot = false,
                    GameVersion = playerData != null ? playerData.version ?? "" : "",
                };

                BackupStore store = CreateStore();
                BackupEntry entry = store.WriteBackup(livePath, metadata, settings.MaxBackupsPerSlot,
                    out IReadOnlyList<string> pruned);
                _lastBackupUtc[saveSlot] = metadata.TimestampUtc;

                _log.Log($"Backed up slot {saveSlot} ({livePath}) -> {entry.DatPath} " +
                         $"[scene={metadata.Scene}, permadeathMode={permadeathMode}, " +
                         $"completion={metadata.CompletionPercent}%, geo={metadata.Geo}]");
                foreach (string victim in pruned)
                    _log.Log($"Pruned oldest backup of slot {saveSlot}: {victim} (retention {settings.MaxBackupsPerSlot})");
            }
            catch (Exception ex)
            {
                // Hard constraint: never throw into the game's save path.
                try { _log.LogError($"Backup of slot {saveSlot} failed (game save is unaffected): {ex}"); }
                catch { /* even logging must not throw here */ }
            }
        }

        private static string DescribeSkip(SkipReason reason)
        {
            switch (reason)
            {
                case SkipReason.Disabled: return "mod is disabled in settings";
                case SkipReason.GameReportedSaveFailed: return "game reported the save write failed";
                case SkipReason.NormalSaveBackupsOff: return "normal (non-Steel-Soul) save and BackupNormalSaves is off";
                case SkipReason.SteelSoulRunAlreadyDead: return "Steel Soul run already dead (permadeathMode=2 death save)";
                case SkipReason.Cooldown: return "cooldown window has not elapsed";
                default: return reason.ToString();
            }
        }
    }
}
