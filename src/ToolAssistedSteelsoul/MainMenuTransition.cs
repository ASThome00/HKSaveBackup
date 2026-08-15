using System.Collections;
using GlobalEnums;
using UnityEngine;

namespace ToolAssistedSteelsoul
{
    /// <summary>
    /// Moves between the title screen and one of the mod's dynamic menus.
    ///
    /// The API's own navigation only ever travels between dynamic menus, and both vanilla
    /// transitions have a hole exactly where the title screen is (decompiled 1.5.78.11833):
    ///   * UIManager.GoToDynamicMenu hides whatever is on screen with HideCurrentMenu, whose
    ///     switch has no MainMenuState.MAIN_MENU case and so falls through hiding nothing —
    ///     the mod screen fades in on top of the title screen, whose buttons stay live
    ///     underneath.
    ///   * UIManager.GoToMainMenu only hides a fixed list of states (OPTIONS_MENU,
    ///     ACHIEVEMENTS_MENU, QUIT_GAME_PROMPT, EXTRAS_MENU, ENGAGE_MENU, NO_SAVE_MENU,
    ///     PLAY_MODE_MENU) and DYNAMIC_MENU is not among them, so a "back" that calls
    ///     UIGoToMainMenu leaves the mod screen standing.
    ///
    /// Both directions are closed here by running the missing half of the fade — the same
    /// one vanilla runs in GoToOptionsMenu when it leaves the title screen — and then handing
    /// off to the vanilla coroutines for everything else (currentDynamicMenu, menuState, UI
    /// input, and the button column's default highlight).
    /// </summary>
    internal static class MainMenuTransition
    {
        /// <summary>Title screen -> <paramref name="screen"/>.</summary>
        public static void Enter(MenuScreen screen)
        {
            UIManager ui = UIManager.instance;
            if (ui == null || screen == null)
                return;
            ui.StartCoroutine(EnterRoutine(ui, screen));
        }

        /// <summary>Whatever dynamic menu is up -> title screen.</summary>
        public static void Leave()
        {
            UIManager ui = UIManager.instance;
            if (ui == null)
                return;
            ui.StartCoroutine(LeaveRoutine(ui));
        }

        private static IEnumerator EnterRoutine(UIManager ui, MenuScreen screen)
        {
            // Only the title screen needs the hand-rolled fade; arriving from anywhere else
            // (the mod list, say) is a state GoToDynamicMenu already knows how to leave.
            if (ui.menuState == MainMenuState.MAIN_MENU)
                yield return HideMainMenu(ui);

            yield return ui.GoToDynamicMenu(screen);
        }

        private static IEnumerator LeaveRoutine(UIManager ui)
        {
            // HideCurrentMenu does handle DYNAMIC_MENU (it fades out currentDynamicMenu and
            // raises BeforeHideDynamicMenu); it is only GoToMainMenu that never calls it for
            // this state. Calling it first leaves GoToMainMenu with nothing to hide, which is
            // the one shape of its own hide-list it copes with.
            if (ui.menuState == MainMenuState.DYNAMIC_MENU && ui.currentDynamicMenu != null)
                yield return ui.HideCurrentMenu();

            // Re-activates and fades in the game title and the button column, restores the
            // default highlight, and sets menuState back to MAIN_MENU.
            yield return ui.GoToMainMenu();
        }

        /// <summary>
        /// The title-screen half of a vanilla MAIN_MENU -> submenu transition, copied from
        /// UIManager.GoToOptionsMenu: the logo and subtitle fade alongside the button column
        /// rather than after it, so only the canvas group is waited on.
        /// </summary>
        private static IEnumerator HideMainMenu(UIManager ui)
        {
            if (ui.gameTitle != null)
                ui.StartCoroutine(FadeOutSprite(ui, ui.gameTitle));

            if (ui.subtitleFSM != null)
                ui.subtitleFSM.SendEvent("FADE OUT");

            if (ui.mainMenuScreen != null)
                yield return ui.FadeOutCanvasGroup(ui.mainMenuScreen);
        }

        /// <summary>
        /// UIManager.FadeOutSprite is private, so this is the same loop against the same
        /// public speed field. GoToMainMenu fades the sprite back in on the way out.
        /// </summary>
        private static IEnumerator FadeOutSprite(UIManager ui, SpriteRenderer sprite)
        {
            while (sprite.color.a > 0f)
            {
                Color c = sprite.color;
                sprite.color = new Color(c.r, c.g, c.b, c.a - Time.unscaledDeltaTime * ui.MENU_FADE_SPEED);
                yield return null;
            }

            Color done = sprite.color;
            sprite.color = new Color(done.r, done.g, done.b, 0f);
        }
    }
}
