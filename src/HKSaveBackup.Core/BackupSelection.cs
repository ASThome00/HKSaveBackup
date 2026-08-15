using System;
using System.Collections.Generic;
using System.Linq;

namespace HKSaveBackup.Core
{
    /// <summary>
    /// Ordering and selection rules for the restore menu's entry list, kept out of the
    /// Unity layer so they are testable.
    ///
    /// "Latest" for the one-action reload deliberately skips pre-restore snapshots. Those
    /// are automatic copies of whatever occupied the slot immediately before a restore
    /// overwrote it, so the newest one is normally the exact state the player just restored
    /// away from — picking it would make a second "Restore Latest &amp; Load" silently undo
    /// the first. Snapshots stay in the browsable list; they are just never the automatic
    /// choice.
    /// </summary>
    public static class BackupSelection
    {
        /// <summary>
        /// Newest first, matching <see cref="BackupStore.ListBackups"/>: timestamp
        /// descending, then base name descending so same-second backups keep the order
        /// their "-2"/"-3" uniquifier suffixes imply (later write first). Null elements
        /// are dropped rather than thrown on — a menu should never fail to draw.
        /// </summary>
        public static List<BackupEntry> OrderNewestFirst(IEnumerable<BackupEntry> entries)
        {
            if (entries == null)
                return new List<BackupEntry>();

            return entries
                .Where(e => e != null)
                .OrderByDescending(e => e.TimestampUtc)
                .ThenByDescending(e => e.BaseName ?? "", StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Whether an entry may be chosen automatically by "Restore Latest &amp; Load".
        /// </summary>
        public static bool IsReloadCandidate(BackupEntry entry) =>
            entry != null && !entry.IsPreRestoreSnapshot;

        /// <summary>
        /// The newest backup that is not a pre-restore snapshot, or null when the slot has
        /// only snapshots (or nothing at all) — in which case the caller must not offer the
        /// one-action reload.
        /// </summary>
        public static BackupEntry LatestReloadCandidate(IEnumerable<BackupEntry> entries) =>
            OrderNewestFirst(entries).FirstOrDefault(IsReloadCandidate);
    }
}
