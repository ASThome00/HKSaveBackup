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

        public bool IsPreRestoreSnapshot =>
            Metadata?.PreRestoreSnapshot ?? Scene == BackupNaming.PreRestoreScene;
    }
}
