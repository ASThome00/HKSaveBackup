using System;

namespace HKSaveBackup.Core
{
    /// <summary>
    /// Pure handling of the per-save-slot backup opt-out flags (index 0 = slot 1).
    ///
    /// The flag array is user-editable JSON, so it can arrive null, truncated, or longer than
    /// the game has slots. Every one of those has to degrade to "this slot is protected" rather
    /// than throw: this is read from inside the game's save callback, and the failure mode of
    /// guessing wrong is a save that silently stops being backed up.
    /// </summary>
    public static class SlotToggles
    {
        public static bool IsEnabled(bool[] flags, int slot)
        {
            int index = slot - 1;
            if (flags == null || index < 0 || index >= flags.Length)
                return true;
            return flags[index];
        }

        /// <summary>
        /// Returns a new flag array with <paramref name="slot"/> set, padded to at least
        /// <paramref name="minCount"/> entries. Missing entries pad with true, matching
        /// <see cref="IsEnabled"/>, so growing the array never silently disables a slot.
        /// An out-of-range slot returns the input unchanged.
        /// </summary>
        public static bool[] WithSlot(bool[] flags, int slot, bool value, int minCount)
        {
            int index = slot - 1;
            if (index < 0)
                return flags;

            int size = Math.Max(minCount, index + 1);
            if (flags != null && flags.Length > size)
                size = flags.Length;

            var result = new bool[size];
            for (int i = 0; i < size; i++)
                result[i] = flags == null || i >= flags.Length || flags[i];
            result[index] = value;
            return result;
        }
    }
}
