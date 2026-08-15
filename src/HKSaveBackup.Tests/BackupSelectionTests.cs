using System;
using System.Collections.Generic;
using System.Linq;
using HKSaveBackup.Core;
using Xunit;

namespace HKSaveBackup.Tests
{
    public class BackupSelectionTests
    {
        private const string Root = @"C:\backups";
        private const string SavePath = @"C:\saves\user1.dat";

        private readonly FakeFileSystem _fs = new FakeFileSystem();
        private readonly BackupStore _store;

        public BackupSelectionTests()
        {
            _store = new BackupStore(_fs, Root);
            _fs.AddFile(SavePath, "save-payload");
        }

        private BackupMetadata Meta(int minute, bool preRestore = false, string scene = "Crossroads_19")
        {
            return new BackupMetadata
            {
                Slot = 1,
                TimestampUtc = new DateTime(2026, 8, 14, 19, minute, 0, DateTimeKind.Utc),
                Scene = preRestore ? BackupNaming.PreRestoreScene : scene,
                CompletionPercent = 47,
                PlaytimeSeconds = 21840,
                Geo = 1204,
                PermadeathMode = 1,
                PreRestoreSnapshot = preRestore,
                GameVersion = "1.5.78.11833",
            };
        }

        private static BackupEntry Entry(int minute, string baseName, bool preRestore = false)
        {
            return new BackupEntry
            {
                BaseName = baseName,
                DatPath = baseName + ".dat",
                JsonPath = baseName + ".json",
                Slot = 1,
                TimestampUtc = new DateTime(2026, 8, 14, 19, minute, 0, DateTimeKind.Utc),
                Scene = preRestore ? BackupNaming.PreRestoreScene : "Crossroads_19",
            };
        }

        [Fact]
        public void OrderNewestFirst_SortsByTimestampDescending()
        {
            var ordered = BackupSelection.OrderNewestFirst(new[]
            {
                Entry(5, "a"), Entry(30, "b"), Entry(15, "c"),
            });

            Assert.Equal(new[] { "b", "c", "a" }, ordered.Select(e => e.BaseName));
        }

        [Fact]
        public void OrderNewestFirst_SameSecond_PutsUniquifiedNameFirst()
        {
            // The store appends "-2" to the second backup written in the same second, so
            // ordinal-descending base name keeps the later write first.
            var ordered = BackupSelection.OrderNewestFirst(new[]
            {
                Entry(5, "user1_20260814-190500_Crossroads_19"),
                Entry(5, "user1_20260814-190500_Crossroads_19-2"),
            });

            Assert.Equal("user1_20260814-190500_Crossroads_19-2", ordered[0].BaseName);
        }

        [Fact]
        public void OrderNewestFirst_MatchesStoreListingOrder()
        {
            _store.WriteBackup(SavePath, Meta(5), 20, out _);
            _store.WriteBackup(SavePath, Meta(30), 20, out _);
            _store.WriteBackup(SavePath, Meta(30), 20, out _);
            _store.WriteBackup(SavePath, Meta(15), 20, out _);

            List<BackupEntry> listed = _store.ListBackups(1);
            List<BackupEntry> ordered = BackupSelection.OrderNewestFirst(Shuffle(listed));

            Assert.Equal(listed.Select(e => e.BaseName), ordered.Select(e => e.BaseName));
        }

        [Fact]
        public void OrderNewestFirst_NullInput_IsEmpty()
        {
            Assert.Empty(BackupSelection.OrderNewestFirst(null));
        }

        [Fact]
        public void OrderNewestFirst_SkipsNullElements()
        {
            var ordered = BackupSelection.OrderNewestFirst(new[] { Entry(5, "a"), null, Entry(30, "b") });
            Assert.Equal(new[] { "b", "a" }, ordered.Select(e => e.BaseName));
        }

        [Fact]
        public void LatestReloadCandidate_PicksNewestBackup()
        {
            BackupEntry latest = BackupSelection.LatestReloadCandidate(new[]
            {
                Entry(5, "a"), Entry(30, "b"), Entry(15, "c"),
            });

            Assert.Equal("b", latest.BaseName);
        }

        [Fact]
        public void LatestReloadCandidate_SkipsPreRestoreSnapshots()
        {
            // The usual shape after one restore: the snapshot is the newest file on disk.
            BackupEntry latest = BackupSelection.LatestReloadCandidate(new[]
            {
                Entry(5, "a"), Entry(15, "b"), Entry(30, "snapshot", preRestore: true),
            });

            Assert.Equal("b", latest.BaseName);
        }

        [Fact]
        public void LatestReloadCandidate_SkipsSnapshotsFlaggedOnlyBySidecar()
        {
            // Scene name says nothing; the sidecar is what marks it as a snapshot.
            var snapshot = new BackupEntry
            {
                BaseName = "snapshot",
                Slot = 1,
                TimestampUtc = new DateTime(2026, 8, 14, 19, 30, 0, DateTimeKind.Utc),
                Scene = "Crossroads_19",
                Metadata = new BackupMetadata { Slot = 1, PreRestoreSnapshot = true },
            };

            BackupEntry latest = BackupSelection.LatestReloadCandidate(new[] { Entry(5, "a"), snapshot });

            Assert.Equal("a", latest.BaseName);
        }

        [Fact]
        public void LatestReloadCandidate_OnlySnapshots_IsNull()
        {
            Assert.Null(BackupSelection.LatestReloadCandidate(new[]
            {
                Entry(5, "a", preRestore: true), Entry(30, "b", preRestore: true),
            }));
        }

        [Fact]
        public void LatestReloadCandidate_NoBackups_IsNull()
        {
            Assert.Null(BackupSelection.LatestReloadCandidate(new BackupEntry[0]));
            Assert.Null(BackupSelection.LatestReloadCandidate(null));
        }

        [Fact]
        public void LatestReloadCandidate_ReadsFromStoreListing()
        {
            _store.WriteBackup(SavePath, Meta(5), 20, out _);
            _store.WriteBackup(SavePath, Meta(15), 20, out _);
            _store.WriteBackup(SavePath, Meta(30, preRestore: true), 20, out _);

            BackupEntry latest = BackupSelection.LatestReloadCandidate(_store.ListBackups(1));

            Assert.False(latest.IsPreRestoreSnapshot);
            Assert.Equal(new DateTime(2026, 8, 14, 19, 15, 0, DateTimeKind.Utc), latest.TimestampUtc);
        }

        [Fact]
        public void IsReloadCandidate_RejectsNullAndSnapshots()
        {
            Assert.False(BackupSelection.IsReloadCandidate(null));
            Assert.False(BackupSelection.IsReloadCandidate(Entry(5, "a", preRestore: true)));
            Assert.True(BackupSelection.IsReloadCandidate(Entry(5, "a")));
        }

        private static List<BackupEntry> Shuffle(IEnumerable<BackupEntry> entries)
        {
            // Deterministic reordering: reverse, then move the middle element to the front.
            var list = entries.Reverse().ToList();
            if (list.Count > 2)
            {
                BackupEntry middle = list[list.Count / 2];
                list.RemoveAt(list.Count / 2);
                list.Insert(0, middle);
            }
            return list;
        }
    }
}
