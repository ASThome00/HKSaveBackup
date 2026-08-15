using System;
using System.IO;
using System.Reflection;
using Modding;
using UnityEngine;

namespace ToolAssistedSteelsoul
{
    /// <summary>Resolves the game's save-file paths and the mod's backup directory.</summary>
    internal static class SavePaths
    {
        private static readonly MethodInfo GetSaveFileNameMethod =
            typeof(ModHooks).GetMethod("GetSaveFileName", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        /// <summary>
        /// The live save file for a slot, honoring ModHooks.GetSaveFileNameHook (other mods
        /// can rename save files; Platform.GetSaveSlotFileName consults the same hook).
        /// The invoker is internal to the API, hence reflection with a vanilla-name fallback.
        /// </summary>
        public static string GetLiveSavePath(int slot)
        {
            string fileName = null;
            try
            {
                fileName = GetSaveFileNameMethod?.Invoke(null, new object[] { slot }) as string;
            }
            catch (Exception)
            {
                // Fall through to the vanilla name.
            }
            if (string.IsNullOrEmpty(fileName))
                fileName = slot == 0 ? "user.dat" : $"user{slot}.dat";
            return Path.Combine(Application.persistentDataPath, fileName);
        }

        /// <summary>The Modding API's per-save mod-data file (GameManager.ModdedSavePath).</summary>
        public static string GetModdedJsonPath(int slot) =>
            Path.Combine(Application.persistentDataPath, $"user{slot}.modded.json");

        public static string ResolveBackupRoot(string configuredDirectory)
        {
            if (!string.IsNullOrEmpty(configuredDirectory))
                return Environment.ExpandEnvironmentVariables(configuredDirectory);
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ToolAssistedSteelsoul");
        }
    }
}
