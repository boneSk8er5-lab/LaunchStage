using System.Windows;
using LaunchStageApp.Services;

namespace LaunchStageApp.Views;

/// <summary>"Remove LaunchStage": asks first, then removes everything LaunchStage added to this PC (see Uninstaller).</summary>
public partial class UninstallWindow : Window
{
    public UninstallWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);

        ProgramFilesText.Text = Uninstaller.CanRemoveProgramFiles
            ? $"•  The LaunchStage program folder is deleted ({Uninstaller.ProgramFolder})"
            : $"•  This copy's program folder stays; delete it yourself afterwards: {Uninstaller.ProgramFolder}";
        StreamDeckText.Visibility = Uninstaller.StreamDeckPluginInstalled ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>True when the user went ahead (LaunchStage then exits).</summary>
    public bool Removed { get; private set; }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (App.Instance.Runner.IsBusy)
        {
            BusyText.Visibility = Visibility.Visible;
            return;
        }

        var problems = Uninstaller.Run(DeleteProfilesBox.IsChecked == true);
        string message = "LaunchStage has been removed. The last files disappear a few seconds after this closes.";
        if (!Uninstaller.CanRemoveProgramFiles)
        {
            message += $"\n\nDelete this folder yourself to finish: {Uninstaller.ProgramFolder}";
        }

        if (problems.Count > 0)
        {
            message += "\n\nNot everything could be removed:\n\n" + string.Join("\n\n", problems.Select(p => "•  " + p));
        }

        Dialogs.Info(this, message);
        Removed = true;
        DialogResult = true;
    }
}
