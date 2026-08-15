using System;
using ToolAssistedSteelsoul.Core;
using Xunit;

namespace ToolAssistedSteelsoul.Tests
{
    public class BackupPolicyTests
    {
        private static readonly DateTime Now = new DateTime(2026, 8, 14, 20, 0, 0, DateTimeKind.Utc);

        private static BackupDecision Decide(
            bool enabled = true,
            bool slotEnabled = true,
            bool saveSucceeded = true,
            int permadeathMode = 1,
            bool backupNormalSaves = false,
            bool backupOnQuitOnly = false,
            bool isQuitToMenuSave = false,
            double cooldownMinutes = 0,
            DateTime? lastBackupUtc = null)
        {
            return BackupPolicy.Decide(enabled, slotEnabled, saveSucceeded, permadeathMode,
                backupNormalSaves, backupOnQuitOnly, isQuitToMenuSave, cooldownMinutes,
                lastBackupUtc, Now);
        }

        [Fact]
        public void SteelSoulRun_IsBackedUp()
        {
            BackupDecision d = Decide(permadeathMode: 1);
            Assert.True(d.ShouldBackup);
        }

        [Fact]
        public void NormalRun_SkippedByDefault()
        {
            BackupDecision d = Decide(permadeathMode: 0);
            Assert.False(d.ShouldBackup);
            Assert.Equal(SkipReason.NormalSaveBackupsOff, d.Reason);
        }

        [Fact]
        public void NormalRun_BackedUpWhenOptedIn()
        {
            Assert.True(Decide(permadeathMode: 0, backupNormalSaves: true).ShouldBackup);
        }

        [Fact]
        public void DeadSteelSoulRun_NeverBackedUp()
        {
            // Mode 2 is the post-death save; even with normal backups on, it is worthless
            // as a restore point and must not displace the pre-death backup as "most recent".
            BackupDecision d = Decide(permadeathMode: 2, backupNormalSaves: true);
            Assert.False(d.ShouldBackup);
            Assert.Equal(SkipReason.SteelSoulRunAlreadyDead, d.Reason);
        }

        [Fact]
        public void UnknownPermadeathMode_IsBackedUp()
        {
            Assert.True(Decide(permadeathMode: 3).ShouldBackup);
        }

        [Fact]
        public void Disabled_SkipsEverything()
        {
            BackupDecision d = Decide(enabled: false, permadeathMode: 1);
            Assert.False(d.ShouldBackup);
            Assert.Equal(SkipReason.Disabled, d.Reason);
        }

        [Fact]
        public void FailedSave_IsNotBackedUp()
        {
            // The game reported the write failed; the file on disk is the OLD save,
            // or worse. Copying it now would produce a backup lying about its timestamp.
            BackupDecision d = Decide(saveSucceeded: false);
            Assert.False(d.ShouldBackup);
            Assert.Equal(SkipReason.GameReportedSaveFailed, d.Reason);
        }

        [Fact]
        public void Cooldown_SkipsInsideWindow()
        {
            BackupDecision d = Decide(cooldownMinutes: 10, lastBackupUtc: Now.AddMinutes(-5));
            Assert.False(d.ShouldBackup);
            Assert.Equal(SkipReason.Cooldown, d.Reason);
        }

        [Fact]
        public void Cooldown_AllowsAtExactBoundary()
        {
            Assert.True(Decide(cooldownMinutes: 10, lastBackupUtc: Now.AddMinutes(-10)).ShouldBackup);
        }

        [Fact]
        public void Cooldown_ZeroMeansEverySave()
        {
            Assert.True(Decide(cooldownMinutes: 0, lastBackupUtc: Now.AddSeconds(-1)).ShouldBackup);
        }

        [Fact]
        public void Cooldown_IgnoredWhenNoPriorBackup()
        {
            Assert.True(Decide(cooldownMinutes: 60, lastBackupUtc: null).ShouldBackup);
        }

        [Fact]
        public void DisabledSlot_IsSkipped()
        {
            BackupDecision d = Decide(slotEnabled: false);
            Assert.False(d.ShouldBackup);
            Assert.Equal(SkipReason.SlotDisabled, d.Reason);
        }

        [Fact]
        public void DisabledSlot_LosesToTheMasterSwitch()
        {
            // Both off: the master switch is the reason worth logging.
            Assert.Equal(SkipReason.Disabled, Decide(enabled: false, slotEnabled: false).Reason);
        }

        [Fact]
        public void DisabledSlot_SkipsEvenAQuitSave()
        {
            Assert.False(Decide(slotEnabled: false, backupOnQuitOnly: true, isQuitToMenuSave: true)
                .ShouldBackup);
        }

        [Fact]
        public void QuitOnly_SkipsABenchSave()
        {
            BackupDecision d = Decide(backupOnQuitOnly: true, isQuitToMenuSave: false);
            Assert.False(d.ShouldBackup);
            Assert.Equal(SkipReason.NotAQuitToMenuSave, d.Reason);
        }

        [Fact]
        public void QuitOnly_BacksUpTheQuitSave()
        {
            Assert.True(Decide(backupOnQuitOnly: true, isQuitToMenuSave: true).ShouldBackup);
        }

        [Fact]
        public void QuitOnly_Off_BacksUpEitherWay()
        {
            Assert.True(Decide(backupOnQuitOnly: false, isQuitToMenuSave: false).ShouldBackup);
            Assert.True(Decide(backupOnQuitOnly: false, isQuitToMenuSave: true).ShouldBackup);
        }

        [Fact]
        public void QuitOnly_DoesNotResurrectADeadSteelSoulSave()
        {
            // The death save is committed outside a quit, but ReturnToMainMenu after dying is
            // not: mode 2 has to win over "it's a quit save" or the shattered state becomes
            // the newest backup.
            BackupDecision d = Decide(permadeathMode: 2, backupOnQuitOnly: true, isQuitToMenuSave: true);
            Assert.False(d.ShouldBackup);
            Assert.Equal(SkipReason.SteelSoulRunAlreadyDead, d.Reason);
        }

        [Fact]
        public void QuitOnly_ReportedAheadOfCooldown()
        {
            // Both would skip; "you asked for quit saves only" is the actionable reason.
            BackupDecision d = Decide(backupOnQuitOnly: true, isQuitToMenuSave: false,
                cooldownMinutes: 10, lastBackupUtc: Now.AddMinutes(-1));
            Assert.Equal(SkipReason.NotAQuitToMenuSave, d.Reason);
        }

        [Fact]
        public void QuitOnly_StillHonorsTheCooldown()
        {
            BackupDecision d = Decide(backupOnQuitOnly: true, isQuitToMenuSave: true,
                cooldownMinutes: 10, lastBackupUtc: Now.AddMinutes(-1));
            Assert.False(d.ShouldBackup);
            Assert.Equal(SkipReason.Cooldown, d.Reason);
        }
    }
}
