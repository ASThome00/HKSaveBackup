using System;
using System.Collections.Generic;
using System.Globalization;
using HKSaveBackup.Core;
using Modding;
using Modding.Menu;
using Modding.Menu.Config;
using UnityEngine;
using UnityEngine.UI;

namespace HKSaveBackup
{
    /// <summary>
    /// The mod's menu tree. The root screen (settings + one restore entry per save slot)
    /// is built once by the API's mod list. The per-slot backup list, confirmation, and
    /// result screens are rebuilt from disk on every visit — the backup set changes at
    /// runtime, and the mod list only calls GetMenuScreen once.
    ///
    /// The title-screen button gets its own copy of the root screen, identical except that
    /// "back" returns to the main menu instead of dropping the player into Options → Mods.
    /// </summary>
    internal sealed class ModMenu
    {
        private readonly HKSaveBackupMod _mod;
        private readonly RestoreService _restore;

        private MenuScreen _mainMenuRootScreen;
        private MenuScreen _slotListScreen;
        private MenuScreen _confirmScreen;
        private MenuScreen _resultScreen;

        private static readonly MenuButtonStyle RowStyle = new MenuButtonStyle
        {
            Height = new RelLength(105f),
            TextSize = 30,
        };

        public ModMenu(HKSaveBackupMod mod, RestoreService restore)
        {
            _mod = mod;
            _restore = restore;
        }

        /// <summary>Entry point for the API's mod list, which owns the screen it is given.</summary>
        public MenuScreen BuildRootScreen(MenuScreen modListMenu) =>
            BuildManagerScreen(() => UIManager.instance.UIGoToDynamicMenu(modListMenu));

        /// <summary>
        /// Entry point for the title-screen button. The screen is rebuilt on every press for
        /// the same reason the slot lists are: it is cheap, and it guarantees the settings rows
        /// and slot entries reflect whatever the other entry point last changed.
        /// </summary>
        public void OpenFromMainMenu()
        {
            DestroyScreen(ref _slotListScreen);
            DestroyScreen(ref _confirmScreen);
            DestroyScreen(ref _resultScreen);
            DestroyScreen(ref _mainMenuRootScreen);

            _mainMenuRootScreen = BuildManagerScreen(() => UIManager.instance.UIGoToMainMenu());
            UIManager.instance.UIGoToDynamicMenu(_mainMenuRootScreen);
        }

        /// <summary>
        /// Builds the manager screen. <paramref name="goBack"/> is what "back" and cancel do,
        /// which is the only thing that differs between the mod-list copy and the title-screen
        /// copy. The built screen is captured by the row closures so the sub-screens return
        /// to whichever copy the player actually came in through.
        /// </summary>
        private MenuScreen BuildManagerScreen(Action goBack)
        {
            MenuBuilder builder = CreateBuilderWithBackButton(
                "HKSaveBackup", goBack, out MenuButton backButton);

            List<IMenuMod.MenuEntry> options = BuildSettingsEntries();
            const float rowPitch = 105f;
            int rowCount = options.Count + 4;

            MenuScreen self = null;

            builder.AddContent(default(NullContentLayout), c => c.AddScrollPaneContent(
                new ScrollbarConfig
                {
                    CancelAction = _ => goBack(),
                    Navigation = new Navigation
                    {
                        mode = Navigation.Mode.Explicit,
                        selectOnUp = backButton,
                        selectOnDown = backButton,
                    },
                    Position = new AnchoredPosition
                    {
                        ChildAnchor = new Vector2(0f, 1f),
                        ParentAnchor = new Vector2(1f, 1f),
                        Offset = new Vector2(-310f, 0f),
                    },
                },
                new RelLength(rowCount * rowPitch),
                RegularGridLayout.CreateVerticalLayout(rowPitch),
                c2 =>
                {
                    AddSettingsContent(options, c2, goBack);
                    for (int slot = 1; slot <= 4; slot++)
                    {
                        int capturedSlot = slot;
                        c2.AddMenuButton($"RestoreSlot{slot}", new MenuButtonConfig
                        {
                            Label = $"Restore Slot {slot}",
                            Description = new DescriptionInfo
                            {
                                Text = $"Browse and restore backups of save slot {slot}",
                            },
                            SubmitAction = _ => OpenSlotList(capturedSlot, self),
                            CancelAction = _ => goBack(),
                            Proceed = true,
                        });
                    }
                }));

            self = builder.Build();
            return self;
        }

        /// <summary>
        /// MenuUtils.CreateMenuBuilderWithBackButton only knows how to return to another
        /// MenuScreen; the title-screen copy has to return to the main menu instead. Same
        /// layout and label as the API's version so both copies look identical.
        /// </summary>
        private static MenuBuilder CreateBuilderWithBackButton(
            string title, Action goBack, out MenuButton backButton)
        {
            MenuButton built = null;
            MenuBuilder builder = MenuUtils.CreateMenuBuilder(title).AddControls(
                new SingleContentLayout(new AnchoredPosition(
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -64f))),
                c => c.AddMenuButton("BackButton", new MenuButtonConfig
                {
                    Label = Language.Language.Get("NAV_BACK", "MainMenu"),
                    CancelAction = _ => goBack(),
                    SubmitAction = _ => goBack(),
                    Proceed = true,
                    Style = MenuButtonStyle.VanillaStyle,
                }, out built));
            backButton = built;
            return builder;
        }

        /// <summary>
        /// The settings rows. Equivalent to MenuUtils.AddModMenuContent, but cancelling runs
        /// an action rather than navigating to a fixed MenuScreen.
        /// </summary>
        private static void AddSettingsContent(
            List<IMenuMod.MenuEntry> entries, ContentArea c, Action goBack)
        {
            foreach (IMenuMod.MenuEntry entry in entries)
            {
                IMenuMod.MenuEntry captured = entry;
                c.AddHorizontalOption(captured.Name, new HorizontalOptionConfig
                {
                    ApplySetting = (_, i) => captured.Saver(i),
                    RefreshSetting = (s, _) => s.optionList.SetOptionTo(captured.Loader()),
                    CancelAction = _ => goBack(),
                    Description = string.IsNullOrEmpty(captured.Description)
                        ? (DescriptionInfo?)null
                        : new DescriptionInfo { Text = captured.Description },
                    Label = captured.Name,
                    Options = captured.Values,
                    Style = HorizontalOptionStyle.VanillaStyle,
                }, out MenuOptionHorizontal option);
                option.menuSetting.RefreshValueFromGameSettings();
            }
        }

        private List<IMenuMod.MenuEntry> BuildSettingsEntries()
        {
            GlobalSettings s() => _mod.Settings;
            var retentionValues = new[] { 5, 10, 20, 50, 100 };
            var cooldownValues = new[] { 0, 5, 15, 30, 60 };

            return new List<IMenuMod.MenuEntry>
            {
                new IMenuMod.MenuEntry(
                    "Backups",
                    new[] { "Off", "On" },
                    "Automatically back up saves when the game writes them",
                    i => s().Enabled = i == 1,
                    () => s().Enabled ? 1 : 0),
                new IMenuMod.MenuEntry(
                    "Backup Normal Saves",
                    new[] { "Off", "On" },
                    "Also back up non-Steel-Soul save files",
                    i => s().BackupNormalSaves = i == 1,
                    () => s().BackupNormalSaves ? 1 : 0),
                new IMenuMod.MenuEntry(
                    "Backups Kept Per Slot",
                    Array.ConvertAll(retentionValues, v => v.ToString(CultureInfo.InvariantCulture)),
                    "Oldest backups are deleted beyond this count",
                    i => s().MaxBackupsPerSlot = retentionValues[i],
                    () =>
                    {
                        int idx = Array.IndexOf(retentionValues, s().MaxBackupsPerSlot);
                        return idx >= 0 ? idx : 2; // hand-edited values display as the default, 20
                    }),
                new IMenuMod.MenuEntry(
                    "Backup Cooldown (Minutes)",
                    Array.ConvertAll(cooldownValues, v => v.ToString(CultureInfo.InvariantCulture)),
                    "Minimum time between backups; 0 backs up every save",
                    i => s().CooldownMinutes = cooldownValues[i],
                    () =>
                    {
                        int idx = Array.IndexOf(cooldownValues, (int)s().CooldownMinutes);
                        return idx >= 0 ? idx : 0;
                    }),
            };
        }

        private void OpenSlotList(int slot, MenuScreen root)
        {
            // Root is the current screen here, so the stale dynamic screens are safe to drop.
            DestroyScreen(ref _slotListScreen);
            DestroyScreen(ref _confirmScreen);
            DestroyScreen(ref _resultScreen);

            MenuBuilder builder = MenuUtils.CreateMenuBuilderWithBackButton(
                $"Slot {slot} Backups", root, out MenuButton backButton);

            if (!RestoreService.IsAtMainMenu())
            {
                AddInfoText(builder,
                    "Restore is only available from the main menu.\n\n" +
                    "A loaded save lives in memory and would overwrite the restored file on its next save. " +
                    "Return to the main menu (or restart the game) and try again.");
            }
            else
            {
                List<BackupEntry> entries;
                try
                {
                    entries = _mod.BackupService.CreateStore().ListBackups(slot);
                }
                catch (Exception ex)
                {
                    _mod.LogError($"Could not list backups for slot {slot}: {ex}");
                    entries = new List<BackupEntry>();
                }

                if (entries.Count == 0)
                {
                    AddInfoText(builder,
                        "No backups for this slot yet.\n\n" +
                        "Backups are taken automatically when the game saves a Steel Soul run " +
                        "(rest at a bench, or quit to menu). Normal saves are only backed up if " +
                        "\"Backup Normal Saves\" is enabled.");
                }
                else
                {
                    builder.AddContent(default(NullContentLayout), c => c.AddScrollPaneContent(
                        new ScrollbarConfig
                        {
                            CancelAction = _ => UIManager.instance.UIGoToDynamicMenu(root),
                            Navigation = new Navigation
                            {
                                mode = Navigation.Mode.Explicit,
                                selectOnUp = backButton,
                                selectOnDown = backButton,
                            },
                            Position = new AnchoredPosition
                            {
                                ChildAnchor = new Vector2(0f, 1f),
                                ParentAnchor = new Vector2(1f, 1f),
                                Offset = new Vector2(-310f, 0f),
                            },
                        },
                        new RelLength(entries.Count * 105f),
                        RegularGridLayout.CreateVerticalLayout(105f),
                        c2 =>
                        {
                            foreach (BackupEntry entry in entries)
                            {
                                BackupEntry captured = entry;
                                c2.AddMenuButton(entry.BaseName, new MenuButtonConfig
                                {
                                    Label = FormatEntryLabel(entry),
                                    Description = new DescriptionInfo { Text = FormatEntryDescription(entry) },
                                    SubmitAction = _ => OpenConfirm(captured, root),
                                    CancelAction = _ => UIManager.instance.UIGoToDynamicMenu(root),
                                    Proceed = true,
                                    Style = RowStyle,
                                });
                            }
                        }));
                }
            }

            _slotListScreen = builder.Build();
            UIManager.instance.UIGoToDynamicMenu(_slotListScreen);
        }

        private void OpenConfirm(BackupEntry entry, MenuScreen root)
        {
            DestroyScreen(ref _confirmScreen);
            DestroyScreen(ref _resultScreen);

            MenuBuilder builder = MenuUtils.CreateMenuBuilderWithBackButton(
                "Confirm Restore", _slotListScreen, out _);

            string text =
                $"Restore slot {entry.Slot} to:\n<b>{FormatEntryLabel(entry)}</b>\n\n" +
                $"The current contents of slot {entry.Slot} will be snapshotted into the backup " +
                "folder first, so nothing is lost.\n\n" +
                "<b>Steam Cloud warning:</b> after restoring, fully exit Hollow Knight before loading " +
                "the save. If the restored save does not stick, Steam Cloud has overwritten it: " +
                "quit, disable Steam Cloud in the game's Steam properties, restore again, launch " +
                "once so the local file wins, then re-enable Steam Cloud.";

            builder.AddContent(
                new SingleContentLayout(new AnchoredPosition(
                    new Vector2(0.5f, 0.68f), new Vector2(0.5f, 0.5f))),
                c => c.AddTextPanel("RestoreInfo",
                    new RelVector2(new Vector2(1500f, 550f)),
                    new TextPanelConfig
                    {
                        Text = text,
                        Size = 26,
                        Font = TextPanelConfig.TextFont.Perpetua,
                        Anchor = TextAnchor.MiddleCenter,
                    }));

            builder.AddContent(
                new SingleContentLayout(new AnchoredPosition(
                    new Vector2(0.5f, 0.12f), new Vector2(0.5f, 0.5f))),
                c => c.AddMenuButton("RestoreNow", new MenuButtonConfig
                {
                    Label = "Restore Now",
                    SubmitAction = _ => DoRestore(entry, root),
                    CancelAction = _ => UIManager.instance.UIGoToDynamicMenu(_slotListScreen),
                    Proceed = true,
                }));

            _confirmScreen = builder.Build();
            UIManager.instance.UIGoToDynamicMenu(_confirmScreen);
        }

        private void DoRestore(BackupEntry entry, MenuScreen root)
        {
            RestoreResult result = _restore.Restore(entry);

            DestroyScreen(ref _resultScreen);
            MenuBuilder builder = MenuUtils.CreateMenuBuilderWithBackButton(
                result.Success ? "Restore Complete" : "Restore Failed", root, out _);

            string text = result.Message;
            if (result.Success)
            {
                text += "\n\nFully exit Hollow Knight before loading the restored save, so Steam Cloud " +
                        "syncs the restored file instead of racing it. If the save select still shows the " +
                        "old state after a restart, follow the Steam Cloud steps from the confirmation screen.";
            }

            AddInfoText(builder, text);
            _resultScreen = builder.Build();
            UIManager.instance.UIGoToDynamicMenu(_resultScreen);
        }

        private static void AddInfoText(MenuBuilder builder, string text)
        {
            builder.AddContent(
                new SingleContentLayout(new AnchoredPosition(
                    new Vector2(0.5f, 0.6f), new Vector2(0.5f, 0.5f))),
                c => c.AddTextPanel("InfoText",
                    new RelVector2(new Vector2(1500f, 600f)),
                    new TextPanelConfig
                    {
                        Text = text,
                        Size = 28,
                        Font = TextPanelConfig.TextFont.Perpetua,
                        Anchor = TextAnchor.MiddleCenter,
                    }));
        }

        private static string FormatEntryLabel(BackupEntry entry)
        {
            DateTime local = DateTime.SpecifyKind(entry.TimestampUtc, DateTimeKind.Utc).ToLocalTime();
            string when = local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

            if (entry.IsPreRestoreSnapshot)
                return $"{when}   pre-restore snapshot";

            BackupMetadata m = entry.Metadata;
            if (m == null)
                return $"{when}   {entry.Scene}";
            return $"{when}   {m.Scene}   {m.CompletionPercent.ToString(CultureInfo.InvariantCulture)}%   {m.Geo} geo";
        }

        private static string FormatEntryDescription(BackupEntry entry)
        {
            BackupMetadata m = entry.Metadata;
            if (m == null)
                return entry.IsPreRestoreSnapshot
                    ? "Automatic snapshot taken before a restore overwrote this slot"
                    : "No metadata sidecar; restore uses the raw save file";

            if (m.PreRestoreSnapshot)
                return "Automatic snapshot taken before a restore overwrote this slot";

            TimeSpan playtime = TimeSpan.FromSeconds(Math.Max(0, m.PlaytimeSeconds));
            string mode = m.PermadeathMode == 1 ? "Steel Soul" :
                          m.PermadeathMode == 0 ? "Normal" : $"Mode {m.PermadeathMode}";
            return $"{mode}  -  playtime {(int)playtime.TotalHours}h {playtime.Minutes:D2}m  -  game {m.GameVersion}";
        }

        private static void DestroyScreen(ref MenuScreen screen)
        {
            if (screen != null && screen.gameObject != null)
                UnityEngine.Object.Destroy(screen.gameObject);
            screen = null;
        }
    }
}
