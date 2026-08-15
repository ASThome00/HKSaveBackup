using System;
using System.Reflection;
using HKSaveBackup.Core;
using Modding;

namespace HKSaveBackup
{
    /// <summary>
    /// Automatic save-file backups for Hollow Knight 1.5.x, aimed at Steel Soul runs.
    /// Backups are taken after the game finishes committing a save to disk; restore is
    /// offered from the mod menu while on the main menu. The mod is gameplay-inert.
    /// </summary>
    public class HKSaveBackupMod : Mod, ITogglableMod, IGlobalSettings<GlobalSettings>, ICustomMenuMod
    {
        internal static HKSaveBackupMod Instance { get; private set; }

        private GlobalSettings _settings = new GlobalSettings();
        private BackupService _backupService;
        private RestoreService _restoreService;
        private ModMenu _menu;

        /// <summary>The API renders the on/off toggle in the mod list itself.</summary>
        public bool ToggleButtonInsideMenu => false;

        public HKSaveBackupMod() : base("HKSaveBackup")
        {
        }

        public override string GetVersion() =>
            Assembly.GetExecutingAssembly().GetName().Version.ToString();

        public void OnLoadGlobal(GlobalSettings settings) => _settings = settings ?? new GlobalSettings();

        public GlobalSettings OnSaveGlobal() => _settings;

        internal GlobalSettings Settings => _settings;

        internal BackupService BackupService => _backupService;

        public override void Initialize()
        {
            Instance = this;
            _backupService = new BackupService(this, () => _settings, new RealFileSystem());
            _restoreService = new RestoreService(this, _backupService);
            _menu = new ModMenu(this, _restoreService, new SaveLoadService(this));

            // SaveGame(int, Action<bool>) is the single commit path: both parameterless
            // overloads funnel into it, and it hands the platform layer a completion
            // callback. Wrapping that callback is what guarantees we copy the .dat only
            // after the write finished — the legacy ModHooks.SavegameSaveHook fires
            // before the platform write completes on some platforms.
            On.GameManager.SaveGame_int_Action1 += OnSaveGame;

            Log($"Initialized. Backup root: {SavePaths.ResolveBackupRoot(_settings.BackupDirectory)}");
        }

        public void Unload()
        {
            On.GameManager.SaveGame_int_Action1 -= OnSaveGame;
            Instance = null;
            Log("Unloaded; save hook removed.");
        }

        public MenuScreen GetMenuScreen(MenuScreen modListMenu, ModToggleDelegates? toggleDelegates)
        {
            // Built lazily in Initialize-order safety: the mod list constructs menus after
            // all mods have initialized, so _menu is non-null by the time this runs.
            return _menu.BuildRootScreen(modListMenu);
        }

        private void OnSaveGame(On.GameManager.orig_SaveGame_int_Action1 orig, GameManager self,
            int saveSlot, Action<bool> callback)
        {
            Action<bool> wrapped;
            try
            {
                BackupService service = _backupService;
                wrapped = didSave =>
                {
                    // Backup first, then hand control back to the game. OnSaveCommitted is
                    // exception-safe, so the game's callback always runs.
                    service.OnSaveCommitted(self, saveSlot, didSave);
                    callback?.Invoke(didSave);
                };
            }
            catch (Exception ex)
            {
                LogError($"Failed to wrap save callback; saving proceeds without backup: {ex}");
                wrapped = callback;
            }
            orig(self, saveSlot, wrapped);
        }
    }
}
