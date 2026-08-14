using System;
using HKSaveBackup.Core;
using Xunit;

namespace HKSaveBackup.Tests
{
    public class BackupNamingTests
    {
        private static readonly DateTime Timestamp = new DateTime(2026, 8, 14, 19, 30, 42, DateTimeKind.Utc);

        [Fact]
        public void MakeBaseName_MatchesDocumentedShape()
        {
            Assert.Equal("user1_20260814-193042_Crossroads_19",
                BackupNaming.MakeBaseName(1, Timestamp, "Crossroads_19"));
        }

        [Theory]
        [InlineData(1, "Crossroads_19")]
        [InlineData(4, "Fungus1_06")]
        [InlineData(2, "Room_Colosseum_Bronze")] // scene with multiple underscores
        [InlineData(3, "prerestore")]
        [InlineData(12, "Town")] // multi-digit slot
        public void BaseName_RoundTrips(int slot, string scene)
        {
            string baseName = BackupNaming.MakeBaseName(slot, Timestamp, scene);
            Assert.True(BackupNaming.TryParseBaseName(baseName, out int parsedSlot, out DateTime parsedTs, out string parsedScene));
            Assert.Equal(slot, parsedSlot);
            Assert.Equal(Timestamp, parsedTs);
            Assert.Equal(DateTimeKind.Utc, parsedTs.Kind);
            Assert.Equal(scene, parsedScene);
        }

        [Theory]
        [InlineData("Cross roads/19", "Cross-roads-19")]
        [InlineData("", "unknown")]
        [InlineData(null, "unknown")]
        [InlineData("scene:with*bad?chars", "scene-with-bad-chars")]
        public void SanitizeScene_ProducesFilenameSafeTokens(string scene, string expected)
        {
            Assert.Equal(expected, BackupNaming.SanitizeScene(scene));
        }

        [Theory]
        [InlineData("")]
        [InlineData("user")]
        [InlineData("user1")]
        [InlineData("user1_20260814-193042")] // no scene
        [InlineData("user1_20260814-193042_")] // empty scene
        [InlineData("userX_20260814-193042_Town")] // non-numeric slot
        [InlineData("user1_2026081x-193042_Town")] // bad timestamp
        [InlineData("save1_20260814-193042_Town")] // wrong prefix
        [InlineData("user1_20261314-193042_Town")] // month 13
        public void TryParseBaseName_RejectsMalformedNames(string baseName)
        {
            Assert.False(BackupNaming.TryParseBaseName(baseName, out _, out _, out _));
        }

        [Fact]
        public void UniquifiedName_StillParses()
        {
            Assert.True(BackupNaming.TryParseBaseName("user1_20260814-193042_Crossroads_19-2",
                out int slot, out DateTime ts, out string scene));
            Assert.Equal(1, slot);
            Assert.Equal(Timestamp, ts);
            Assert.Equal("Crossroads_19-2", scene);
        }
    }
}
