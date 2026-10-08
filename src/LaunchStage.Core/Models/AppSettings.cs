namespace LaunchStage.Core.Models;

/// <summary>App-wide settings, stored in %AppData%\LaunchStage\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Process names no profile will ever close or minimize, e.g. "KeePass" or "Spotify".</summary>
    public List<string> NeverClose { get; set; } = new();

    public bool StartWithWindows { get; set; } = true;

    /// <summary>
    /// LaunchStage runs with administrator rights (approved once, through two scheduled tasks), so it can move
    /// and close admin apps. Normal apps are still started without admin rights.
    /// </summary>
    public bool AdminMode { get; set; }

    /// <summary>When off, closing the window exits LaunchStage instead of hiding it in the tray.</summary>
    public bool ShowTrayIcon { get; set; } = true;

    /// <summary>What LaunchStage does after a profile finishes: stay open, minimize, or go to the tray.</summary>
    public AfterActivating AfterActivating { get; set; } = AfterActivating.Tray;

    /// <summary>The "Getting started" walkthrough has been shown (it's shown once; Help can open it again).</summary>
    public bool WelcomeShown { get; set; }

    /// <summary>Hotkey that opens the game picker, e.g. "Ctrl+Alt+G". Null = none.</summary>
    public string? GamePickerHotkey { get; set; }

    public bool ColorBlindMode { get; set; }
    /// <summary>Colors: "Dark", "Light" or "System" (match Windows' light/dark app setting).</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>Launcher look: "Cards", "SmallCards", "List" or "Tiles".</summary>
    public string LauncherLayout { get; set; } = "Cards";
}
