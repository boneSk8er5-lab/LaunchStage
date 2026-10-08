using System.Windows;

namespace LaunchStageApp.Services;

/// <summary>Small message boxes, with or without an owner window (the tray has none).</summary>
internal static class Dialogs
{
    public static void Info(Window? owner, string text) =>
        Show(owner, text, MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Warn(Window? owner, string text) =>
        Show(owner, text, MessageBoxButton.OK, MessageBoxImage.Warning);

    public static bool Confirm(Window? owner, string text) =>
        Show(owner, text, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private static MessageBoxResult Show(Window? owner, string text, MessageBoxButton buttons, MessageBoxImage image) =>
        owner != null
            ? MessageBox.Show(owner, text, "LaunchStage", buttons, image)
            : MessageBox.Show(text, "LaunchStage", buttons, image);
}
