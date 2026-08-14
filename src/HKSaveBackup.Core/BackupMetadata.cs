using System;
using System.Collections.Generic;
using System.Globalization;

namespace HKSaveBackup.Core
{
    /// <summary>
    /// Sidecar metadata describing one backup, rendered by the restore menu.
    /// Captured from the in-memory PlayerData at save time — the .dat payload is never parsed.
    /// </summary>
    public sealed class BackupMetadata
    {
        public int Slot;
        public DateTime TimestampUtc;
        public string Scene = "";
        public double CompletionPercent;
        public long PlaytimeSeconds;
        public int Geo;
        public int PermadeathMode;
        public bool PreRestoreSnapshot;
        public string GameVersion = "";

        private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

        public string ToJson()
        {
            return JsonLite.Write(new[]
            {
                new KeyValuePair<string, object>("slot", Slot),
                new KeyValuePair<string, object>("timestampUtc", TimestampUtc.ToString(TimestampFormat, CultureInfo.InvariantCulture)),
                new KeyValuePair<string, object>("scene", Scene ?? ""),
                new KeyValuePair<string, object>("completionPercent", CompletionPercent),
                new KeyValuePair<string, object>("playtimeSeconds", PlaytimeSeconds),
                new KeyValuePair<string, object>("geo", Geo),
                new KeyValuePair<string, object>("permadeathMode", PermadeathMode),
                new KeyValuePair<string, object>("preRestoreSnapshot", PreRestoreSnapshot),
                new KeyValuePair<string, object>("gameVersion", GameVersion ?? ""),
            });
        }

        public static BackupMetadata FromJson(string json)
        {
            Dictionary<string, object> map = JsonLite.Parse(json);
            var meta = new BackupMetadata
            {
                Slot = GetInt(map, "slot"),
                Scene = GetString(map, "scene"),
                CompletionPercent = GetDouble(map, "completionPercent"),
                PlaytimeSeconds = (long)GetDouble(map, "playtimeSeconds"),
                Geo = GetInt(map, "geo"),
                PermadeathMode = GetInt(map, "permadeathMode"),
                PreRestoreSnapshot = GetBool(map, "preRestoreSnapshot"),
                GameVersion = GetString(map, "gameVersion"),
            };
            string ts = GetString(map, "timestampUtc");
            meta.TimestampUtc = DateTime.ParseExact(ts, TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            return meta;
        }

        private static int GetInt(Dictionary<string, object> map, string key) => (int)GetDouble(map, key);

        private static double GetDouble(Dictionary<string, object> map, string key)
        {
            if (map.TryGetValue(key, out object value) && value is double d)
                return d;
            throw new FormatException($"Missing or non-numeric field '{key}'");
        }

        private static string GetString(Dictionary<string, object> map, string key)
        {
            if (map.TryGetValue(key, out object value) && value is string s)
                return s;
            return "";
        }

        private static bool GetBool(Dictionary<string, object> map, string key)
        {
            if (map.TryGetValue(key, out object value) && value is bool b)
                return b;
            return false;
        }
    }
}
