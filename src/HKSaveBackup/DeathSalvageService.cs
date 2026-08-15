using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HKSaveBackup.Core;
using Modding;
using UnityEngine;

namespace HKSaveBackup
{
    /// <summary>
    /// Feature 4: on a Steel Soul death, offer "salvage the run" or "let it die".
    ///
    /// THE DEATH SEQUENCE (verified against the decompiled 1.5.78.11833 assembly):
    ///   HeroController.Die()                 flips playerData permadeathMode 1 -> 2 in MEMORY,
    ///                                        spawns the death effect, then
    ///                                        StartCoroutine(gm.PlayerDead(DEATH_WAIT)).
    ///   GameManager.PlayerDead(waitTime)     ModHooks.OnBeforePlayerDead, orig_PlayerDead,
    ///                                        ModHooks.OnAfterPlayerDead.
    ///   GameManager.orig_PlayerDead          FreezeInPlace, NoLongerFirstGame,
    ///                                        ResetSemiPersistentItems, then
    ///                                        SaveGame(profileID, cb)  <-- the death save,
    ///                                        waits for it, then LoadScene("PermaDeath").
    ///   PermaDeath scene                     cutscene; its exit runs
    ///                                        GameManager.LoadPermadeathUnlockScene(), which for
    ///                                        an already-unlocked player is
    ///                                        ReturnToMainMenu(SaveAndContinueOnFail) - another
    ///                                        save commit.
    ///   Nothing ever deletes the dead file: the only GameManager.ClearSaveFile caller is
    ///   SaveSlotButton.ClearSaveFile, i.e. the player confirming the clear prompt on a slot
    ///   that save-select renders in its DEFEATED state.
    ///
    /// WHY WE INTERCEPT AT PlayerDead (and not on the PermaDeath scene):
    /// PlayerDead is the last point at which the slot file on disk is still the last good
    /// commit — orig_PlayerDead's SaveGame is what destroys it. Intercepting here means the
    /// common salvage costs zero file writes: we simply never let the death save happen. The
    /// PermaDeath-scene alternative would have to overwrite the (by then destroyed) slot file
    /// from a backup, would still need save suppression because the scene's exit route saves
    /// again, and on a first Steel Soul death would already have burned the PermaDeath_Unlock
    /// side effects. Strictly more writes, strictly more state to undo.
    ///
    /// RE-SERIALIZATION SAFETY (why this is allowed to bypass RestoreService's main-menu gate):
    /// The gate exists because a loaded session holds its save in memory and re-serializes it
    /// over the slot on the next commit. Salvage satisfies that invariant rather than the rule:
    ///   1. The only commit that would have happened is the death save inside orig_PlayerDead,
    ///      and on the salvage branch orig is never invoked.
    ///   2. From the moment the player chooses salvage until the main menu is reached, save
    ///      commits are latched off in the mod's own SaveGame hook (see ShouldRefuseSaveCommit),
    ///      so any other commit attempt is refused instead of executed.
    ///   3. The route back is GameManager.ReturnToMainMenu(DontSave) - the game's own no-save
    ///      quit path, verified to skip the SaveGame call entirely.
    ///   4. Any actual file restore is deferred until GameManager.sceneName == "Menu_Title", so
    ///      it runs through the ordinary RestoreService.Restore with its gate satisfied and its
    ///      pre-restore snapshot taken.
    /// The dead in-memory PlayerData is therefore never serialized anywhere.
    /// </summary>
    internal sealed class DeathSalvageService
    {
        private const double MinPromptSeconds = 5;
        private const double MaxPromptSeconds = 120;

        /// <summary>How long to wait for the main menu after asking the game to quit to it.</summary>
        private const float ReturnToMenuTimeoutSeconds = 90f;

        /// <summary>
        /// Hard ceiling on the save-suppression latch. If the salvage coroutine dies (a Unity
        /// exception kills the coroutine without unwinding through our finally), a latch stuck
        /// on would silently stop the game saving for the rest of the session. Expiring it is
        /// the lesser evil: by then the player is either at the menu or has quit.
        /// </summary>
        private const float SuppressionWatchdogSeconds = 180f;

        private readonly HKSaveBackupMod _mod;
        private readonly Func<GlobalSettings> _settings;
        private readonly BackupService _backups;
        private readonly RestoreService _restore;

        private SalvagePromptOverlay _overlay;
        private bool _suppressSaveCommits;
        private float _suppressArmedAtRealtime;
        private bool _salvageInFlight;

        public DeathSalvageService(HKSaveBackupMod mod, Func<GlobalSettings> settings,
            BackupService backups, RestoreService restore)
        {
            _mod = mod;
            _settings = settings;
            _backups = backups;
            _restore = restore;
        }

        public void Install()
        {
            // Installed unconditionally: the toggle is read at death time, so it can be flipped
            // from the mod menu mid-session. With the toggle off the hook is a pass-through that
            // returns the game's own enumerator untouched.
            On.GameManager.PlayerDead += OnPlayerDead;
        }

        public void Uninstall()
        {
            On.GameManager.PlayerDead -= OnPlayerDead;
            DisarmSaveSuppression("mod unloaded");
            if (_overlay != null)
            {
                UnityEngine.Object.Destroy(_overlay.gameObject);
                _overlay = null;
            }
        }

        /// <summary>
        /// Consulted by the mod's SaveGame hook. True means: do not let this commit run.
        /// Only ever true between "player chose salvage" and "main menu reached".
        /// </summary>
        public bool ShouldRefuseSaveCommit()
        {
            if (!_suppressSaveCommits)
                return false;
            if (Time.realtimeSinceStartup - _suppressArmedAtRealtime > SuppressionWatchdogSeconds)
            {
                _mod.LogError("Death-salvage save suppression expired without reaching the main menu; " +
                              "re-enabling saves. The save slot on disk was never overwritten.");
                _suppressSaveCommits = false;
                return false;
            }
            return true;
        }

        private IEnumerator OnPlayerDead(On.GameManager.orig_PlayerDead orig, GameManager self, float waitTime)
        {
            // No yields before this point: on the vanilla path we hand back the game's own
            // enumerator, so a death with the toggle off runs byte-for-byte the vanilla flow.
            SalvageDecision decision;
            int slot;
            string detail;
            try
            {
                if (!ShouldPrompt(self, out decision, out slot, out detail))
                    return orig(self, waitTime);
            }
            catch (Exception ex)
            {
                SafeLogError($"Death-salvage check failed; the death proceeds as vanilla: {ex}");
                return orig(self, waitTime);
            }

            return RunPrompt(orig, self, waitTime, slot, decision, detail);
        }

        private bool ShouldPrompt(GameManager gm, out SalvageDecision decision, out int slot, out string detail)
        {
            decision = SalvageDecision.Nothing;
            slot = -1;
            detail = "";

            GlobalSettings settings = _settings();
            if (settings == null || !settings.DeathSalvagePrompt)
                return false;
            if (_salvageInFlight)
                return false;
            if (gm == null || gm.playerData == null)
                return false;

            // permadeathMode 2 means HeroController.Die already flipped this run to "dead" in
            // memory. Nothing on disk has changed yet - orig_PlayerDead has not run.
            if (gm.playerData.permadeathMode != 2)
                return false;

            slot = gm.profileID;
            string livePath = SavePaths.GetLiveSavePath(slot);
            bool liveExists = File.Exists(livePath);
            DateTime? liveWriteUtc = liveExists ? File.GetLastWriteTimeUtc(livePath) : (DateTime?)null;

            List<BackupEntry> backups;
            try
            {
                backups = _backups.CreateStore().ListBackups(slot);
            }
            catch (Exception ex)
            {
                _mod.LogError($"Could not list backups for slot {slot} during death salvage: {ex}");
                backups = new List<BackupEntry>();
            }

            decision = SalvagePolicy.Choose(liveExists, liveWriteUtc, backups);
            if (!decision.CanSalvage)
            {
                _mod.LogWarn($"Steel Soul death on slot {slot}: nothing to salvage " +
                             $"(live save {(liveExists ? "present" : "missing")}, " +
                             $"{backups.Count} backup(s)); vanilla death proceeds.");
                return false;
            }

            detail = DescribeCandidate(decision, liveWriteUtc);
            _mod.Log($"Steel Soul death on slot {slot}: offering salvage " +
                     $"[source={decision.Source}, reason={decision.Reason}, {detail}]");
            return true;
        }

        /// <summary>
        /// The prompt coroutine. It is returned to HeroController.Die's StartCoroutine, so it
        /// runs on the hero object - fine for the prompt (no scene change happens while it is
        /// open), which is why the post-choice work is handed to the mod's own persistent object.
        /// </summary>
        private IEnumerator RunPrompt(On.GameManager.orig_PlayerDead orig, GameManager self,
            float waitTime, int slot, SalvageDecision decision, string detail)
        {
            SalvagePromptOverlay overlay = null;
            double seconds = 20;
            try
            {
                overlay = EnsureOverlay();
                seconds = _settings()?.DeathSalvagePromptSeconds ?? 20;
                seconds = Math.Min(MaxPromptSeconds, Math.Max(MinPromptSeconds, seconds));
                overlay.BeginPrompt(detail, (float)seconds);
            }
            catch (Exception ex)
            {
                SafeLogError($"Could not open the death-salvage prompt; the death proceeds as vanilla: {ex}");
                overlay = null;
            }

            if (overlay == null)
            {
                yield return orig(self, waitTime);
                yield break;
            }

            // The overlay resolves itself on its own timeout; this outer deadline covers the case
            // where it stops updating entirely (destroyed, disabled, an exception in Update).
            // A death must never hang the game waiting for a prompt that will never answer.
            float promptDeadline = Time.realtimeSinceStartup + (float)seconds + 15f;
            while (overlay.Result == SalvagePromptOverlay.Choice.None &&
                   Time.realtimeSinceStartup < promptDeadline)
            {
                yield return null;
            }

            SalvagePromptOverlay.Choice choice = overlay.Result;
            overlay.EndPrompt();

            if (choice != SalvagePromptOverlay.Choice.Salvage)
            {
                _mod.Log($"Death salvage declined for slot {slot}; running the vanilla death sequence.");
                // The vanilla enumerator, unmodified: death save, then the PermaDeath scene.
                yield return orig(self, waitTime);
                yield break;
            }

            bool handedOff = false;
            try
            {
                _salvageInFlight = true;
                ArmSaveSuppression();

                // Belt and braces only - the file on disk is what matters and nothing will be
                // written. Clearing the in-memory "dead" flag keeps the session's PlayerData
                // from claiming a death that is being undone.
                try { self.playerData.permadeathMode = 1; }
                catch (Exception ex) { _mod.LogWarn($"Could not clear in-memory permadeathMode: {ex}"); }

                overlay.ShowNotice("Salvaging run - returning to the main menu...", 20f);
                overlay.StartCoroutine(FinishSalvage(self, slot, decision));
                handedOff = true;
            }
            catch (Exception ex)
            {
                SafeLogError($"Death salvage could not start: {ex}");
            }

            if (!handedOff)
            {
                // Could not take over the flow: undo the latch and let the game do what it
                // always does, rather than leaving the player in a half-dead session.
                DisarmSaveSuppression("salvage failed to start");
                _salvageInFlight = false;
                yield return orig(self, waitTime);
            }
        }

        /// <summary>
        /// Runs on the mod's persistent object so it survives the quit-to-menu scene load
        /// (HeroController, which owns the prompt coroutine, does not).
        /// </summary>
        private IEnumerator FinishSalvage(GameManager gm, int slot, SalvageDecision decision)
        {
            try
            {
                bool quitStarted = false;
                try
                {
                    // DontSave: verified in the decompile to skip the SaveGame call entirely.
                    gm.StartCoroutine(gm.ReturnToMainMenu(GameManager.ReturnToMainMenuSaveModes.DontSave));
                    quitStarted = true;
                    _mod.Log($"Death salvage for slot {slot}: quitting to the main menu without saving.");
                }
                catch (Exception ex)
                {
                    SafeLogError($"Death salvage could not start the quit-to-menu: {ex}");
                }

                bool atMenu = false;
                if (quitStarted)
                {
                    float deadline = Time.realtimeSinceStartup + ReturnToMenuTimeoutSeconds;
                    while (Time.realtimeSinceStartup < deadline)
                    {
                        bool reached = false;
                        try { reached = RestoreService.IsAtMainMenu(); }
                        catch (Exception) { /* transient during the scene swap */ }
                        if (reached)
                        {
                            atMenu = true;
                            break;
                        }
                        yield return null;
                    }
                    // Let the menu settle before touching save files or the save-select cache.
                    for (int i = 0; atMenu && i < 10; i++)
                        yield return null;
                }

                ApplySalvage(slot, decision, atMenu);
            }
            finally
            {
                // Whatever happened, saving must work again: the session past this point is
                // either the main menu or a freshly loaded save.
                DisarmSaveSuppression("salvage finished");
                _salvageInFlight = false;
            }
        }

        private void ApplySalvage(int slot, SalvageDecision decision, bool atMenu)
        {
            try
            {
                if (!atMenu)
                {
                    _mod.LogError($"Death salvage for slot {slot}: never reached the main menu, so no file " +
                                  "was touched. The death save was still prevented - the slot on disk is " +
                                  "your last save. Restart the game and load it.");
                    ShowNotice("Salvage incomplete - restart the game; your last save is intact on disk.", 25f);
                    return;
                }

                if (decision.Source == SalvageSource.LiveSlotFile)
                {
                    // The whole salvage: we stopped the death save, so the slot file is still
                    // the last good commit. No write at all is the safest possible restore.
                    _mod.Log($"Death salvage for slot {slot} complete: the death save was prevented and the " +
                             $"slot file was left untouched ({decision.Reason}).");
                    ShowNotice("Run salvaged - your last save is intact. Start Game to pick it back up.", 20f);
                    return;
                }

                if (decision.Backup == null)
                {
                    _mod.LogError($"Death salvage for slot {slot}: backup candidate went missing; " +
                                  "the slot was left as it is.");
                    return;
                }

                _mod.Log($"Death salvage for slot {slot}: restoring {decision.Backup.DatPath} ({decision.Reason}).");
                RestoreResult result = _restore.Restore(decision.Backup);
                ShowNotice(result.Success
                    ? "Run salvaged from a backup. Fully exit the game before loading it (Steam Cloud)."
                    : "Salvage could not restore the backup - see ModLog.txt. Nothing was overwritten.", 25f);
            }
            catch (Exception ex)
            {
                SafeLogError($"Death salvage for slot {slot} failed while applying the result: {ex}");
            }
        }

        private void ArmSaveSuppression()
        {
            _suppressSaveCommits = true;
            _suppressArmedAtRealtime = Time.realtimeSinceStartup;
            _mod.Log("Death salvage: save commits are latched off until the main menu is reached.");
        }

        private void DisarmSaveSuppression(string why)
        {
            if (!_suppressSaveCommits)
                return;
            _suppressSaveCommits = false;
            try { _mod.Log($"Death salvage: save commits re-enabled ({why})."); }
            catch (Exception) { /* logging must not throw on the way out */ }
        }

        private void ShowNotice(string text, float seconds)
        {
            try { _overlay?.ShowNotice(text, seconds); }
            catch (Exception) { /* the log line is the real record */ }
        }

        private SalvagePromptOverlay EnsureOverlay()
        {
            if (_overlay != null)
                return _overlay;

            // Created on first use, so a session that never dies (or never enables the toggle)
            // carries no extra GameObject.
            var host = new GameObject("HKSaveBackup_DeathSalvage");
            UnityEngine.Object.DontDestroyOnLoad(host);
            _overlay = host.AddComponent<SalvagePromptOverlay>();
            return _overlay;
        }

        private static string DescribeCandidate(SalvageDecision decision, DateTime? liveWriteUtc)
        {
            if (decision.Source == SalvageSource.Backup && decision.Backup != null)
            {
                BackupEntry e = decision.Backup;
                string scene = e.Metadata != null ? e.Metadata.Scene : e.Scene;
                return $"Restores backup from {FormatLocal(e.TimestampUtc)}" +
                       (string.IsNullOrEmpty(scene) ? "" : $" ({scene})");
            }

            return liveWriteUtc.HasValue
                ? $"Returns you to your last save from {FormatLocal(liveWriteUtc.Value)}"
                : "Returns you to your last save";
        }

        private static string FormatLocal(DateTime utc) =>
            DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        private void SafeLogError(string message)
        {
            try { _mod.LogError(message); }
            catch (Exception) { /* never throw into the death sequence */ }
        }
    }
}
