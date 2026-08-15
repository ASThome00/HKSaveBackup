using HKSaveBackup.Core;
using Xunit;

namespace HKSaveBackup.Tests
{
    public class SlotTogglesTests
    {
        private static bool[] AllOn() => new[] { true, true, true, true };

        [Fact]
        public void DefaultFlags_EverySlotEnabled()
        {
            bool[] flags = AllOn();
            for (int slot = 1; slot <= 4; slot++)
                Assert.True(SlotToggles.IsEnabled(flags, slot));
        }

        [Fact]
        public void IsEnabled_ReadsTheSlotMinusOneIndex()
        {
            bool[] flags = { true, false, true, true };
            Assert.True(SlotToggles.IsEnabled(flags, 1));
            Assert.False(SlotToggles.IsEnabled(flags, 2));
            Assert.True(SlotToggles.IsEnabled(flags, 3));
        }

        [Fact]
        public void MissingFlags_CountAsEnabled()
        {
            // Settings written by an older build have no array at all.
            Assert.True(SlotToggles.IsEnabled(null, 1));
        }

        [Fact]
        public void TruncatedFlags_CountAsEnabled()
        {
            bool[] flags = { false, false };
            Assert.False(SlotToggles.IsEnabled(flags, 2));
            Assert.True(SlotToggles.IsEnabled(flags, 3));
            Assert.True(SlotToggles.IsEnabled(flags, 4));
        }

        [Fact]
        public void UnknownSlots_CountAsEnabled()
        {
            // Slot 0 is the legacy "user.dat" profile; a negative slot is nonsense. Neither
            // should be quietly dropped from backup.
            Assert.True(SlotToggles.IsEnabled(new[] { false, false, false, false }, 0));
            Assert.True(SlotToggles.IsEnabled(new[] { false, false, false, false }, -1));
        }

        [Fact]
        public void WithSlot_SetsOnlyTheTargetSlot()
        {
            bool[] result = SlotToggles.WithSlot(AllOn(), 3, false, 4);
            Assert.Equal(new[] { true, true, false, true }, result);
        }

        [Fact]
        public void WithSlot_DoesNotMutateTheInput()
        {
            bool[] original = AllOn();
            SlotToggles.WithSlot(original, 2, false, 4);
            Assert.Equal(AllOn(), original);
        }

        [Fact]
        public void WithSlot_GrowsAShortArrayPaddingWithEnabled()
        {
            // Padding must agree with IsEnabled's default, or persisting a single toggle
            // would disable every slot the old array did not cover.
            bool[] result = SlotToggles.WithSlot(new[] { false }, 4, false, 4);
            Assert.Equal(new[] { false, true, true, false }, result);
        }

        [Fact]
        public void WithSlot_GrowsFromNull()
        {
            bool[] result = SlotToggles.WithSlot(null, 1, false, 4);
            Assert.Equal(new[] { false, true, true, true }, result);
        }

        [Fact]
        public void WithSlot_KeepsExtraEntriesFromALongerArray()
        {
            bool[] result = SlotToggles.WithSlot(new[] { true, true, true, true, false }, 1, false, 4);
            Assert.Equal(new[] { false, true, true, true, false }, result);
        }

        [Fact]
        public void WithSlot_IgnoresAnOutOfRangeSlot()
        {
            bool[] original = AllOn();
            Assert.Same(original, SlotToggles.WithSlot(original, 0, false, 4));
        }

        [Fact]
        public void WithSlot_RoundTripsThroughIsEnabled()
        {
            bool[] flags = SlotToggles.WithSlot(null, 2, false, 4);
            Assert.False(SlotToggles.IsEnabled(flags, 2));
            Assert.True(SlotToggles.IsEnabled(flags, 1));

            flags = SlotToggles.WithSlot(flags, 2, true, 4);
            Assert.True(SlotToggles.IsEnabled(flags, 2));
        }
    }
}
