using System;

namespace HKSaveBackup.Core
{
    /// <summary>One backup on disk: a .dat payload plus its .json sidecar.</summary>
    public sealed class BackupEntry
    {
        public string BaseName;
        public string DatPath;
        public string JsonPath;
        public int Slot;
        public DateTime TimestampUtc;
        public string Scene;

        /// <summary>Sidecar contents; null if the sidecar is missing or unreadable (the entry is still restorable).</summary>
        public BackupMetadata Metadata;

        /// <summary>
        /// Path of the backed-up user{N}.modded.json (the Modding API's per-save mod data),
        /// or null when the save had none at backup time. Copied as opaque bytes.
        /// </summary>
        public string ModdedJsonPath;

        public bool IsPreRestoreSnapshot =>
            Metadata?.PreRestoreSnapshot ?? Scene == BackupNaming.PreRestoreScene;
    }
}
