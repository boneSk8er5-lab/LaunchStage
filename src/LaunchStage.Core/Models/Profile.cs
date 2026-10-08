using System.Text.Json.Serialization;

namespace LaunchStage.Core.Models;

/// <summary>A saved workspace: which apps belong in it, where each window goes, and what happens to everything else.</summary>
public sealed class Profile
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    // Launcher button look (used by the GUI later).
    public string? Icon { get; set; }
    public string Color { get; set; } = "#00ADB5";
    public string? Image { get; set; }
    public bool Favorite { get; set; }

    // Private profile: a salted hash of the 4-digit PIN (see ProfilePin). Null = not private.
    public string? PinHash { get; set; }
    public string? PinSalt { get; set; }

    [JsonIgnore]
    public bool IsPrivate => !string.IsNullOrEmpty(PinHash);

    // Hotkeys, saved as text like "Ctrl+Alt+1". Null = none.
    /// <summary>Opens the profile, or closes it when it's already open.</summary>
    public string? Hotkey { get; set; }

    /// <summary>Puts the profile's open windows back in their spots (re-snap).</summary>
    public string? ResnapHotkey { get; set; }

    // Options.
    /// <summary>When the profile is ready, pop up the game picker (installed Steam / Epic games plus added ones).</summary>
    public bool ShowSuggestedGames { get; set; }
    public OtherAppsAction OtherApps { get; set; } = OtherAppsAction.Minimize;
    /// <summary>No longer used: this is now the app-wide AfterActivating setting. Kept so older files still load.</summary>
    public AfterActivating AfterActivating { get; set; } = AfterActivating.Tray;
    /// <summary>
    /// Off (default): each app opens, finishes loading and is put in place before the next one starts.
    /// On: every app starts at the same time (faster on powerful PCs, laggy on others).
    /// </summary>
    public bool LaunchAllAtOnce { get; set; }

    /// <summary>No longer used (one at a time is now the default). Kept so older files still load.</summary>
    public bool LaunchOneAtATime { get; set; }

    /// <summary>Seconds to wait before a second positioning pass, for apps that move themselves after opening. 0 turns it off.</summary>
    public int ReapplyDelaySeconds { get; set; } = 3;

    /// <summary>When the profile is closed, put the apps that stay open back where they were before it started.</summary>
    public bool RestoreKeptApps { get; set; } = true;

    /// <summary>
    /// Before closing, move the profile's windows back to their saved spots, so apps that remember their window
    /// positions remember the profile's layout instead of wherever the windows were dragged.
    /// </summary>
    public bool ResetPositionsBeforeClosing { get; set; } = true;

    /// <summary>Apps in launch order. Closing also follows this order.</summary>
    public List<AppEntry> Apps { get; set; } = new();
}

/// <summary>One app (or one window of an app) inside a profile.</summary>
public sealed class AppEntry
{
    public string Name { get; set; } = "";
    public AppType Type { get; set; } = AppType.Program;
    public AppBehavior Behavior { get; set; } = AppBehavior.LaunchAndPosition;

    /// <summary>What to open: an .exe, a shortcut, a URL, or a launcher link such as steam://rungameid/123.</summary>
    public string Path { get; set; } = "";

    /// <summary>Extra launch arguments, e.g. a project folder for VS Code.</summary>
    public string? Arguments { get; set; }

    public string? WorkingDirectory { get; set; }

    /// <summary>The process whose window belongs to this app (e.g. "javaw" for Minecraft). Taken from Path when empty.</summary>
    public string? ProcessName { get; set; }

    /// <summary>Optional: only match windows whose title contains this text.</summary>
    public string? TitleContains { get; set; }

    /// <summary>Optional: only match windows with this window class.</summary>
    public string? WindowClass { get; set; }

    /// <summary>
    /// The window's title when it was added to the profile. Used to tell an app's windows apart when only some of
    /// them should be closed (e.g. close an overlay app's main window and let it close its own overlays).
    /// </summary>
    public string? CapturedTitle { get; set; }

    public bool RunAsAdmin { get; set; }

    /// <summary>Browsers only: open (and match) a private / incognito window instead of a normal one.</summary>
    public bool PrivateWindow { get; set; }

    /// <summary>
    /// Browsers only: the websites this window opens on (one tab each), in a window of its own. Empty or null means
    /// the browser opens whatever it normally shows at startup.
    /// </summary>
    public List<string>? Websites { get; set; }

    /// <summary>Close this app when the profile is closed.</summary>
    public bool CloseWithProfile { get; set; } = true;

    /// <summary>Games only: launch the game when the profile activates.</summary>
    public bool AutoStart { get; set; }

    public double LaunchDelaySeconds { get; set; }
    public int LaunchTimeoutSeconds { get; set; } = 30;

    /// <summary>Where the window goes. Null means the app only opens.</summary>
    public WindowPosition? Position { get; set; }
}

/// <summary>A window position saved relative to a specific monitor, so it survives monitor rearranging.</summary>
public sealed class WindowPosition
{
    /// <summary>Windows device name, e.g. \\.\DISPLAY1.</summary>
    public string Monitor { get; set; } = "";

    /// <summary>For reading the file only; LaunchStage matches monitors by name and size.</summary>
    public int MonitorNumber { get; set; }

    public Rect MonitorBounds { get; set; } = new();

    /// <summary>Offset from the monitor's top-left corner.</summary>
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public WindowState State { get; set; } = WindowState.Normal;
}
