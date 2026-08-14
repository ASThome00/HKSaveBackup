using System;
using System.Globalization;
using System.Threading;
using HKSaveBackup.Core;
using Xunit;

namespace HKSaveBackup.Tests
{
    public class BackupMetadataTests
    {
        private static BackupMetadata Sample() => new BackupMetadata
        {
            Slot = 1,
            TimestampUtc = new DateTime(2026, 8, 14, 19, 30, 42, DateTimeKind.Utc),
            Scene = "Crossroads_19",
            CompletionPercent = 47.5,
            PlaytimeSeconds = 21840,
            Geo = 1204,
            PermadeathMode = 1,
            PreRestoreSnapshot = false,
            GameVersion = "1.5.78.11833",
        };

        [Fact]
        public void Json_RoundTrips()
        {
            BackupMetadata parsed = BackupMetadata.FromJson(Sample().ToJson());
            Assert.Equal(1, parsed.Slot);
            Assert.Equal(Sample().TimestampUtc, parsed.TimestampUtc);
            Assert.Equal("Crossroads_19", parsed.Scene);
            Assert.Equal(47.5, parsed.CompletionPercent);
            Assert.Equal(21840, parsed.PlaytimeSeconds);
            Assert.Equal(1204, parsed.Geo);
            Assert.Equal(1, parsed.PermadeathMode);
            Assert.False(parsed.PreRestoreSnapshot);
            Assert.Equal("1.5.78.11833", parsed.GameVersion);
        }

        [Fact]
        public void Json_UsesDocumentedFieldNames()
        {
            string json = Sample().ToJson();
            Assert.Contains("\"slot\"", json);
            Assert.Contains("\"timestampUtc\": \"2026-08-14T19:30:42Z\"", json);
            Assert.Contains("\"scene\"", json);
            Assert.Contains("\"completionPercent\"", json);
            Assert.Contains("\"playtimeSeconds\"", json);
            Assert.Contains("\"geo\"", json);
            Assert.Contains("\"permadeathMode\"", json);
            Assert.Contains("\"preRestoreSnapshot\"", json);
        }

        [Fact]
        public void Json_IsCultureInvariant()
        {
            CultureInfo original = Thread.CurrentThread.CurrentCulture;
            try
            {
                // de-DE writes decimals with commas; the sidecar must not.
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                string json = Sample().ToJson();
                Assert.Contains("47.5", json);
                Assert.Equal(47.5, BackupMetadata.FromJson(json).CompletionPercent);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [Fact]
        public void FromJson_EscapedStrings_RoundTrip()
        {
            BackupMetadata meta = Sample();
            meta.Scene = "weird \"scene\"\\name";
            Assert.Equal("weird \"scene\"\\name", BackupMetadata.FromJson(meta.ToJson()).Scene);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{\"slot\": {\"nested\": 1}}")]
        [InlineData("{\"slot\": \"one\"}")]
        public void FromJson_Malformed_Throws(string json)
        {
            Assert.Throws<FormatException>(() => BackupMetadata.FromJson(json));
        }

        [Fact]
        public void FromJson_PreRestoreSnapshotFlag_Survives()
        {
            BackupMetadata meta = Sample();
            meta.PreRestoreSnapshot = true;
            Assert.True(BackupMetadata.FromJson(meta.ToJson()).PreRestoreSnapshot);
        }
    }
}
