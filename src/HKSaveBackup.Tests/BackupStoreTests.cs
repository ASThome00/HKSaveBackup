using System;
using System.Linq;
using HKSaveBackup.Core;
using Xunit;

namespace HKSaveBackup.Tests
{
    public class BackupStoreTests
    {
        private const string Root = @"C:\backups";
        private const string SavePath = @"C:\saves\user1.dat";

        private readonly FakeFileSystem _fs = new FakeFileSystem();
        private readonly BackupStore _store;

        public BackupStoreTests()
        {
            _store = new BackupStore(_fs, Root);
            _fs.AddFile(SavePath, "save-payload");
        }

        private BackupMetadata Meta(int slot = 1, int minute = 0, string scene = "Crossroads_19", bool preRestore = false)
        {
            return new BackupMetadata
            {
                Slot = slot,
                TimestampUtc = new DateTime(2026, 8, 14, 19, minute, 0, DateTimeKind.Utc),
                Scene = scene,
                CompletionPercent = 47,
                PlaytimeSeconds = 21840,
                Geo = 1204,
                PermadeathMode = 1,
                PreRestoreSnapshot = preRestore,
                GameVersion = "1.5.78.11833",
            };
        }

        [Fact]
        public void WriteBackup_CreatesPayloadAndSidecar()
        {
            BackupEntry entry = _store.WriteBackup(SavePath, Meta(), 20, out var pruned);

            Assert.Equal(@"C:\backups\slot1\user1_20260814-190000_Crossroads_19.dat", entry.DatPath);
            Assert.True(_fs.FileExists(entry.DatPath));
            Assert.True(_fs.FileExists(entry.JsonPath));
            Assert.Equal("save-payload", _fs.ReadAllText(entry.DatPath));
            Assert.Empty(pruned);

            BackupMetadata sidecar = BackupMetadata.FromJson(_fs.ReadAllText(entry.JsonPath));
            Assert.Equal(1, sidecar.PermadeathMode);
        }

        [Fact]
        public void WriteBackup_SameSecond_GetsUniqueName()
        {
            BackupEntry first = _store.WriteBackup(SavePath, Meta(), 20, out _);
            BackupEntry second = _store.WriteBackup(SavePath, Meta(), 20, out _);
            Assert.NotEqual(first.DatPath, second.DatPath);
            Assert.EndsWith("-2.dat", second.DatPath);
        }

        [Fact]
        public void ListBackups_NewestFirst()
        {
            _store.WriteBackup(SavePath, Meta(minute: 5), 20, out _);
            _store.WriteBackup(SavePath, Meta(minute: 30), 20, out _);
            _store.WriteBackup(SavePath, Meta(minute: 15), 20, out _);

            var listed = _store.ListBackups(1);
            Assert.Equal(3, listed.Count);
            Assert.Equal(30, listed[0].TimestampUtc.Minute);
            Assert.Equal(15, listed[1].TimestampUtc.Minute);
            Assert.Equal(5, listed[2].TimestampUtc.Minute);
        }

        [Fact]
        public void ListBackups_IsolatesSlots()
        {
            _store.WriteBackup(SavePath, Meta(slot: 1), 20, out _);
            _store.WriteBackup(SavePath, Meta(slot: 2), 20, out _);
            Assert.Single(_store.ListBackups(1));
            Assert.Single(_store.ListBackups(2));
            Assert.Empty(_store.ListBackups(3));
        }

        [Fact]
        public void ListBackups_ToleratesCorruptSidecar()
        {
            BackupEntry entry = _store.WriteBackup(SavePath, Meta(), 20, out _);
            _fs.WriteAllText(entry.JsonPath, "{corrupt");

            var listed = _store.ListBackups(1);
            Assert.Single(listed);
            Assert.Null(listed[0].Metadata);
            Assert.Equal("Crossroads_19", listed[0].Scene); // falls back to filename parse
        }

        [Fact]
        public void ListBackups_IgnoresForeignFiles()
        {
            _fs.AddFile(@"C:\backups\slot1\notes.dat", "not-a-backup");
            _store.WriteBackup(SavePath, Meta(), 20, out _);
            Assert.Single(_store.ListBackups(1));
        }

        [Fact]
        public void Prune_AtLimit_DeletesNothing()
        {
            for (int i = 0; i < 5; i++)
                _store.WriteBackup(SavePath, Meta(minute: i), 5, out _);
            Assert.Equal(5, _store.ListBackups(1).Count);
        }

        [Fact]
        public void Prune_OneOverLimit_DeletesOldestPair()
        {
            for (int i = 0; i < 5; i++)
                _store.WriteBackup(SavePath, Meta(minute: i), 5, out _);
            BackupEntry oldest = _store.ListBackups(1).Last();

            _store.WriteBackup(SavePath, Meta(minute: 10), 5, out var pruned);

            Assert.Equal(new[] { oldest.BaseName }, pruned);
            Assert.False(_fs.FileExists(oldest.DatPath));
            Assert.False(_fs.FileExists(oldest.JsonPath));
            Assert.Equal(5, _store.ListBackups(1).Count);
        }

        [Fact]
        public void Prune_LimitShrunk_DeletesAllExcess()
        {
            for (int i = 0; i < 10; i++)
                _store.WriteBackup(SavePath, Meta(minute: i), 20, out _);

            var pruned = _store.Prune(1, 3);

            Assert.Equal(7, pruned.Count);
            var remaining = _store.ListBackups(1);
            Assert.Equal(3, remaining.Count);
            Assert.Equal(9, remaining[0].TimestampUtc.Minute); // newest survived
        }

        [Fact]
        public void Prune_LimitBelowOne_IsClampedToOne()
        {
            for (int i = 0; i < 3; i++)
                _store.WriteBackup(SavePath, Meta(minute: i), 20, out _);
            _store.Prune(1, 0);
            Assert.Single(_store.ListBackups(1));
        }

        [Fact]
        public void Prune_DoesNotTouchOtherSlots()
        {
            _store.WriteBackup(SavePath, Meta(slot: 2), 20, out _);
            for (int i = 0; i < 4; i++)
                _store.WriteBackup(SavePath, Meta(slot: 1, minute: i), 2, out _);
            Assert.Single(_store.ListBackups(2));
        }

        [Fact]
        public void RestoreBackup_OverwritesTarget()
        {
            BackupEntry entry = _store.WriteBackup(SavePath, Meta(), 20, out _);
            _fs.WriteAllText(SavePath, "dead-save");

            _store.RestoreBackup(entry, SavePath);

            Assert.Equal("save-payload", _fs.ReadAllText(SavePath));
        }

        [Fact]
        public void PreRestoreSnapshot_CountsTowardRingAndIsFlagged()
        {
            BackupEntry snapshot = _store.WriteBackup(SavePath,
                Meta(scene: BackupNaming.PreRestoreScene, preRestore: true), 20, out _);
            Assert.True(snapshot.IsPreRestoreSnapshot);
            Assert.True(_store.ListBackups(1).Single().IsPreRestoreSnapshot);
        }

        [Fact]
        public void WriteBackup_MissingSource_Throws()
        {
            Assert.ThrowsAny<Exception>(() =>
                _store.WriteBackup(@"C:\saves\missing.dat", Meta(), 20, out _));
        }
    }
}
