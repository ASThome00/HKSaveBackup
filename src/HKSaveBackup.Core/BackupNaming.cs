using System;
using System.Globalization;
using System.Text;

namespace HKSaveBackup.Core
{
    /// <summary>
    /// Backup filename scheme: user{slot}_{yyyyMMdd-HHmmss}_{scene}[.dat|.json].
    /// The scene suffix exists so a user scanning the folder can find "the backup from
    /// before I walked into the Colosseum" without opening sidecars.
    /// </summary>
    public static class BackupNaming
    {
        public const string TimestampFormat = "yyyyMMdd-HHmmss";

        /// <summary>Scene token used for snapshots taken of the slot being overwritten during a restore.</summary>
        public const string PreRestoreScene = "prerestore";

        public static string MakeBaseName(int slot, DateTime timestampUtc, string scene)
        {
            return $"user{slot.ToString(CultureInfo.InvariantCulture)}" +
                   $"_{timestampUtc.ToString(TimestampFormat, CultureInfo.InvariantCulture)}" +
                   $"_{SanitizeScene(scene)}";
        }

        /// <summary>
        /// Keep only filename-safe characters; scene names are IDs like "Crossroads_19"
        /// so this is a guard against surprises, not a lossy transform in practice.
        /// </summary>
        public static string SanitizeScene(string scene)
        {
            if (string.IsNullOrEmpty(scene))
                return "unknown";
            var sb = new StringBuilder(scene.Length);
            foreach (char c in scene)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    sb.Append(c);
                else
                    sb.Append('-');
            }
            return sb.Length > 0 ? sb.ToString() : "unknown";
        }

        /// <summary>
        /// Parse a base name (no extension) back into its parts. The timestamp block has a
        /// fixed shape, so scenes containing underscores parse unambiguously.
        /// </summary>
        public static bool TryParseBaseName(string baseName, out int slot, out DateTime timestampUtc, out string scene)
        {
            slot = 0;
            timestampUtc = default;
            scene = null;
            if (string.IsNullOrEmpty(baseName) || !baseName.StartsWith("user", StringComparison.Ordinal))
                return false;

            int firstSep = baseName.IndexOf('_');
            if (firstSep <= 4)
                return false;
            if (!int.TryParse(baseName.Substring(4, firstSep - 4), NumberStyles.None, CultureInfo.InvariantCulture, out slot))
                return false;

            int tsStart = firstSep + 1;
            int tsLen = TimestampFormat.Length; // yyyyMMdd-HHmmss = 15 chars
            if (baseName.Length < tsStart + tsLen + 2 || baseName[tsStart + tsLen] != '_')
                return false;
            string ts = baseName.Substring(tsStart, tsLen);
            if (!DateTime.TryParseExact(ts, TimestampFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestampUtc))
                return false;

            scene = baseName.Substring(tsStart + tsLen + 1);
            return scene.Length > 0;
        }
    }
}
