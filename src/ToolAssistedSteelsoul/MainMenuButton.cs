using System;
using System.Collections.Generic;
using Modding.Menu;
using Modding.Menu.Config;
using UnityEngine;
using UnityEngine.UI;

namespace ToolAssistedSteelsoul
{
    /// <summary>
    /// Adds a "Save Backups" entry to the title screen's button column, alongside Start Game,
    /// Options and Quit. Everything here is best-effort: the vanilla menu is a scene object we
    /// do not own, so every step is guarded and a failure leaves the menu exactly as it was.
    /// </summary>
    internal sealed class MainMenuButton
    {
        /// <summary>Name of the injected GameObject; doubles as the "already injected" marker.</summary>
        private const string ButtonName = "ToolAssistedSteelsoulMainMenuButton";

        private const string Label = "Save Backups";

        private readonly ToolAssistedSteelsoulMod _mod;
        private readonly ModMenu _menu;

        private bool _hooked;

        public MainMenuButton(ToolAssistedSteelsoulMod mod, ModMenu menu)
        {
            _mod = mod;
            _menu = menu;
        }

        /// <summary>
        /// UIManager raises EditMenus from its Awake, once the vanilla menu objects exist but
        /// before MenuButtonList.Start wires navigation. Subscribing late is safe: the event's
        /// accessor replays the call when the UIManager is already up, which is what happens
        /// when the mod is toggled back on from inside the menu.
        /// </summary>
        public void Hook()
        {
            if (_hooked)
                return;
            UIManager.EditMenus += Inject;
            _hooked = true;
        }

        public void Unhook()
        {
            if (_hooked)
            {
                UIManager.EditMenus -= Inject;
                _hooked = false;
            }
            Disable();
        }

        private void Inject()
        {
            try
            {
                InjectCore();
            }
            catch (Exception ex)
            {
                _mod.LogError($"Could not add the main menu button; the title screen is unchanged: {ex}");
            }
        }

        private void InjectCore()
        {
            UIManager ui = UIManager.instance;
            if (ui == null)
            {
                _mod.LogWarn("No UIManager while editing menus; skipping the main menu button.");
                return;
            }

            MainMenuOptions options = ui.mainMenuButtons;
            Transform column = FindColumn(options);
            if (column == null)
            {
                _mod.LogWarn("Could not find the vanilla title button column; skipping the main menu button.");
                return;
            }

            MenuButtonList list = FindButtonList(options, column);

            // A previous load left its button in place (see Disable): revive that one rather
            // than adding a second, so the MenuButtonList entry stays valid and unique.
            Transform existing = column.Find(ButtonName);
            if (existing != null)
            {
                var revived = existing.GetComponent<MenuButton>();
                if (revived != null)
                {
                    revived.submitAction = _ => OpenSaveManager();
                    revived.gameObject.SetActive(true);
                    Recalculate(list);
                    _mod.Log($"Re-enabled the \"{Label}\" button on the main menu.");
                    return;
                }

                // Not something we recognise - leave it alone rather than stacking on top.
                _mod.LogWarn($"A \"{ButtonName}\" object without a MenuButton is already on the main menu; skipping.");
                return;
            }

            MenuButton button;
            new ContentArea(column.gameObject, new SingleContentLayout(new Vector2(0.5f, 0.5f)))
                .AddMenuButton(ButtonName, new MenuButtonConfig
                {
                    Label = Label,
                    SubmitAction = _ => OpenSaveManager(),
                    CancelAction = _ => UIManager.instance.UIGoToMainMenu(),
                    Proceed = true,
                    Style = MenuButtonStyle.VanillaStyle,
                }, out button);

            PlaceAboveQuit(button, options, column);

            if (list != null)
            {
                // One before the end: the list is in visual order, so this lands above Quit.
                list.AddSelectableEnd(button, 1);
                Recalculate(list);
            }
            else
            {
                _mod.LogWarn("No MenuButtonList on the main menu; wiring navigation by hand.");
                WireNavigationManually(button, options);
            }

            _mod.Log($"Added the \"{Label}\" button to the main menu.");
        }

        /// <summary>
        /// Every vanilla title button is a direct child of MainMenuButtons, so any one of them
        /// identifies the column. Start is the button no platform hides.
        /// </summary>
        private static Transform FindColumn(MainMenuOptions options)
        {
            if (options == null)
                return null;
            MenuButton anchor = options.startButton ?? options.optionsButton ?? options.quitButton;
            return anchor != null ? anchor.transform.parent : null;
        }

        private static MenuButtonList FindButtonList(MainMenuOptions options, Transform column)
        {
            if (options == null)
                return null;
            return options.GetComponent<MenuButtonList>()
                ?? column.GetComponent<MenuButtonList>()
                ?? options.GetComponentInParent<MenuButtonList>();
        }

        /// <summary>
        /// Rebuilding the list's explicit up/down chain is what makes controller and keyboard
        /// navigation see the new button. Before the list's own Start has run this throws
        /// (the component is mid-initialisation), and that is fine - Start picks the entry up.
        /// </summary>
        private void Recalculate(MenuButtonList list)
        {
            if (list == null)
                return;
            try
            {
                list.RecalculateNavigation();
            }
            catch (Exception ex)
            {
                _mod.LogDebug($"MenuButtonList.RecalculateNavigation deferred to its own Start: {ex.Message}");
            }
        }

        /// <summary>
        /// The column is driven by a Unity layout group, so sibling order decides where the
        /// button lands. If a build ever has no layout group the sibling index does nothing,
        /// and the fallback positions the button by hand from the vanilla row pitch.
        /// </summary>
        private static void PlaceAboveQuit(MenuButton button, MainMenuOptions options, Transform column)
        {
            MenuButton quit = options.quitButton;
            if (quit != null && quit.transform.parent == column)
                button.transform.SetSiblingIndex(quit.transform.GetSiblingIndex());
            else
                button.transform.SetAsLastSibling();

            if (column.GetComponent<LayoutGroup>() != null)
                return;

            List<RectTransform> rows = VisibleRows(options);
            if (rows.Count == 0)
                return;

            RectTransform last = rows[rows.Count - 1];
            float pitch = rows.Count >= 2
                ? last.anchoredPosition.y - rows[rows.Count - 2].anchoredPosition.y
                : -MenuButtonStyle.VanillaStyle.Height.Delta;

            var rt = (RectTransform)button.transform;
            rt.anchorMin = last.anchorMin;
            rt.anchorMax = last.anchorMax;
            rt.pivot = last.pivot;
            rt.anchoredPosition = last.anchoredPosition;

            // Take the bottom slot and push the button that owned it down one row.
            last.anchoredPosition += new Vector2(0f, pitch);
        }

        private static void WireNavigationManually(MenuButton button, MainMenuOptions options)
        {
            List<RectTransform> rows = VisibleRows(options);
            if (rows.Count == 0)
                return;

            Selectable below = rows[rows.Count - 1].GetComponent<Selectable>();
            Selectable above = rows.Count >= 2 ? rows[rows.Count - 2].GetComponent<Selectable>() : below;
            if (below == null || above == null)
                return;

            Link(above, button);
            Link(button, below);

            Navigation own = button.navigation;
            own.mode = Navigation.Mode.Explicit;
            button.navigation = own;
        }

        private static void Link(Selectable upper, Selectable lower)
        {
            Navigation up = upper.navigation;
            up.selectOnDown = lower;
            upper.navigation = up;

            Navigation down = lower.navigation;
            down.selectOnUp = upper;
            lower.navigation = down;
        }

        /// <summary>The vanilla title buttons this platform actually shows, top to bottom.</summary>
        private static List<RectTransform> VisibleRows(MainMenuOptions options)
        {
            var rows = new List<RectTransform>();
            MenuButton[] all =
            {
                options.startButton, options.optionsButton, options.achievementsButton,
                options.extrasButton, options.quitButton,
            };
            foreach (MenuButton b in all)
            {
                if (b != null && b.gameObject.activeSelf && b.transform is RectTransform rt)
                    rows.Add(rt);
            }
            rows.Sort((a, b) => b.anchoredPosition.y.CompareTo(a.anchoredPosition.y));
            return rows;
        }

        private void OpenSaveManager()
        {
            try
            {
                _menu.OpenFromMainMenu();
            }
            catch (Exception ex)
            {
                _mod.LogError($"Could not open the save manager from the main menu: {ex}");
            }
        }

        /// <summary>
        /// Hides the button and repairs the up/down chain around it. The GameObject is kept
        /// rather than destroyed: the vanilla MenuButtonList holds a hard reference to it and
        /// offers no way to drop one entry, so a destroyed object would break the next
        /// navigation rebuild. An inactive one stays harmless and can be revived on reload.
        /// </summary>
        private void Disable()
        {
            try
            {
                Transform column = FindColumn(UIManager.instance != null ? UIManager.instance.mainMenuButtons : null);
                Transform existing = column != null ? column.Find(ButtonName) : null;
                if (existing == null)
                    return;

                var button = existing.GetComponent<MenuButton>();
                if (button == null)
                    return;

                button.submitAction = null;

                // Unity's explicit navigation refuses to move onto an inactive Selectable, so
                // without this the row above would dead-end instead of reaching the row below.
                Selectable above = button.navigation.selectOnUp;
                Selectable below = button.navigation.selectOnDown;
                if (above != null && below != null && above != button && below != button)
                    Link(above, below);

                button.gameObject.SetActive(false);
                _mod.Log("Main menu button hidden.");
            }
            catch (Exception ex)
            {
                _mod.LogError($"Could not remove the main menu button: {ex}");
            }
        }
    }
}
