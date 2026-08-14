namespace HKSaveBackup
{
    /// <summary>
    /// Persisted via the API's IGlobalSettings mechanism to
    /// {saves}/HKSaveBackupMod.GlobalSettings.json.
    /// </summary>
    public class GlobalSettings
    {
        /// <summary>Master switch. When false the mod takes no backups (restore menu still works).</summary>
        public bool Enabled = true;

        /// <summary>
        /// Where backups are written. Empty means the default: %USERPROFILE%\Documents\HKSaveBackup.
        /// Deliberately OUTSIDE the game's save folder — Steam Cloud syncs that folder, and a
        /// backup living there can be swept, deduplicated, or deleted by cloud sync exactly
        /// when the save it protects is destroyed. Environment variables are expanded.
        /// </summary>
        public string BackupDirectory = "";

        /// <summary>Ring-buffer size per save slot. Oldest backup pair is pruned first.</summary>
        public int MaxBackupsPerSlot = 20;

        /// <summary>
        /// Minimum minutes between backups per slot. 0 = back up on every save commit.
        /// Bench/story/quit saves are already naturally spaced, so this is an escape hatch,
        /// not the default behavior.
        /// </summary>
        public double CooldownMinutes = 0;

        /// <summary>Also back up non-Steel-Soul saves. Off by default to avoid spamming
        /// backups on ordinary playthroughs.</summary>
        public bool BackupNormalSaves = false;
    }
}
