using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LaunchStage.Core.Engine;
using LaunchStage.Core.Storage;

namespace LaunchStageApp.Views;

/// <summary>
/// The small corner popup that shows progress ("Waiting for Inkscape... (1 of 3)"), asks what to do when an
/// app won't close, and reports the result. It never takes focus away from the apps being set up.
/// </summary>
public partial class StatusPopup : Window
{
    private TaskCompletionSource<CloseFailureChoice>? _choice;
    private DispatcherTimer? _autoClose;
    private readonly bool _closing;
    private readonly bool _resnapping;

    /// <param name="resnapping">Re-snap: only putting the profile's windows back in their spots.</param>
    public StatusPopup(string profileName, bool closing = false, bool resnapping = false)
    {
        InitializeComponent();
        _closing = closing;
        _resnapping = resnapping;
        TitleText.Text = resnapping ? $"Putting {profileName} back in place"
            : closing ? $"Closing {profileName}"
            : $"Setting up {profileName}";
        StatusText.Text = "Starting...";

        SizeChanged += (_, _) => PlaceInCorner();
        Closed += (_, _) =>
        {
            _autoClose?.Stop();
            _choice?.TrySetResult(CloseFailureChoice.LeaveOpenAndContinue);
        };
    }

    public void SetStatus(string text) => StatusText.Text = text;

    public Task<CloseFailureChoice> AskCloseFailedAsync(string appName)
    {
        _choice = new TaskCompletionSource<CloseFailureChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        ClosePromptText.Text = $"!  {appName} didn't close.";
        ClosePrompt.Visibility = Visibility.Visible;
        Activate();
        return _choice.Task;
    }

    public void ShowResult(ActivationResult result, string profileName)
    {
        ClosePrompt.Visibility = Visibility.Collapsed;

        if (result.Stopped)
        {
            SetTitle($"!  {profileName} was stopped", "WarnBrush");
            StatusText.Text = _closing ? "Some apps were left open." : "Nothing was opened.";
            StartAutoClose(4);
            return;
        }

        bool clean = result.Problems.Count == 0;
        SetTitle(clean
                ? (_resnapping ? $"✓  {profileName} is back in place"
                    : _closing ? $"✓  {profileName} is closed"
                    : $"✓  {profileName} is ready")
                : $"✕  {profileName} finished with {result.Problems.Count} problem{(result.Problems.Count == 1 ? "" : "s")}",
            clean ? "GoodBrush" : "BadBrush");

        if (clean && result.Notes.Count == 0)
        {
            StatusText.Text = _resnapping ? "Every open window is in its spot."
                : _closing ? "Its apps are closed."
                : "Everything is in place.";
            StartAutoClose(2.5);
            return;
        }

        StatusText.Visibility = Visibility.Collapsed;
        foreach (string note in result.Notes)
        {
            AddMessage("!  " + note, "WarnBrush");
        }

        foreach (string problem in result.Problems)
        {
            AddMessage("✕  " + problem, "BadBrush");
        }

        Messages.Visibility = Visibility.Visible;
        FinishButtons.Visibility = Visibility.Visible;
        if (clean)
        {
            StartAutoClose(10);
        }
    }

    public void ShowProblem(string message)
    {
        SetTitle("✕  LaunchStage", "BadBrush");
        StatusText.Visibility = Visibility.Collapsed;
        AddMessage(message, "BadBrush");
        Messages.Visibility = Visibility.Visible;
        FinishButtons.Visibility = Visibility.Visible;
    }

    private void SetTitle(string text, string brushKey)
    {
        TitleText.Text = text;
        TitleText.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
    }

    private void AddMessage(string text, string brushKey)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        Messages.Children.Add(block);
    }

    private void StartAutoClose(double seconds)
    {
        _autoClose = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _autoClose.Tick += (_, _) =>
        {
            _autoClose?.Stop();
            Close();
        };
        _autoClose.Start();
    }

    private void PlaceInCorner()
    {
        Rect area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 8;
        Top = area.Bottom - ActualHeight - 8;
    }

    private void Resolve(CloseFailureChoice choice)
    {
        ClosePrompt.Visibility = Visibility.Collapsed;
        var pending = _choice;
        _choice = null;
        pending?.TrySetResult(choice);
    }

    private void TryAgain_Click(object sender, RoutedEventArgs e) => Resolve(CloseFailureChoice.TryAgain);

    private void LeaveOpen_Click(object sender, RoutedEventArgs e) => Resolve(CloseFailureChoice.LeaveOpenAndContinue);

    private void Stop_Click(object sender, RoutedEventArgs e) => Resolve(CloseFailureChoice.StopProfile);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(AppPaths.LogFile))
        {
            Process.Start(new ProcessStartInfo(AppPaths.LogFile) { UseShellExecute = true });
        }
    }
}
