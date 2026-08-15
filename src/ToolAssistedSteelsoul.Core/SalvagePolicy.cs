using System;
using System.Collections.Generic;

namespace ToolAssistedSteelsoul.Core
{
    /// <summary>Where a salvaged Steel Soul run is read back from.</summary>
    public enum SalvageSource
    {
        /// <summary>Nothing to salvage.</summary>
        None = 0,

        /// <summary>
        /// The slot's own .dat is already the last committed save and is left untouched;
        /// salvage only has to stop the death save from overwriting it.
        /// </summary>
        LiveSlotFile,

        /// <summary>The slot file is missing or has regressed; a backup is copied back over it.</summary>
        Backup,
    }

    public enum SalvageReason
    {
        /// <summary>The slot file exists and is no older than the newest backup.</summary>
        LiveSaveIsCurrent = 0,

        /// <summary>The slot file is gone (deleted, or a failed write); the newest backup stands in.</summary>
        LiveSaveMissing,

        /// <summary>A backup is meaningfully newer than the slot file, so the slot file lost progress.</summary>
        LiveSaveOlderThanNewestBackup,

        /// <summary>No slot file and no usable backup: there is nothing to hand back.</summary>
        NothingToSalvage,
    }

    public readonly struct SalvageDecision
    {
        public readonly SalvageSource Source;
        public readonly SalvageReason Reason;

        /// <summary>The backup to restore; non-null exactly when <see cref="Source"/> is Backup.</summary>
        public readonly BackupEntry Backup;

        private SalvageDecision(SalvageSource source, SalvageReason reason, BackupEntry backup)
        {
            Source = source;
            Reason = reason;
            Backup = backup;
        }

        public bool CanSalvage => Source != SalvageSource.None;

        public static SalvageDecision KeepLiveSlotFile(SalvageReason reason) =>
            new SalvageDecision(SalvageSource.LiveSlotFile, reason, null);

        public static SalvageDecision RestoreBackup(BackupEntry entry, SalvageReason reason) =>
            new SalvageDecision(SalvageSource.Backup, reason, entry);

        public static readonly SalvageDecision Nothing =
            new SalvageDecision(SalvageSource.None, SalvageReason.NothingToSalvage, null);
    }

    /// <summary>
    /// Pure decision logic for the death-salvage prompt: given the state of a save slot at the
    /// moment a Steel Soul run dies, which file should the player be handed back?
    ///
    /// The key asymmetry, verified against the decompiled 1.5.78 death sequence: the death save
    /// is written by GameManager.orig_PlayerDead, and salvage intercepts BEFORE that write. So
    /// at decision time the slot's .dat is still the last good commit — the same content the
    /// newest backup holds. Copying a backup over an intact slot file is therefore pure
    /// downside (an extra write that can fail, and it can only ever be older). Salvage prefers
    /// the live file and reaches for a backup only when the live file is missing or has visibly
    /// regressed behind the store (a hand-edited slot, a failed write, an earlier restore of an
    /// older backup).
    ///
    /// Pre-restore snapshots and mode-2 (already dead) backups are never chosen automatically:
    /// a snapshot's contents are opaque (the mod does not parse .dat files, so its metadata
    /// records permadeathMode -1) and handing back an unknown or dead save without the player
    /// picking it would defeat the point. The mod menu still lists them for manual restore.
    /// </summary>
    public static class SalvagePolicy
    {
        /// <summary>
        /// How far a backup must lead the slot file before the slot file is considered stale.
        /// A backup is written seconds after the commit it copies, but the two timestamps come
        /// from different clocks (file mtime vs. DateTime.UtcNow) and the save folder may live
        /// on a filesystem with coarse or skewed timestamps, so ties go to the live file.
        /// </summary>
        public static readonly TimeSpan StaleLiveSaveTolerance = TimeSpan.FromMinutes(5);

        /// <summary>A backup the prompt is allowed to select on the player's behalf.</summary>
        public static bool IsAutoSalvageCandidate(BackupEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.DatPath))
                return false;
            if (entry.IsPreRestoreSnapshot)
                return false;
            // No sidecar: the payload is still a valid restore point (see BackupStore).
            if (entry.Metadata != null && entry.Metadata.PermadeathMode == 2)
                return false;
            return true;
        }

        /// <param name="liveSaveExists">Whether the slot's live .dat is on disk right now.</param>
        /// <param name="liveSaveWriteTimeUtc">Its last-write time, or null if it could not be read.</param>
        /// <param name="backups">The slot's backups; order does not matter.</param>
        public static SalvageDecision Choose(
            bool liveSaveExists,
            DateTime? liveSaveWriteTimeUtc,
            IEnumerable<BackupEntry> backups)
        {
            BackupEntry newest = null;
            if (backups != null)
            {
                foreach (BackupEntry entry in backups)
                {
                    if (!IsAutoSalvageCandidate(entry))
                        continue;
                    if (newest == null || entry.TimestampUtc > newest.TimestampUtc)
                        newest = entry;
                }
            }

            if (!liveSaveExists)
            {
                return newest != null
                    ? SalvageDecision.RestoreBackup(newest, SalvageReason.LiveSaveMissing)
                    : SalvageDecision.Nothing;
            }

            if (newest == null)
                return SalvageDecision.KeepLiveSlotFile(SalvageReason.LiveSaveIsCurrent);

            // Unknown mtime: trust the live file. It is by construction the last committed
            // save, and an unnecessary restore is a write we do not have to risk.
            if (liveSaveWriteTimeUtc.HasValue &&
                newest.TimestampUtc - liveSaveWriteTimeUtc.Value > StaleLiveSaveTolerance)
            {
                return SalvageDecision.RestoreBackup(newest, SalvageReason.LiveSaveOlderThanNewestBackup);
            }

            return SalvageDecision.KeepLiveSlotFile(SalvageReason.LiveSaveIsCurrent);
        }
    }
}
