using System;
using System.Collections.Generic;
using HKSaveBackup.Core;
using Xunit;

namespace HKSaveBackup.Tests
{
    public class SalvagePolicyTests
    {
        private static readonly DateTime DeathTime = new DateTime(2026, 8, 15, 21, 0, 0, DateTimeKind.Utc);

        private static BackupEntry Backup(DateTime timestampUtc, int permadeathMode = 1,
            bool preRestoreSnapshot = false, bool withMetadata = true)
        {
            return new BackupEntry
            {
                BaseName = "user1_" + timestampUtc.Ticks,
                DatPath = @"C:\backups\slot1\user1.dat",
                JsonPath = @"C:\backups\slot1\user1.json",
                Slot = 1,
                TimestampUtc = timestampUtc,
                Scene = preRestoreSnapshot ? BackupNaming.PreRestoreScene : "Crossroads_04",
                Metadata = withMetadata
                    ? new BackupMetadata
                    {
                        Slot = 1,
                        TimestampUtc = timestampUtc,
                        Scene = preRestoreSnapshot ? BackupNaming.PreRestoreScene : "Crossroads_04",
                        PermadeathMode = permadeathMode,
                        PreRestoreSnapshot = preRestoreSnapshot,
                    }
                    : null,
            };
        }

        [Fact]
        public void LiveSaveIsPreferred_WhenItMatchesTheNewestBackup()
        {
            // The normal case: salvage intercepts before the death save, so the slot file is
            // still the last good commit. Restoring a backup over it would be a pointless write.
            SalvageDecision d = SalvagePolicy.Choose(
                liveSaveExists: true,
                liveSaveWriteTimeUtc: DeathTime.AddMinutes(-30),
                backups: new List<BackupEntry> { Backup(DeathTime.AddMinutes(-30)) });

            Assert.Equal(SalvageSource.LiveSlotFile, d.Source);
            Assert.Equal(SalvageReason.LiveSaveIsCurrent, d.Reason);
            Assert.Null(d.Backup);
            Assert.True(d.CanSalvage);
        }

        [Fact]
        public void LiveSaveIsPreferred_WhenItIsNewerThanEveryBackup()
        {
            // Cooldown mode: the last commit produced no backup, so the slot file leads the store.
            SalvageDecision d = SalvagePolicy.Choose(true, DeathTime.AddMinutes(-2),
                new List<BackupEntry> { Backup(DeathTime.AddHours(-3)) });

            Assert.Equal(SalvageSource.LiveSlotFile, d.Source);
        }

        [Fact]
        public void LiveSaveIsPreferred_WhenBackupsExistButNoneAreEligible()
        {
            SalvageDecision d = SalvagePolicy.Choose(true, DeathTime.AddMinutes(-5),
                new List<BackupEntry>
                {
                    Backup(DeathTime.AddMinutes(-1), preRestoreSnapshot: true),
                    Backup(DeathTime.AddMinutes(-1), permadeathMode: 2),
                });

            Assert.Equal(SalvageSource.LiveSlotFile, d.Source);
        }

        [Fact]
        public void LiveSaveIsPreferred_WhenItsWriteTimeIsUnknown()
        {
            SalvageDecision d = SalvagePolicy.Choose(true, null,
                new List<BackupEntry> { Backup(DeathTime.AddHours(-1)) });

            Assert.Equal(SalvageSource.LiveSlotFile, d.Source);
        }

        [Fact]
        public void LiveSaveIsPreferred_WithinTheStaleTolerance()
        {
            SalvageDecision d = SalvagePolicy.Choose(true, DeathTime.AddMinutes(-10),
                new List<BackupEntry> { Backup(DeathTime.AddMinutes(-10).Add(SalvagePolicy.StaleLiveSaveTolerance)) });

            Assert.Equal(SalvageSource.LiveSlotFile, d.Source);
        }

        [Fact]
        public void NewestBackupWins_WhenTheLiveSaveHasRegressed()
        {
            BackupEntry newest = Backup(DeathTime.AddMinutes(-5));
            SalvageDecision d = SalvagePolicy.Choose(true, DeathTime.AddHours(-6),
                new List<BackupEntry> { Backup(DeathTime.AddHours(-8)), newest });

            Assert.Equal(SalvageSource.Backup, d.Source);
            Assert.Equal(SalvageReason.LiveSaveOlderThanNewestBackup, d.Reason);
            Assert.Same(newest, d.Backup);
        }

        [Fact]
        public void NewestBackupWins_WhenTheLiveSaveIsMissing()
        {
            BackupEntry newest = Backup(DeathTime.AddMinutes(-20));
            SalvageDecision d = SalvagePolicy.Choose(false, null,
                new List<BackupEntry> { newest, Backup(DeathTime.AddHours(-2)) });

            Assert.Equal(SalvageSource.Backup, d.Source);
            Assert.Equal(SalvageReason.LiveSaveMissing, d.Reason);
            Assert.Same(newest, d.Backup);
        }

        [Fact]
        public void DeadRunBackupsAreNeverChosenAutomatically()
        {
            // A mode-2 backup is a shattered run; handing it back would salvage nothing.
            BackupEntry alive = Backup(DeathTime.AddHours(-2));
            SalvageDecision d = SalvagePolicy.Choose(false, null,
                new List<BackupEntry> { Backup(DeathTime.AddMinutes(-1), permadeathMode: 2), alive });

            Assert.Equal(SalvageSource.Backup, d.Source);
            Assert.Same(alive, d.Backup);
        }

        [Fact]
        public void PreRestoreSnapshotsAreNeverChosenAutomatically()
        {
            // Snapshot contents are opaque (the mod never parses .dat files), so their run state
            // is unknown. They stay available for a manual restore from the mod menu.
            BackupEntry ordinary = Backup(DeathTime.AddHours(-2));
            SalvageDecision d = SalvagePolicy.Choose(false, null,
                new List<BackupEntry> { Backup(DeathTime.AddMinutes(-1), preRestoreSnapshot: true), ordinary });

            Assert.Same(ordinary, d.Backup);
        }

        [Fact]
        public void SnapshotWithoutMetadataIsRecognisedByItsSceneName()
        {
            BackupEntry snapshot = Backup(DeathTime.AddMinutes(-1), preRestoreSnapshot: true, withMetadata: false);
            Assert.False(SalvagePolicy.IsAutoSalvageCandidate(snapshot));
        }

        [Fact]
        public void BackupWithoutMetadataIsStillACandidate()
        {
            // A corrupt or missing sidecar does not make the payload unrestorable.
            BackupEntry entry = Backup(DeathTime.AddMinutes(-10), withMetadata: false);
            Assert.True(SalvagePolicy.IsAutoSalvageCandidate(entry));

            SalvageDecision d = SalvagePolicy.Choose(false, null, new List<BackupEntry> { entry });
            Assert.Same(entry, d.Backup);
        }

        [Fact]
        public void NothingToSalvage_WhenNoLiveSaveAndNoUsableBackup()
        {
            SalvageDecision d = SalvagePolicy.Choose(false, null,
                new List<BackupEntry> { Backup(DeathTime.AddMinutes(-1), permadeathMode: 2) });

            Assert.False(d.CanSalvage);
            Assert.Equal(SalvageSource.None, d.Source);
            Assert.Equal(SalvageReason.NothingToSalvage, d.Reason);
        }

        [Fact]
        public void NothingToSalvage_WhenTheSlotIsEmptyAndUnbackedUp()
        {
            Assert.False(SalvagePolicy.Choose(false, null, null).CanSalvage);
            Assert.False(SalvagePolicy.Choose(false, null, new List<BackupEntry>()).CanSalvage);
        }

        [Fact]
        public void EntriesWithoutAPayloadPathAreNotCandidates()
        {
            var broken = new BackupEntry { Slot = 1, TimestampUtc = DeathTime, DatPath = null };
            Assert.False(SalvagePolicy.IsAutoSalvageCandidate(broken));
            Assert.False(SalvagePolicy.IsAutoSalvageCandidate(null));
        }
    }
}
