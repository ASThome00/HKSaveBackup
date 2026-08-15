using System;
using System.Collections;
using GlobalEnums;
using UnityEngine;

namespace HKSaveBackup
{
    /// <summary>
    /// Drives the game into loading a save slot along the same path the vanilla save-select
    /// uses, so a restored save can be played without backing out through the menus.
    ///
    /// Vanilla sequence (read out of the decompiled 1.5.78.11833 assembly):
    ///   SaveSlotButton.OnSubmit, when saveFileState == LoadedStats and
    ///   saveStats.permadeathMode != 2, calls GameManager.LoadGameFromUI(slot). That starts
    ///   GameManager.LoadGameFromUIRoutine, which runs UIManager.ContinueGame() (stops UI
    ///   input, plays the start-game sting, stops the menu music, hides the save-profile
    ///   menu), then GameManager.LoadGame(slot, Action&lt;bool&gt;) and waits for the
    ///   callback; on true it calls GameManager.ContinueGame() (RunContinueGame fades out,
    ///   MakeMenuLean, GameState.LOADING, loads Knight_Pickup, ReadyForRespawn), on false
    ///   UIManager.UIGoToMainMenu(). LoadGame is what assigns GameManager.profileID.
    ///
    /// Two things this mod has to add, because the submit comes from a dynamic mod menu and
    /// not from a SaveSlotButton:
    ///   * SaveSlotButton's own guards. saveFileState comes from Prepare(), which reads the
    ///     slot with GameManager.GetSaveStatsForSlot(slot, Action&lt;SaveStats&gt;) — an async
    ///     callback that yields null for an unreadable/corrupt file. OnSubmit refuses to
    ///     load a permadeathMode == 2 (shattered Steel Soul) save and offers to clear it
    ///     instead. We re-read the freshly restored file the same way and apply the same
    ///     two guards before committing to the load.
    ///   * Taking the menu down. UIManager.ContinueGame only hides the menu when
    ///     menuState == SAVE_PROFILES, and UIManager.GoToMainMenu (the failed-load path)
    ///     has no DYNAMIC_MENU branch either, so a mod menu left standing would hang over
    ///     the game or over the title screen. We fade it out with UIManager.HideCurrentMenu
    ///     and hand menuState back to MAIN_MENU before the hand-off.
    ///
    /// Everything here is exception-safe and every failure before the hand-off falls back
    /// to the caller's result screen with the menu still intact.
    /// </summary>
    internal sealed class SaveLoadService
    {
        /// <summary>
        /// Ceiling on the wait for GetSaveStatsForSlot. The read is a local file plus a
        /// JSON parse, so this only ever fires if the platform layer never calls back —
        /// in which case falling back beats hanging the menu forever.
        /// </summary>
        private const float StatsTimeoutSeconds = 15f;

        private readonly Modding.ILogger _log;

        public SaveLoadService(Modding.ILogger log)
        {
            _log = log;
        }

        /// <summary>
        /// Start loading <paramref name="slot"/>.
        ///
        /// Returns false when the load could not even be started; nothing on screen has
        /// changed and the caller owns the UI. Returns true when the attempt is running:
        /// either it reaches the game's load path, or <paramref name="onFailure"/> is
        /// invoked with a user-facing message while the menu is still up.
        /// </summary>
        public bool BeginLoad(int slot, Action<string> onFailure)
        {
            try
            {
                if (slot < 1 || slot > 4)
                {
                    _log.LogError($"Refusing to load save slot {slot}: not a valid slot index.");
                    return false;
                }

                // Same rule as restore, for the same reason plus one more: loading is only
                // meaningful from the title screen, and a save is already live otherwise.
                if (!RestoreService.IsAtMainMenu())
                {
                    _log.LogWarn($"Refusing to load slot {slot}: not at the main menu.");
                    return false;
                }

                GameManager gm = GameManager.instance;
                UIManager ui = UIManager.instance;
                if (gm == null || ui == null)
                {
                    _log.LogError($"Refusing to load slot {slot}: GameManager/UIManager unavailable.");
                    return false;
                }

                gm.StartCoroutine(LoadRoutine(gm, ui, slot, onFailure));
                return true;
            }
            catch (Exception ex)
            {
                _log.LogError($"Could not start the load of slot {slot}: {ex}");
                return false;
            }
        }

        private IEnumerator LoadRoutine(GameManager gm, UIManager ui, int slot, Action<string> onFailure)
        {
            // The menu is still on screen and still interactive while the slot is read;
            // freeze input so the player cannot navigate out from under the hand-off.
            SetUIInput(gm, enabled: false);

            SaveStats stats = null;
            bool statsReturned = false;
            string error = TryRequestStats(gm, slot, s => { stats = s; statsReturned = true; });
            if (error != null)
            {
                AbortBeforeHandoff(gm, onFailure, error);
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + StatsTimeoutSeconds;
            while (!statsReturned && Time.realtimeSinceStartup < deadline)
                yield return null;

            error = DescribeStatsProblem(slot, statsReturned, stats);
            if (error != null)
            {
                AbortBeforeHandoff(gm, onFailure, error);
                yield break;
            }

            // Everything fallible is behind us; from here the menu comes down, and a
            // failure inside the game's own load routine is handled by the game (it
            // returns to the main menu, which menuState is set up for below).
            if (!TryBeginHideMenu(ui, out IEnumerator hide, out error))
            {
                AbortBeforeHandoff(gm, onFailure, error);
                yield break;
            }
            if (hide != null)
                yield return ui.StartCoroutine(hide);

            error = TryHandOff(gm, ui, slot);
            if (error != null)
            {
                // The menu is already gone, so there is nothing to fall back to except the
                // title screen — which is still a working UI, unlike a blank canvas.
                _log.LogError(error);
                TryGoToMainMenu(ui);
            }
        }

        private string TryRequestStats(GameManager gm, int slot, Action<SaveStats> callback)
        {
            try
            {
                gm.GetSaveStatsForSlot(slot, callback);
                return null;
            }
            catch (Exception ex)
            {
                _log.LogError($"GetSaveStatsForSlot({slot}) threw: {ex}");
                return "the restored save file could not be read - see ModLog.txt";
            }
        }

        private static string DescribeStatsProblem(int slot, bool statsReturned, SaveStats stats)
        {
            if (!statsReturned)
                return $"timed out reading slot {slot} back from disk";
            if (stats == null)
                return "the game reports the restored save file as unreadable";
            if (stats.permadeathMode == 2)
                return "the restored save is a Steel Soul run that has already died, " +
                       "and the game refuses to load those";
            return null;
        }

        /// <summary>
        /// Fade the mod's dynamic menu out the way the API's own navigation does, so the
        /// hand-off does not leave a menu floating over the game.
        /// </summary>
        private bool TryBeginHideMenu(UIManager ui, out IEnumerator hide, out string error)
        {
            hide = null;
            error = null;
            try
            {
                if (ui.menuState != MainMenuState.DYNAMIC_MENU || ui.currentDynamicMenu == null)
                {
                    // Not standing on a mod screen (nothing to hide, or the player was moved
                    // elsewhere); leave whatever menu state exists alone.
                    return true;
                }
                hide = ui.HideCurrentMenu();
                return true;
            }
            catch (Exception ex)
            {
                _log.LogError($"Could not hide the mod menu before loading: {ex}");
                error = "the menu could not be closed - see ModLog.txt";
                return false;
            }
        }

        private string TryHandOff(GameManager gm, UIManager ui, int slot)
        {
            try
            {
                // UIManager.GoToMainMenu has no DYNAMIC_MENU branch, so if the load fails
                // and LoadGameFromUIRoutine falls back to UIGoToMainMenu, this is what makes
                // it fade the title screen back in instead of doing nothing.
                ui.menuState = MainMenuState.MAIN_MENU;

                _log.Log($"Loading slot {slot} via GameManager.LoadGameFromUI (vanilla save-select path).");
                gm.LoadGameFromUI(slot);
                return null;
            }
            catch (Exception ex)
            {
                return $"Hand-off to GameManager.LoadGameFromUI({slot}) failed: {ex}";
            }
        }

        private void AbortBeforeHandoff(GameManager gm, Action<string> onFailure, string message)
        {
            _log.LogWarn($"Load after restore aborted: {message}.");
            SetUIInput(gm, enabled: true);
            try
            {
                onFailure?.Invoke(message);
            }
            catch (Exception ex)
            {
                _log.LogError($"Fallback UI after an aborted load failed: {ex}");
            }
        }

        private void SetUIInput(GameManager gm, bool enabled)
        {
            try
            {
                InputHandler ih = gm.inputHandler;
                if (ih == null)
                    return;
                if (enabled)
                    ih.StartUIInput();
                else
                    ih.StopUIInput();
            }
            catch (Exception ex)
            {
                _log.LogError($"Could not toggle UI input: {ex}");
            }
        }

        private void TryGoToMainMenu(UIManager ui)
        {
            try
            {
                ui.UIGoToMainMenu();
            }
            catch (Exception ex)
            {
                _log.LogError($"Could not return to the main menu after a failed load: {ex}");
            }
        }
    }
}
