using System.Windows;
using System.Windows.Input;
using LaunchStage.Core.Desktop;
using LaunchStage.Core.Engine;
using LaunchStageApp.Services;

namespace LaunchStageApp.Views;

/// <summary>Lists the app windows open right now so one can be added to a profile.</summary>
public partial class WindowPicker : Window
{
    public sealed class Row
    {
        public Row(WindowInfo window, string title, string detail)
        {
            Window = window;
            Title = title;
            Detail = detail;
        }

        public WindowInfo Window { get; }
        public string Title { get; }
        public string Detail { get; }
    }

    public WindowPicker()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Load();
    }

    /// <summary>The window that was picked, once the dialog returns true.</summary>
    public WindowInfo? Chosen { get; private set; }

    private void Load()
    {
        var monitors = Monitors.GetAll();
        var rows = new List<Row>();
        foreach (var window in WindowFinder.GetAppWindows(includeNotInTaskbar: true))
        {
            if (window.ProcessName.StartsWith("LaunchStage", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var position = ProfileCapture.CapturePosition(window, monitors);
            string detail = $"Monitor {position.MonitorNumber} · {position.Width}x{position.Height} · {window.State}"
                            + (window.InTaskbar ? "" : " · not in taskbar")
                            + (BrowserPrivacy.IsPrivate(window) ? " · private" : "");
            rows.Add(new Row(window, $"{window.AppName}  —  {window.Title}", detail));
        }

        WindowsList.ItemsSource = rows;
        if (rows.Count > 0)
        {
            WindowsList.SelectedIndex = 0;
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Load();

    private void Add_Click(object sender, RoutedEventArgs e) => Finish();

    private void WindowsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Finish();

    private void Finish()
    {
        if (WindowsList.SelectedItem is not Row row)
        {
            return;
        }

        if (WindowFinder.IsWindowGone(row.Window.Handle))
        {
            Load(); // it closed meanwhile
            return;
        }

        Chosen = row.Window;
        DialogResult = true;
    }
}
