using HKSaveBackup.Core;

namespace HKSaveBackup
{
    /// <summary>
    /// Persisted via the API's IGlobalSettings mechanism to
    /// {saves}/HKSaveBackupMod.GlobalSettings.json.
    /// </summary>
    public class GlobalSettings
    {
        /// <summary>Master switch. When false the mod takes no backups (restore menu still works).</summary>
        public bool Enabled = true;

        /// <summary>
        /// Where backups are written. Empty means the default: %USERPROFILE%\Documents\HKSaveBackup.
        /// Deliberately OUTSIDE the game's save folder — Steam Cloud syncs that folder, and a
        /// backup living there can be swept, deduplicated, or deleted by cloud sync exactly
        /// when the save it protects is destroyed. Environment variables are expanded.
        /// </summary>
        public string BackupDirectory = "";

        /// <summary>Ring-buffer size per save slot. Oldest backup pair is pruned first.</summary>
        public int MaxBackupsPerSlot = 20;

        /// <summary>
        /// Minimum minutes between backups per slot. 0 = back up on every save commit.
        /// Bench/story/quit saves are already naturally spaced, so this is an escape hatch,
        /// not the default behavior.
        /// </summary>
        public double CooldownMinutes = 0;

        /// <summary>Also back up non-Steel-Soul saves. Off by default to avoid spamming
        /// backups on ordinary playthroughs.</summary>
        public bool BackupNormalSaves = false;

        /// <summary>
        /// Only back up saves the game commits on its way out of gameplay — i.e. the save
        /// inside GameManager.ReturnToMainMenu. Bench, story and autosave commits are skipped.
        /// Off by default: for a Steel Soul run the bench save IS the safety net, and a
        /// quit-only backup can be hours behind the death it is meant to undo.
        /// </summary>
        public bool BackupOnQuitOnly = false;

        /// <summary>
        /// Per-save-slot opt-out, index 0 = slot 1. A disabled slot still restores normally;
        /// only automatic backups stop. Read through <see cref="IsSlotEnabled"/> — this array
        /// is user-editable JSON and may be null, short, or long.
        /// </summary>
        public bool[] SlotEnabled = { true, true, true, true };

        /// <summary>The number of save slots the settings menu exposes a toggle for.</summary>
        public const int SlotCount = 4;

        /// <summary>
        /// Whether automatic backups are on for a slot. Slots outside the array (a hand-edited
        /// or truncated settings file, or the legacy slot 0 "user.dat") count as enabled:
        /// an unrecognised slot should still be protected.
        /// </summary>
        public bool IsSlotEnabled(int slot) => SlotToggles.IsEnabled(SlotEnabled, slot);

        /// <summary>Sets the per-slot switch, growing/normalising the array as needed.</summary>
        public void SetSlotEnabled(int slot, bool value) =>
            SlotEnabled = SlotToggles.WithSlot(SlotEnabled, slot, value, SlotCount);

        /// <summary>
        /// Offer "salvage this run / let it die" when a Steel Soul run dies, instead of letting
        /// the death commit straight away.
        ///
        /// OFF BY DEFAULT, and deliberately so: every other part of this mod is gameplay-inert
        /// (it copies files the game has already written), while this one interrupts the death
        /// sequence and can undo a permadeath. Players who want the stakes intact never have to
        /// opt out of anything. With this false the death path is the vanilla one, untouched.
        /// </summary>
        public bool DeathSalvagePrompt = false;

        /// <summary>
        /// Seconds the death prompt waits for an answer before choosing "let it die" on its own.
        /// The timeout is a safety net, not a feature: if the overlay fails to draw or input
        /// never arrives, the run must still resolve the way vanilla would, not hang forever.
        /// Clamped to a sane range at use time.
        /// </summary>
        public double DeathSalvagePromptSeconds = 20;
    }
}
