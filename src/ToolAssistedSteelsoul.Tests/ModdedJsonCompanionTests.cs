using System;
using System.Linq;
using ToolAssistedSteelsoul.Core;
using Xunit;

namespace ToolAssistedSteelsoul.Tests
{
    public class ModdedJsonCompanionTests
    {
        private const string Root = @"C:\backups";
        private const string SavePath = @"C:\saves\user1.dat";
        private const string ModdedPath = @"C:\saves\user1.modded.json";

        private readonly FakeFileSystem _fs = new FakeFileSystem();
        private readonly BackupStore _store;

        public ModdedJsonCompanionTests()
        {
            _store = new BackupStore(_fs, Root);
            _fs.AddFile(SavePath, "save-payload");
        }

        private static BackupMetadata Meta(int minute = 0) => new BackupMetadata
        {
            Slot = 1,
            TimestampUtc = new DateTime(2026, 8, 14, 19, minute, 0, DateTimeKind.Utc),
            Scene = "Crossroads_19",
            PermadeathMode = 1,
        };

        [Fact]
        public void WriteBackup_CarriesModdedJson_WhenPresent()
        {
            _fs.AddFile(ModdedPath, "{\"Benchwarp\":{}}");
            BackupEntry entry = _store.WriteBackup(SavePath, Meta(), 20, out _, ModdedPath);
            Assert.NotNull(entry.ModdedJsonPath);
            Assert.Equal("{\"Benchwarp\":{}}", _fs.ReadAllText(entry.ModdedJsonPath));
        }

        [Fact]
        public void WriteBackup_NoModdedJson_IsFine()
        {
            BackupEntry entry = _store.WriteBackup(SavePath, Meta(), 20, out _, ModdedPath);
            Assert.Null(entry.ModdedJsonPath);
        }

        [Fact]
        public void ListBackups_FindsCompanion()
        {
            _fs.AddFile(ModdedPath, "mod-data");
            _store.WriteBackup(SavePath, Meta(), 20, out _, ModdedPath);
            Assert.NotNull(_store.ListBackups(1).Single().ModdedJsonPath);
        }

        [Fact]
        public void Prune_DeletesCompanionWithPair()
        {
            _fs.AddFile(ModdedPath, "mod-data");
            BackupEntry oldest = _store.WriteBackup(SavePath, Meta(minute: 0), 20, out _, ModdedPath);
            _store.WriteBackup(SavePath, Meta(minute: 1), 20, out _, ModdedPath);

            _store.Prune(1, 1);

            Assert.False(_fs.FileExists(oldest.DatPath));
            Assert.False(_fs.FileExists(oldest.JsonPath));
            Assert.False(_fs.FileExists(oldest.ModdedJsonPath));
        }

        [Fact]
        public void Restore_BringsCompanionBack()
        {
            _fs.AddFile(ModdedPath, "old-mod-data");
            BackupEntry entry = _store.WriteBackup(SavePath, Meta(), 20, out _, ModdedPath);
            _fs.WriteAllText(ModdedPath, "new-mod-data");

            _store.RestoreBackup(entry, SavePath, ModdedPath);

            Assert.Equal("old-mod-data", _fs.ReadAllText(ModdedPath));
        }

        [Fact]
        public void Restore_DeletesStaleCompanion_WhenBackupHadNone()
        {
            BackupEntry entry = _store.WriteBackup(SavePath, Meta(), 20, out _, ModdedPath);
            _fs.AddFile(ModdedPath, "acquired-later");

            _store.RestoreBackup(entry, SavePath, ModdedPath);

            Assert.False(_fs.FileExists(ModdedPath));
        }
    }
}
