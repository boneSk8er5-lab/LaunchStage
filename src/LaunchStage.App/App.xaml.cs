using System.Windows;
using System.Windows.Threading;
using LaunchStage.Core.Engine;
using LaunchStage.Core.Logging;
using LaunchStage.Core.Models;
using LaunchStage.Core.Storage;
using LaunchStageApp.Services;
using LaunchStageApp.Views;

namespace LaunchStageApp;

/// <summary>
/// Starts LaunchStage. Command-line options:
///   --tray                  start hidden in the tray (used by Start with Windows)
///   --activate "Stream"     activate a profile (works whether or not LaunchStage is already running)
///   --close "Stream"        close a profile's apps
///   --resnap "Stream"       put a profile's open windows back in their spots
///   --toggle "Stream"       open a profile, or close it when it's already open
///   --games                 open the game picker
///   --uninstall             open "Remove LaunchStage" (used by Uninstall LaunchStage.cmd)
///   --export-guide "folder" save the guide as a web page (guide.html + images) in that folder, then exit
///   --exit                 tell the running LaunchStage to exit (used by build.cmd)
///   --setup-admin [--startup] / --remove-admin
///                           internal: run with admin rights to create or remove the Admin support tasks
/// </summary>
public partial class App : Application
{
    private SingleInstance? _singleInstance;
    private TrayIcon? _tray;
    private MainWindow? _main;
    private SettingsWindow? _settingsWindow;
    private readonly HashSet<string> _reportedHotkeyProblems = new(StringComparer.OrdinalIgnoreCase);
    private OpenProfilesFile? _openProfiles;

    public static App Instance => (App)Current;

    internal HotkeyManager? Hotkeys { get; private set; }

    public AppSettings Settings { get; private set; } = new();

    internal ActivationRunner Runner { get; } = new();

    public bool IsExiting { get; private set; }

    public bool HasTray => _tray != null;

    private Window? VisibleMain => _main != null && _main.IsVisible ? _main : null;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        string? activateName = null;
        string? closeName = null;
        string? resnapName = null;
        string? toggleName = null;
        bool gamesRequest = false;
        bool uninstallRequest = false;
        bool startInTray = false;
        bool exitRequest = false;
        bool setupAdmin = false;
        bool removeAdmin = false;
        bool startupFlag = false;
        for (int i = 0; i < e.Args.Length; i++)
        {
            string option = e.Args[i].TrimStart('-', '/').ToLowerInvariant();
            if (option == "tray")
            {
                startInTray = true;
            }
            else if (option == "exit")
            {
                exitRequest = true;
            }
            else if (option == "setup-admin")
            {
                setupAdmin = true;
            }
            else if (option == "remove-admin")
            {
                removeAdmin = true;
            }
            else if (option == "startup")
            {
                startupFlag = true;
            }
            else if (option == "activate" && i + 1 < e.Args.Length)
            {
                activateName = e.Args[i + 1];
                i++;
            }
            else if (option == "close" && i + 1 < e.Args.Length)
            {
                closeName = e.Args[i + 1];
                i++;
            }
            else if (option == "resnap" && i + 1 < e.Args.Length)
            {
                resnapName = e.Args[i + 1];
                i++;
            }
            else if (option == "toggle" && i + 1 < e.Args.Length)
            {
                toggleName = e.Args[i + 1];
                i++;
            }
            else if (option == "games")
            {
                gamesRequest = true;
            }
            else if (option == "uninstall")
            {
                uninstallRequest = true;
            }
            else if (option == "export-guide" && i + 1 < e.Args.Length)
            {
                // Used by build-guide.cmd; works whether or not LaunchStage is running.
                int code = ExportGuide(e.Args[i + 1]);
                Shutdown(code);
                return;
            }
        }

        // Each request is (command, argument), handed to the running copy or done by this one.
        (string Command, string? Argument)? request =
            activateName != null ? ("activate", activateName)
            : closeName != null ? ("close", closeName)
            : resnapName != null ? ("resnap", resnapName)
            : toggleName != null ? ("toggle", toggleName)
            : gamesRequest ? ("games", null)
            : uninstallRequest ? ("uninstall", null)
            : null;

        // Admin support setup: this copy was started with admin rights only to create or remove the tasks.
        if (setupAdmin || removeAdmin)
        {
            bool ok = AdminTasks.IsElevated && (setupAdmin ? AdminTasks.CreateTasks(startupFlag) : AdminTasks.DeleteTasks());
            Shutdown(ok ? 0 : 1);
            return;
        }

        // Already running? Hand the request over and quit.
        _singleInstance = new SingleInstance();
        if (exitRequest)
        {
            if (!_singleInstance.IsFirstInstance)
            {
                SingleInstance.Send("exit", null);
            }

            Shutdown();
            return;
        }

        if (!_singleInstance.IsFirstInstance)
        {
            if (request is { } handOver)
            {
                SingleInstance.Send(handOver.Command, handOver.Argument);
            }
            else if (!startInTray)
            {
                SingleInstance.Send("show", null);
            }

            Shutdown();
            return;
        }

        Settings = SettingsStore.Load();

        // Admin support is on but this copy was opened normally: start the administrator copy
        // (no permission prompt) and pass it whatever was asked for.
        if (Settings.AdminMode && !AdminTasks.IsElevated)
        {
            if (HandOffToAdminCopy(request, startInTray))
            {
                Shutdown();
                return;
            }

            Log.Warn("Admin support is on, but the administrator copy couldn't be started; running without admin rights.");
            _singleInstance = new SingleInstance();
        }

        ThemeManager.Apply(Settings.Theme, Settings.ColorBlindMode);
        StartupManager.Apply(Settings.StartWithWindows, Settings.AdminMode);
        if (Settings.ShowTrayIcon)
        {
            CreateTray();
        }

        Runner.BusyChanged += busy => _main?.SetBusy(busy);
        Runner.Finished += OnActivationFinished;
        _singleInstance.Listen((command, argument) => Dispatcher.InvokeAsync(() => HandleCommand(command, argument)));

        Log.Info($"LaunchStage started{(startInTray ? " in the tray" : "")}{(AdminTasks.IsElevated ? " with administrator rights" : "")}.");

        try
        {
            Hotkeys = new HotkeyManager();
            ReloadHotkeys();
        }
        catch (Exception ex)
        {
            Log.Error($"Hotkeys couldn't be set up: {ex}");
        }

        WriteAppPath();
        _openProfiles = new OpenProfilesFile();

        bool stayHidden = HasTray && (startInTray || request != null);
        if (!stayHidden)
        {
            ShowMainWindow();
        }

        if (request is { } todo)
        {
            // Started only to remove LaunchStage: if that's cancelled, don't stay running.
            _exitIfUninstallCancelled = todo.Command == "uninstall";
            HandleCommand(todo.Command, todo.Argument);
        }
    }

    private bool _exitIfUninstallCancelled;

    /// <summary>"Remove LaunchStage" (Settings, or Uninstall LaunchStage.cmd). Exits LaunchStage when it's done.</summary>
    public void ShowUninstall()
    {
        var window = new UninstallWindow();
        if (VisibleMain is { } owner)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        window.ShowDialog();
        if (window.Removed || _exitIfUninstallCancelled)
        {
            ExitApp();
        }

        _exitIfUninstallCancelled = false;
    }

    private static int ExportGuide(string folder)
    {
        try
        {
            string page = GuideHtml.Write(System.IO.Path.GetFullPath(folder));
            Log.Info($"Saved the guide as a web page: {page}");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't save the guide as a web page: {ex}");
            return 1;
        }
    }

    /// <summary>Notes where LaunchStage.exe is, so the Stream Deck plugin can find it without asking.</summary>
    private static void WriteAppPath()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (exe != null && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                AppPaths.EnsureFolders();
                System.IO.File.WriteAllText(System.IO.Path.Combine(AppPaths.Root, "app-path.txt"), exe);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Couldn't note where LaunchStage is installed: {ex.Message}");
        }
    }

    private bool HandOffToAdminCopy((string Command, string? Argument)? request, bool startInTray)
    {
        if (!AdminTasks.RunTaskExists())
        {
            return false;
        }

        // Release the single-instance lock so the administrator copy can take it.
        _singleInstance?.Dispose();
        _singleInstance = null;

        if (!AdminTasks.StartAdminCopy())
        {
            return false;
        }

        Log.Info("Admin support is on; handed over to the administrator copy.");
        if (request is { } handOver)
        {
            SingleInstance.SendWithRetry(handOver.Command, handOver.Argument, TimeSpan.FromSeconds(15));
        }
        else if (!startInTray)
        {
            SingleInstance.SendWithRetry("show", null, TimeSpan.FromSeconds(15));
        }

        return true;
    }

    private void HandleCommand(string command, string? argument)
    {
        Log.Info($"Received '{command}' {argument}");
        if (command == "activate")
        {
            _ = Runner.RunAsync(argument);
        }
        else if (command == "close")
        {
            _ = Runner.CloseAsync(argument);
        }
        else if (command == "resnap")
        {
            _ = Runner.ResnapAsync(argument);
        }
        else if (command == "toggle" && argument != null)
        {
            ToggleProfile(argument);
        }
        else if (command == "games")
        {
            GamePicker.ShowPicker(null);
        }
        else if (command == "uninstall")
        {
            ShowUninstall();
        }
        else if (command == "exit")
        {
            ExitApp();
        }
        else
        {
            ShowMainWindow();
        }
    }

    // ---------------- Windows ----------------

    public void ShowMainWindow()
    {
        _main ??= new MainWindow();
        _main.RefreshProfiles();
        _main.SetBusy(Runner.IsBusy);

        if (!_main.IsVisible)
        {
            _main.Show();
        }

        if (_main.WindowState == System.Windows.WindowState.Minimized)
        {
            _main.WindowState = System.Windows.WindowState.Normal;
        }

        _main.Activate();

        // First time the window is opened: the "Getting started" walkthrough (once).
        if (!Settings.WelcomeShown && !_welcomeShowing)
        {
            _welcomeShowing = true;
            Dispatcher.InvokeAsync(() => WelcomeWindow.ShowTour(_main), DispatcherPriority.ApplicationIdle);
        }
    }

    private bool _welcomeShowing;

    /// <summary>The walkthrough was finished or skipped: don't show it by itself again.</summary>
    public void MarkWelcomeSeen()
    {
        _welcomeShowing = false;
        if (!Settings.WelcomeShown)
        {
            Settings.WelcomeShown = true;
            SaveSettings();
        }
    }

    public void RefreshMain() => _main?.RefreshProfiles();

    public void OpenSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow();
        if (_main != null && _main.IsVisible)
        {
            _settingsWindow.Owner = _main;
            _settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        AppSettings? saved;
        bool removeRequested;
        try
        {
            _settingsWindow.ShowDialog();
            saved = _settingsWindow.SavedSettings;
            removeRequested = _settingsWindow.RemoveRequested;
        }
        finally
        {
            _settingsWindow = null;
        }

        if (saved != null)
        {
            ApplySettings(saved);
        }

        if (removeRequested)
        {
            ShowUninstall();
        }
    }

    // ---------------- Settings ----------------

    /// <summary>Switches the launcher layout (from the header buttons) and remembers it.</summary>
    public void SetLayout(string layout)
    {
        Settings.LauncherLayout = layout;
        SaveSettings();
        _main?.ApplyLayout();
        Log.Info($"Launcher layout: {layout}.");
    }

    public void SetColorBlind(bool on)
    {
        Settings.ColorBlindMode = on;
        SaveSettings();
        ThemeManager.Apply(Settings.Theme, on);
    }

    public void ApplySettings(AppSettings updated)
    {
        bool turningAdminOn = updated.AdminMode && !Settings.AdminMode;
        bool turningAdminOff = !updated.AdminMode && Settings.AdminMode;

        if (turningAdminOn && !AdminTasks.SetUpWithPermission(updated.StartWithWindows, out string? setupError))
        {
            Dialogs.Warn(VisibleMain, $"Admin support wasn't turned on: {setupError}.");
            updated.AdminMode = false;
            turningAdminOn = false;
        }

        if (turningAdminOff && !AdminTasks.Remove(out string? removeError))
        {
            Dialogs.Warn(VisibleMain, $"Admin support couldn't be fully removed: {removeError}.");
        }

        Settings = updated;
        SaveSettings();
        ThemeManager.Apply(updated.Theme, updated.ColorBlindMode);
        StartupManager.Apply(updated.StartWithWindows, updated.AdminMode);

        if (turningAdminOn &&
            Dialogs.Confirm(VisibleMain, "Admin support is set up.\n\nRestart LaunchStage now with administrator rights?"))
        {
            RestartAsAdmin();
            return;
        }

        if (turningAdminOff && AdminTasks.IsElevated)
        {
            RestartAsNormalApp();
            return;
        }

        if (updated.ShowTrayIcon && _tray == null)
        {
            CreateTray();
        }
        else if (!updated.ShowTrayIcon && _tray != null)
        {
            _tray.Dispose();
            _tray = null;
            ShowMainWindow(); // never leave the app running with nothing visible
        }

        _main?.SyncColorBlindToggle();
        _main?.ApplyLayout();
        ReloadHotkeys();
    }

    // ---------------- Hotkeys ----------------

    /// <summary>
    /// Turns on the hotkeys saved in the settings and profiles (call after anything that may change them).
    /// Hotkeys that another program already uses are reported once.
    /// </summary>
    public void ReloadHotkeys()
    {
        if (Hotkeys == null || IsExiting)
        {
            return;
        }

        var wanted = new List<(string Combo, string What, Action Action)>();
        if (!string.IsNullOrWhiteSpace(Settings.GamePickerHotkey))
        {
            wanted.Add((Settings.GamePickerHotkey, "open the game picker", () => GamePicker.ShowPicker(null)));
        }

        try
        {
            foreach (var profile in ProfileStore.LoadAll())
            {
                string name = profile.Name;
                if (!string.IsNullOrWhiteSpace(profile.Hotkey))
                {
                    wanted.Add((profile.Hotkey, $"open or close '{name}'", () => ToggleProfile(name)));
                }

                if (!string.IsNullOrWhiteSpace(profile.ResnapHotkey))
                {
                    wanted.Add((profile.ResnapHotkey, $"put '{name}' windows back", () => _ = Runner.ResnapAsync(name)));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't read profiles for hotkeys: {ex.Message}");
        }

        var fresh = Hotkeys.Set(wanted).Where(problem => _reportedHotkeyProblems.Add(problem)).ToList();
        if (fresh.Count > 0)
        {
            Dialogs.Warn(VisibleMain,
                "Some hotkeys couldn't be turned on:\n\n" + string.Join("\n", fresh.Select(p => "•  " + p)) +
                "\n\nPick a different key combination for them.");
        }
    }

    /// <summary>A profile's hotkey: opens the profile, or closes it when it's already open.</summary>
    private void ToggleProfile(string name)
    {
        if (Runner.IsBusy)
        {
            Log.Info($"Hotkey for '{name}' ignored: a profile is still being set up.");
            return;
        }

        Profile? profile;
        try
        {
            profile = ProfileStore.Find(name);
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't read '{name}': {ex.Message}");
            return;
        }

        if (profile == null)
        {
            return;
        }

        bool isOpen = WindowSnapshot.IsProfileOpen(profile, LaunchStage.Core.Desktop.WindowFinder.GetAppWindows(includeNotInTaskbar: true));
        Log.Info($"Hotkey for '{name}': it's {(isOpen ? "open, so closing it" : "not open, so opening it")}.");
        _ = isOpen ? Runner.CloseAsync(name) : Runner.RunAsync(name);
    }

    private void SaveSettings()
    {
        try
        {
            SettingsStore.Save(Settings);
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't save settings: {ex.Message}");
        }
    }

    private void CreateTray()
    {
        _tray = new TrayIcon();
        _tray.OpenRequested += ShowMainWindow;
        _tray.SettingsRequested += OpenSettings;
        _tray.GamePickerRequested += () => GamePicker.ShowPicker(null);
        _tray.HelpRequested += () => HelpWindow.ShowTopic();
        _tray.ExitRequested += ExitApp;
        _tray.ActivateRequested += name => _ = Runner.RunAsync(name);
        _tray.CloseRequested += name => _ = Runner.CloseAsync(name);
        _tray.ResnapRequested += name => _ = Runner.ResnapAsync(name);
        _tray.IsBusy = () => Runner.IsBusy;
    }

    // ---------------- After a profile runs ----------------

    private void OnActivationFinished(Profile profile, ActivationResult result, StatusPopup popup)
    {
        RefreshMain();
        if (result.Stopped)
        {
            return;
        }

        // "Suggest a game when this profile is ready"
        GamePicker? picker = profile.ShowSuggestedGames ? GamePicker.ShowPicker(profile.Name) : null;

        switch (Settings.AfterActivating)
        {
            case AfterActivating.StayOpen:
                break;

            case AfterActivating.Tray:
                if (_main != null && _main.IsVisible)
                {
                    if (HasTray)
                    {
                        _main.Hide();
                    }
                    else
                    {
                        _main.WindowState = System.Windows.WindowState.Minimized;
                    }
                }

                break;

            case AfterActivating.Minimize:
                if (_main != null && _main.IsVisible)
                {
                    _main.WindowState = System.Windows.WindowState.Minimized;
                }

                break;

            case AfterActivating.Exit:
                if (picker != null)
                {
                    picker.Closed += (_, _) => ExitApp(); // let the game be picked first
                }
                else
                {
                    popup.Closed += (_, _) => ExitApp();
                }

                break;
        }
    }

    // ---------------- Restart with or without admin rights ----------------

    private void RestartAsAdmin()
    {
        Log.Info("Restarting with administrator rights.");
        PrepareForRestart();
        if (AdminTasks.StartAdminCopy())
        {
            SingleInstance.SendWithRetry("show", null, TimeSpan.FromSeconds(15));
        }
        else
        {
            Dialogs.Warn(null, "LaunchStage couldn't restart with administrator rights. Open it again from the Start menu or your shortcut.");
        }

        Shutdown();
    }

    private void RestartAsNormalApp()
    {
        Log.Info("Restarting without administrator rights.");
        PrepareForRestart();

        // Explorer runs as the normal user, so LaunchStage starts without admin rights.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{Environment.ProcessPath}\"")
        {
            UseShellExecute = true
        });
        Shutdown();
    }

    private void PrepareForRestart()
    {
        IsExiting = true;
        _tray?.Dispose();
        _tray = null;
        _openProfiles?.Dispose();
        _openProfiles = null;
        Hotkeys?.Dispose(); // free the hotkeys for the new copy
        Hotkeys = null;
        _singleInstance?.Dispose(); // free the lock for the new copy
        _singleInstance = null;
    }

    // ---------------- Exit ----------------

    public void ExitApp()
    {
        if (IsExiting)
        {
            return;
        }

        IsExiting = true;
        Log.Info("LaunchStage exiting.");
        _tray?.Dispose();
        _tray = null;
        _openProfiles?.Dispose();
        _openProfiles = null;
        Hotkeys?.Dispose();
        Hotkeys = null;
        _singleInstance?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _openProfiles?.Dispose();
        Hotkeys?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error($"Unhandled error: {e.Exception}");
        e.Handled = true;
        if (IsExiting)
        {
            return; // already on the way out: don't block the exit with a message
        }

        MessageBox.Show(
            $"Something went wrong: {e.Exception.Message}\n\nDetails are in {AppPaths.LogFile}",
            "LaunchStage", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
