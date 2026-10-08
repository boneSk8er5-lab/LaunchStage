namespace LaunchStage.Core.Models;

/// <summary>Programs are positioned by default; games are launch-only by default.</summary>
public enum AppType
{
    Program,
    Game
}

public enum AppBehavior
{
    /// <summary>Open it if it isn't running, then move it into place.</summary>
    LaunchAndPosition,

    /// <summary>Move it if it's running; leave it alone if it isn't.</summary>
    PositionOnly,

    /// <summary>Open it if it isn't running, but never move it.</summary>
    LaunchOnly
}

public enum WindowState
{
    Normal,
    Maximized,
    Minimized
}

/// <summary>What happens to open apps that aren't part of the profile being activated.</summary>
public enum OtherAppsAction
{
    Leave,
    Minimize,
    Close
}

/// <summary>What LaunchStage itself does once a profile is finished (an app-wide setting).</summary>
public enum AfterActivating
{
    Tray,
    Minimize,
    Exit,
    StayOpen
}
