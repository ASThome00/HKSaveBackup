using System;

namespace HKSaveBackup.Core
{
    public enum SkipReason
    {
        None = 0,
        Disabled,
        GameReportedSaveFailed,
        NormalSaveBackupsOff,
        SteelSoulRunAlreadyDead,
        Cooldown,
    }

    public readonly struct BackupDecision
    {
        public readonly bool ShouldBackup;
        public readonly SkipReason Reason;

        private BackupDecision(bool shouldBackup, SkipReason reason)
        {
            ShouldBackup = shouldBackup;
            Reason = reason;
        }

        public static readonly BackupDecision Backup = new BackupDecision(true, SkipReason.None);
        public static BackupDecision Skip(SkipReason reason) => new BackupDecision(false, reason);
    }

    /// <summary>
    /// Pure decision logic for whether a save-commit should produce a backup.
    ///
    /// permadeathMode semantics (verified against the decompiled 1.5.78 assembly):
    ///   0 = normal run, 1 = active Steel Soul run, 2 = Steel Soul run that has died.
    /// Mode 2 matters: on death the game flips 1 -> 2 and then SAVES. Backing that up
    /// would make "most recent backup" the shattered, unplayable state — skip it.
    /// Unknown modes (future/modded) are treated like Steel Soul: backing up too much
    /// is recoverable, backing up too little is not.
    /// </summary>
    public static class BackupPolicy
    {
        public static BackupDecision Decide(
            bool enabled,
            bool gameReportedSaveSucceeded,
            int permadeathMode,
            bool backupNormalSaves,
            double cooldownMinutes,
            DateTime? lastBackupUtc,
            DateTime nowUtc)
        {
            if (!enabled)
                return BackupDecision.Skip(SkipReason.Disabled);
            if (!gameReportedSaveSucceeded)
                return BackupDecision.Skip(SkipReason.GameReportedSaveFailed);
            if (permadeathMode == 2)
                return BackupDecision.Skip(SkipReason.SteelSoulRunAlreadyDead);
            if (permadeathMode == 0 && !backupNormalSaves)
                return BackupDecision.Skip(SkipReason.NormalSaveBackupsOff);
            if (cooldownMinutes > 0 && lastBackupUtc.HasValue &&
                (nowUtc - lastBackupUtc.Value).TotalMinutes < cooldownMinutes)
                return BackupDecision.Skip(SkipReason.Cooldown);
            return BackupDecision.Backup;
        }
    }
}
