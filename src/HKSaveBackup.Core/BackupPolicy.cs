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
        SlotDisabled,
        NotAQuitToMenuSave,
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
        /// <summary>
        /// Parameters are ordered to match the order the gates are evaluated in. Callers should
        /// pass them by name — every gate but two is a bool, and a silent argument swap here
        /// means silently not backing up.
        /// </summary>
        /// <param name="enabled">Master switch (GlobalSettings.Enabled).</param>
        /// <param name="slotEnabled">Per-slot switch for the slot being saved.</param>
        /// <param name="gameReportedSaveSucceeded">The didSave flag the game handed its own save callback.</param>
        /// <param name="permadeathMode">0 normal, 1 Steel Soul, 2 Steel Soul already dead.</param>
        /// <param name="backupNormalSaves">Opt in to backing up permadeathMode 0 saves.</param>
        /// <param name="backupOnQuitOnly">Only back up saves committed on the way out of gameplay.</param>
        /// <param name="isQuitToMenuSave">This save was committed inside a quit-to-menu sequence.</param>
        public static BackupDecision Decide(
            bool enabled,
            bool slotEnabled,
            bool gameReportedSaveSucceeded,
            int permadeathMode,
            bool backupNormalSaves,
            bool backupOnQuitOnly,
            bool isQuitToMenuSave,
            double cooldownMinutes,
            DateTime? lastBackupUtc,
            DateTime nowUtc)
        {
            if (!enabled)
                return BackupDecision.Skip(SkipReason.Disabled);
            if (!slotEnabled)
                return BackupDecision.Skip(SkipReason.SlotDisabled);
            if (!gameReportedSaveSucceeded)
                return BackupDecision.Skip(SkipReason.GameReportedSaveFailed);
            if (permadeathMode == 2)
                return BackupDecision.Skip(SkipReason.SteelSoulRunAlreadyDead);
            if (permadeathMode == 0 && !backupNormalSaves)
                return BackupDecision.Skip(SkipReason.NormalSaveBackupsOff);
            // Checked after the permadeath gates so the more specific reason wins the log line,
            // and before the cooldown because "you asked for quit saves only" explains the skip
            // better than "the cooldown has not elapsed" when both apply.
            if (backupOnQuitOnly && !isQuitToMenuSave)
                return BackupDecision.Skip(SkipReason.NotAQuitToMenuSave);
            if (cooldownMinutes > 0 && lastBackupUtc.HasValue &&
                (nowUtc - lastBackupUtc.Value).TotalMinutes < cooldownMinutes)
                return BackupDecision.Skip(SkipReason.Cooldown);
            return BackupDecision.Backup;
        }
    }
}
