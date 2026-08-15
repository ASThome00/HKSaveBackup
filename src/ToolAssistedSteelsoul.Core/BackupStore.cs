using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ToolAssistedSteelsoul.Core
{
    /// <summary>
    /// Owns the backup directory layout and the ring-buffer retention:
    ///   {root}/slot{N}/user{N}_{timestamp}_{scene}.dat + .json
    /// All methods throw on IO failure; the mod layer decides what failures mean
    /// (backup failures are swallowed and logged, restore failures surface to the user).
    /// </summary>
    public sealed class BackupStore
    {
        private readonly IBackupFileSystem _fs;
        private readonly string _rootDirectory;

        public BackupStore(IBackupFileSystem fs, string rootDirectory)
        {
            _fs = fs ?? throw new ArgumentNullException(nameof(fs));
            _rootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
        }

        public string RootDirectory => _rootDirectory;

        public string SlotDirectory(int slot) => Path.Combine(_rootDirectory, $"slot{slot}");

        /// <summary>
        /// Copy <paramref name="sourceDatPath"/> into the store, write the sidecar, then prune.
        /// Returns the entry that was written. Never overwrites an existing backup: name
        /// collisions (two saves in the same second) get a numeric suffix.
        /// <paramref name="moddedJsonSourcePath"/> is the save's user{N}.modded.json (the
        /// Modding API's per-save mod data); when present it is carried along as opaque
        /// bytes so a restore does not desync other mods' save data.
        /// </summary>
        public BackupEntry WriteBackup(string sourceDatPath, BackupMetadata metadata, int maxBackupsPerSlot,
            out IReadOnlyList<string> prunedBaseNames, string moddedJsonSourcePath = null)
        {
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));

            string slotDir = SlotDirectory(metadata.Slot);
            _fs.CreateDirectory(slotDir);

            string baseName = BackupNaming.MakeBaseName(metadata.Slot, metadata.TimestampUtc, metadata.Scene);
            baseName = Uniquify(slotDir, baseName);

            string datPath = Path.Combine(slotDir, baseName + ".dat");
            string jsonPath = Path.Combine(slotDir, baseName + ".json");
            string moddedPath = null;

            // Payload before sidecar: a .dat without a .json is still a valid restore
            // point, a .json without a .dat is garbage.
            _fs.CopyFile(sourceDatPath, datPath, overwrite: false);
            if (moddedJsonSourcePath != null && _fs.FileExists(moddedJsonSourcePath))
            {
                moddedPath = Path.Combine(slotDir, baseName + ".modded.json");
                _fs.CopyFile(moddedJsonSourcePath, moddedPath, overwrite: false);
            }
            _fs.WriteAllText(jsonPath, metadata.ToJson());

            prunedBaseNames = Prune(metadata.Slot, maxBackupsPerSlot);

            return new BackupEntry
            {
                BaseName = baseName,
                DatPath = datPath,
                JsonPath = jsonPath,
                ModdedJsonPath = moddedPath,
                Slot = metadata.Slot,
                TimestampUtc = metadata.TimestampUtc,
                Scene = BackupNaming.SanitizeScene(metadata.Scene),
                Metadata = metadata,
            };
        }

        /// <summary>All backups for a slot, newest first. Files with unparseable names are ignored.</summary>
        public List<BackupEntry> ListBackups(int slot)
        {
            var entries = new List<BackupEntry>();
            foreach (string datPath in _fs.ListFiles(SlotDirectory(slot), "*.dat"))
            {
                string baseName = Path.GetFileNameWithoutExtension(datPath);
                if (!BackupNaming.TryParseBaseName(baseName, out int parsedSlot, out DateTime ts, out string scene))
                    continue;

                string jsonPath = Path.Combine(SlotDirectory(slot), baseName + ".json");
                string moddedPath = Path.Combine(SlotDirectory(slot), baseName + ".modded.json");
                BackupMetadata metadata = null;
                if (_fs.FileExists(jsonPath))
                {
                    try
                    {
                        metadata = BackupMetadata.FromJson(_fs.ReadAllText(jsonPath));
                    }
                    catch (FormatException)
                    {
                        // Corrupt sidecar: the backup itself is intact, list it without metadata.
                    }
                }

                entries.Add(new BackupEntry
                {
                    BaseName = baseName,
                    DatPath = datPath,
                    JsonPath = jsonPath,
                    ModdedJsonPath = _fs.FileExists(moddedPath) ? moddedPath : null,
                    Slot = parsedSlot,
                    TimestampUtc = ts,
                    Scene = scene,
                    Metadata = metadata,
                });
            }

            return entries
                .OrderByDescending(e => e.TimestampUtc)
                .ThenByDescending(e => e.BaseName, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Delete oldest backups beyond <paramref name="maxBackupsPerSlot"/>.
        /// Deletes the .dat last so an interrupted prune leaves a restorable payload,
        /// never an orphaned sidecar pretending to be one.
        /// </summary>
        public IReadOnlyList<string> Prune(int slot, int maxBackupsPerSlot)
        {
            if (maxBackupsPerSlot < 1)
                maxBackupsPerSlot = 1;

            List<BackupEntry> entries = ListBackups(slot);
            var pruned = new List<string>();
            for (int i = maxBackupsPerSlot; i < entries.Count; i++)
            {
                BackupEntry victim = entries[i];
                if (_fs.FileExists(victim.JsonPath))
                    _fs.DeleteFile(victim.JsonPath);
                if (victim.ModdedJsonPath != null && _fs.FileExists(victim.ModdedJsonPath))
                    _fs.DeleteFile(victim.ModdedJsonPath);
                _fs.DeleteFile(victim.DatPath);
                pruned.Add(victim.BaseName);
            }
            return pruned;
        }

        /// <summary>
        /// Copy a backup's payload over the live save file, and bring the modded.json
        /// companion in line with the backup: restored if the backup has one, deleted from
        /// the target otherwise (a stale companion would feed mods per-save data belonging
        /// to a different timeline). The caller is responsible for having snapshotted the
        /// target first (see the restore flow).
        /// </summary>
        public void RestoreBackup(BackupEntry entry, string targetDatPath, string targetModdedJsonPath = null)
        {
            _fs.CopyFile(entry.DatPath, targetDatPath, overwrite: true);
            if (targetModdedJsonPath == null)
                return;
            if (entry.ModdedJsonPath != null && _fs.FileExists(entry.ModdedJsonPath))
                _fs.CopyFile(entry.ModdedJsonPath, targetModdedJsonPath, overwrite: true);
            else if (_fs.FileExists(targetModdedJsonPath))
                _fs.DeleteFile(targetModdedJsonPath);
        }

        private string Uniquify(string slotDir, string baseName)
        {
            if (!_fs.FileExists(Path.Combine(slotDir, baseName + ".dat")))
                return baseName;
            for (int i = 2; ; i++)
            {
                string candidate = $"{baseName}-{i}";
                if (!_fs.FileExists(Path.Combine(slotDir, candidate + ".dat")))
                    return candidate;
            }
        }
    }
}
