using LaunchStage.Core.Engine;

namespace LaunchStage.Cli;

/// <summary>Shows activation progress in the console and asks the user when an app won't close.</summary>
internal sealed class ConsoleUi : IActivationUi
{
    public void Status(string message) => Console.WriteLine("  " + message);

    public CloseFailureChoice AskCloseFailed(string appName)
    {
        Console.WriteLine();
        Program.WriteColor($"  {appName} didn't close.", ConsoleColor.Yellow);

        if (Console.IsInputRedirected)
        {
            Console.WriteLine("  No keyboard available, so leaving it open and continuing.");
            return CloseFailureChoice.LeaveOpenAndContinue;
        }

        Console.WriteLine("  [T] Try again    [L] Leave it open & continue    [S] Stop profile");
        while (true)
        {
            var key = Console.ReadKey(intercept: true).Key;
            switch (key)
            {
                case ConsoleKey.T:
                    return CloseFailureChoice.TryAgain;
                case ConsoleKey.L:
                    return CloseFailureChoice.LeaveOpenAndContinue;
                case ConsoleKey.S:
                    return CloseFailureChoice.StopProfile;
            }
        }
    }
}
