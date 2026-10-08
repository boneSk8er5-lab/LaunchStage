namespace LaunchStage.Core.Engine;

public enum CloseFailureChoice
{
    TryAgain,
    LeaveOpenAndContinue,
    StopProfile
}

/// <summary>
/// How the engine talks to whoever is running it. The command line implements this now;
/// the GUI's status popup will implement it later.
/// </summary>
public interface IActivationUi
{
    /// <summary>Progress, e.g. "Waiting for Inkscape... (1 of 3)".</summary>
    void Status(string message);

    /// <summary>Called when an app stayed open after being asked to close (e.g. the user pressed Cancel).</summary>
    CloseFailureChoice AskCloseFailed(string appName);
}
