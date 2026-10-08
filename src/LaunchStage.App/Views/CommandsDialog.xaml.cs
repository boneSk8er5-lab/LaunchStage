using System.IO;
using System.Windows;
using System.Windows.Controls;
using LaunchStage.Core.Logging;
using LaunchStageApp.Services;

namespace LaunchStageApp.Views;

/// <summary>Shows the command lines that open or close a profile, ready to copy into a Stream Deck button or shortcut.</summary>
public partial class CommandsDialog : Window
{
    public CommandsDialog(string profileName)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);

        string exe = ExePath();
        TitleText.Text = $"Stream Deck commands for {profileName}";
        ActivateBox.Text = $"\"{exe}\" --activate \"{profileName}\"";
        ToggleBox.Text = $"\"{exe}\" --toggle \"{profileName}\"";
        CloseBox.Text = $"\"{exe}\" --close \"{profileName}\"";
        ResnapBox.Text = $"\"{exe}\" --resnap \"{profileName}\"";
        GamesBox.Text = $"\"{exe}\" --games";
        PathBox.Text = exe;
    }

    /// <summary>LaunchStage.exe's full path (the window app, never the command-line tool).</summary>
    private static string ExePath()
    {
        string path = Environment.ProcessPath ?? "LaunchStage.exe";
        return path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? Path.ChangeExtension(path, ".exe") : path;
    }

    private void CopyActivate_Click(object sender, RoutedEventArgs e) => Copy(ActivateBox, "Open command copied.");

    private void CopyClose_Click(object sender, RoutedEventArgs e) => Copy(CloseBox, "Close command copied.");

    private void CopyResnap_Click(object sender, RoutedEventArgs e) => Copy(ResnapBox, "Put-back command copied.");

    private void CopyToggle_Click(object sender, RoutedEventArgs e) => Copy(ToggleBox, "Open-or-close command copied.");

    private void CopyGames_Click(object sender, RoutedEventArgs e) => Copy(GamesBox, "Game picker command copied.");

    private void CopyPath_Click(object sender, RoutedEventArgs e) => Copy(PathBox, "Program path copied.");

    private void Copy(TextBox box, string message)
    {
        try
        {
            Clipboard.SetText(box.Text);
            CopiedText.Text = message;
        }
        catch (Exception ex)
        {
            // Another app can be holding the clipboard for a moment.
            Log.Warn($"Couldn't copy to the clipboard: {ex.Message}");
            box.Focus();
            box.SelectAll();
            CopiedText.Text = "Couldn't reach the clipboard. The text is selected: press Ctrl+C.";
        }
    }
}
