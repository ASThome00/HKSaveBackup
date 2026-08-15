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
    /// </summary>
    internal sealed class ModMenu
    {
        private readonly HKSaveBackupMod _mod;
        private readonly RestoreService _restore;
        private readonly SaveLoadService _loader;

        private MenuScreen _rootScreen;
        private MenuScreen _slotListScreen;
        private MenuScreen _confirmScreen;
        private MenuScreen _resultScreen;

        /// <summary>
        /// Screens replaced by a rebuild. Destroying one while it is still the screen the
        /// player is standing on breaks UIManager's next transition (it fades out
        /// currentDynamicMenu), so they are dropped one navigation later instead.
        /// </summary>
        private readonly List<MenuScreen> _retiredScreens = new List<MenuScreen>();

        private static readonly MenuButtonStyle RowStyle = new MenuButtonStyle
        {
            Height = new RelLength(105f),
            TextSize = 30,
        };

        public ModMenu(HKSaveBackupMod mod, RestoreService restore, SaveLoadService loader)
        {
            _mod = mod;
            _restore = restore;
            _loader = loader;
        }

        public MenuScreen BuildRootScreen(MenuScreen modListMenu)
        {
            MenuBuilder builder = MenuUtils.CreateMenuBuilderWithBackButton(
                "HKSaveBackup", modListMenu, out MenuButton backButton);

            List<IMenuMod.MenuEntry> options = BuildSettingsEntries();
            const float rowPitch = 105f;
            int rowCount = options.Count + 4;

            builder.AddContent(default(NullContentLayout), c => c.AddScrollPaneContent(
                new ScrollbarConfig
                {
                    CancelAction = _ => UIManager.instance.UIGoToDynamicMenu(modListMenu),
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
                    MenuUtils.AddModMenuContent(options, c2, modListMenu);
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
                            SubmitAction = _ => OpenSlotList(capturedSlot),
                            CancelAction = _ => UIManager.instance.UIGoToDynamicMenu(modListMenu),
                            Proceed = true,
                        });
                    }
                }));

            _rootScreen = builder.Build();
            return _rootScreen;
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

        private void OpenSlotList(int slot)
        {
            RetireScreen(ref _slotListScreen);
            RetireScreen(ref _confirmScreen);
            RetireScreen(ref _resultScreen);
            DestroyRetiredScreens();

            MenuBuilder builder = MenuUtils.CreateMenuBuilderWithBackButton(
                $"Slot {slot} Backups", _rootScreen, out MenuButton backButton);

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
                    // Ordering is Core's rule, not the menu's: the shortcut below picks
                    // from the same sequence the rows are drawn from.
                    entries = BackupSelection.OrderNewestFirst(
                        _mod.BackupService.CreateStore().ListBackups(slot));
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
                    // Newest non-snapshot backup: the one-action "put this run back the way
                    // it was" target. Null when the slot only ever received pre-restore
                    // snapshots, in which case the shortcut is simply not offered.
                    BackupEntry latest = BackupSelection.LatestReloadCandidate(entries);
                    int rowCount = entries.Count + (latest != null ? 1 : 0);

                    builder.AddContent(default(NullContentLayout), c => c.AddScrollPaneContent(
                        new ScrollbarConfig
                        {
                            CancelAction = _ => UIManager.instance.UIGoToDynamicMenu(_rootScreen),
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
                        new RelLength(rowCount * 105f),
                        RegularGridLayout.CreateVerticalLayout(105f),
                        c2 =>
                        {
                            if (latest != null)
                            {
                                BackupEntry capturedLatest = latest;
                                c2.AddMenuButton("RestoreLatestAndLoad", new MenuButtonConfig
                                {
                                    Label = "Restore Latest & Load",
                                    Description = new DescriptionInfo
                                    {
                                        Text = $"Roll slot {slot} back to {FormatEntryLabel(latest)} and start playing it",
                                    },
                                    SubmitAction = _ => OpenConfirm(capturedLatest, loadAfterRestore: true),
                                    CancelAction = _ => UIManager.instance.UIGoToDynamicMenu(_rootScreen),
                                    Proceed = true,
                                    Style = RowStyle,
                                });
                            }

                            foreach (BackupEntry entry in entries)
                            {
                                BackupEntry captured = entry;
                                c2.AddMenuButton(entry.BaseName, new MenuButtonConfig
                                {
                                    Label = FormatEntryLabel(entry),
                                    Description = new DescriptionInfo { Text = FormatEntryDescription(entry) },
                                    SubmitAction = _ => OpenConfirm(captured, loadAfterRestore: false),
                                    CancelAction = _ => UIManager.instance.UIGoToDynamicMenu(_rootScreen),
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

        /// <summary>
        /// The confirmation step, identical for both routes into a restore — the shortcut
        /// only preselects the backup, it does not skip the confirmation or the Steam Cloud
        /// warning.
        /// </summary>
        private void OpenConfirm(BackupEntry entry, bool loadAfterRestore)
        {
            RetireScreen(ref _confirmScreen);
            RetireScreen(ref _resultScreen);
            DestroyRetiredScreens();

            MenuBuilder builder = MenuUtils.CreateMenuBuilderWithBackButton(
                "Confirm Restore", _slotListScreen, out _);

            string text =
                $"Restore slot {entry.Slot} to:\n<b>{FormatEntryLabel(entry)}</b>\n\n" +
                $"The current contents of slot {entry.Slot} will be snapshotted into the backup " +
                "folder first, so nothing is lost.\n\n";
            if (loadAfterRestore)
            {
                text += $"Slot {entry.Slot} will then be loaded straight away.\n\n";
            }
            text +=
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
                    Label = loadAfterRestore ? "Restore & Load" : "Restore Now",
                    SubmitAction = _ => DoRestore(entry, loadAfterRestore),
                    CancelAction = _ => UIManager.instance.UIGoToDynamicMenu(_slotListScreen),
                    Proceed = true,
                }));

            _confirmScreen = builder.Build();
            UIManager.instance.UIGoToDynamicMenu(_confirmScreen);
        }

        private void DoRestore(BackupEntry entry, bool loadAfterRestore)
        {
            RestoreResult result = _restore.Restore(entry);

            // Loading is only ever attempted on a reported success; on any other outcome,
            // and on any failure to get the load started, the result screen is the fallback.
            if (result.Success && loadAfterRestore)
                TryLoad(entry, result);
            else
                ShowResult(entry, result, null);
        }

        /// <summary>
        /// Hand the restored slot to the game's load path. Anything that stops the load —
        /// now or after the asynchronous slot re-read — lands on the result screen with the
        /// reason, so the UI is never left mid-transition.
        /// </summary>
        private void TryLoad(BackupEntry entry, RestoreResult result)
        {
            try
            {
                if (_loader.BeginLoad(entry.Slot, problem => ShowResult(entry, result, problem)))
                    return;
            }
            catch (Exception ex)
            {
                _mod.LogError($"Load of slot {entry.Slot} could not be started: {ex}");
            }

            ShowResult(entry, result, "the load could not be started - see ModLog.txt");
        }

        private void ShowResult(BackupEntry entry, RestoreResult result, string loadProblem)
        {
            RetireScreen(ref _resultScreen);
            DestroyRetiredScreens();

            MenuBuilder builder = MenuUtils.CreateMenuBuilderWithBackButton(
                result.Success ? "Restore Complete" : "Restore Failed", _rootScreen, out _);

            string text = result.Message;
            if (loadProblem != null)
                text += $"\n\n<b>The save was not loaded:</b> {loadProblem}. The restore itself stands.";
            if (result.Success)
            {
                text += "\n\nFully exit Hollow Knight before loading the restored save, so Steam Cloud " +
                        "syncs the restored file instead of racing it. If the save select still shows the " +
                        "old state after a restart, follow the Steam Cloud steps from the confirmation screen.";
            }

            // Offering the load is gated on the same rule the load itself enforces: main
            // menu only. Anywhere else the button would just refuse itself.
            bool offerLoad = result.Success && RestoreService.IsAtMainMenu();

            AddInfoText(builder, text, offerLoad ? 0.68f : 0.6f, offerLoad ? 550f : 600f);

            if (offerLoad)
            {
                builder.AddContent(
                    new SingleContentLayout(new AnchoredPosition(
                        new Vector2(0.5f, 0.12f), new Vector2(0.5f, 0.5f))),
                    c => c.AddMenuButton("LoadRestoredSave", new MenuButtonConfig
                    {
                        Label = "Load This Save Now",
                        Description = new DescriptionInfo
                        {
                            Text = $"Start playing slot {entry.Slot} from the restored file",
                        },
                        SubmitAction = _ => TryLoad(entry, result),
                        CancelAction = _ => UIManager.instance.UIGoToDynamicMenu(_rootScreen),
                        Proceed = true,
                    }));
            }

            _resultScreen = builder.Build();
            UIManager.instance.UIGoToDynamicMenu(_resultScreen);
        }

        private static void AddInfoText(MenuBuilder builder, string text, float anchorY = 0.6f, float height = 600f)
        {
            builder.AddContent(
                new SingleContentLayout(new AnchoredPosition(
                    new Vector2(0.5f, anchorY), new Vector2(0.5f, 0.5f))),
                c => c.AddTextPanel("InfoText",
                    new RelVector2(new Vector2(1500f, height)),
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

        private void RetireScreen(ref MenuScreen screen)
        {
            if (screen != null)
                _retiredScreens.Add(screen);
            screen = null;
        }

        /// <summary>Destroy every retired screen the player is not currently standing on.</summary>
        private void DestroyRetiredScreens()
        {
            MenuScreen current = UIManager.instance != null ? UIManager.instance.currentDynamicMenu : null;
            for (int i = _retiredScreens.Count - 1; i >= 0; i--)
            {
                MenuScreen screen = _retiredScreens[i];
                if (screen != null && screen == current)
                    continue;
                if (screen != null && screen.gameObject != null)
                    UnityEngine.Object.Destroy(screen.gameObject);
                _retiredScreens.RemoveAt(i);
            }
        }
    }
}
