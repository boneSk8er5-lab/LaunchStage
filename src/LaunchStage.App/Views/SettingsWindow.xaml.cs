using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LaunchStage.Core.Desktop;
using LaunchStage.Core.Engine;
using LaunchStage.Core.Models;
using LaunchStage.Core.Storage;
using LaunchStageApp.Services;
using Microsoft.Win32;

namespace LaunchStageApp.Views;

public partial class SettingsWindow : Window
{
    private readonly List<string> _neverClose;

    /// <summary>The new settings when the user pressed Save; null if cancelled. Applied by the app after this closes.</summary>
    public AppSettings? SavedSettings { get; private set; }
    private List<AppChoice> _catalog = new();

    public SettingsWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        MaxHeight = SystemParameters.WorkArea.Height - 40; // taller than that, it scrolls
        CopyrightText.Text = $"LaunchStage {AppInfo.Version} · {AppInfo.Copyright}";

        var settings = App.Instance.Settings;
        ThemeDark.IsChecked = settings.Theme is not ("Light" or "System");
        ThemeLight.IsChecked = settings.Theme == "Light";
        ThemeSystem.IsChecked = settings.Theme == "System";
        LayoutCards.IsChecked = settings.LauncherLayout is not ("SmallCards" or "List" or "Tiles");
        LayoutSmall.IsChecked = settings.LauncherLayout == "SmallCards";
        LayoutList.IsChecked = settings.LauncherLayout == "List";
        LayoutTiles.IsChecked = settings.LauncherLayout == "Tiles";
        StartWithWindowsBox.IsChecked = settings.StartWithWindows;
        TrayBox.IsChecked = settings.ShowTrayIcon;
        ColorBlindBox.IsChecked = settings.ColorBlindMode;
        AdminBox.IsChecked = settings.AdminMode;
        ShowAdminStatus(settings.AdminMode);

        AfterTray.IsChecked = settings.AfterActivating == AfterActivating.Tray;
        AfterMinimize.IsChecked = settings.AfterActivating == AfterActivating.Minimize;
        AfterStay.IsChecked = settings.AfterActivating is AfterActivating.StayOpen or AfterActivating.Exit;

        HotkeyText.Attach(GameHotkeyBox);
        GameHotkeyBox.Text = HotkeyText.Normalize(settings.GamePickerHotkey) ?? "";

        _neverClose = settings.NeverClose.ToList();
        RenderNeverClose();

        Loaded += async (_, _) =>
        {
            _catalog = await AppCatalog.LoadAsync();
            CatalogStatus.Text = $"{_catalog.Count} apps found. Type to search.";
            RenderNeverClose(); // show friendly names now that we know them
        };
    }

    private void ShowAdminStatus(bool adminMode)
    {
        if (adminMode && AdminTasks.IsElevated)
        {
            AdminStatus.Text = "✓  On: LaunchStage is running with administrator rights.";
            AdminStatus.SetResourceReference(TextBlock.ForegroundProperty, "GoodBrush");
        }
        else if (adminMode)
        {
            AdminStatus.Text = "!  On, but this copy isn't running as administrator. Exit LaunchStage from the tray and open it again.";
            AdminStatus.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
        }
        else
        {
            AdminStatus.Text = "Off: apps that run as administrator can't be moved or closed.";
            AdminStatus.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        }
    }

    // ---------------- Never close: chips ----------------

    private void RenderNeverClose()
    {
        NeverClosePanel.Children.Clear();
        foreach (string processName in _neverClose)
        {
            NeverClosePanel.Children.Add(MakeChip(processName));
        }

        NeverCloseEmpty.Visibility = _neverClose.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private UIElement MakeChip(string processName)
    {
        string? friendly = _catalog
            .FirstOrDefault(c => string.Equals(c.ProcessName, processName, StringComparison.OrdinalIgnoreCase))?.Name;
        string label = friendly != null && !string.Equals(friendly, processName, StringComparison.OrdinalIgnoreCase)
            ? $"{friendly}  ({processName})"
            : processName;

        var remove = new Button
        {
            Content = "✕",
            Style = (Style)FindResource("ChipRemoveButton"),
            ToolTip = "Remove"
        };
        remove.Click += (_, _) =>
        {
            _neverClose.Remove(processName);
            RenderNeverClose();
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(remove);

        var chip = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 6),
            Child = row
        };
        chip.SetResourceReference(Border.BackgroundProperty, "ButtonBrush"); // follows the theme
        return chip;
    }

    private void AddNeverClose(string processName)
    {
        string name = WindowMatcher.StripExe(processName.Trim());
        if (name.Length > 0 && !_neverClose.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _neverClose.Add(name);
            RenderNeverClose();
        }

        AppSearchBox.Clear();
        SuggestionPopup.IsOpen = false;
        AppSearchBox.Focus();
    }

    // ---------------- Never close: search ----------------

    private void AppSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        string text = AppSearchBox.Text;
        AppSearchPlaceholder.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        var matches = AppCatalog.Search(
            _catalog.Where(c => !_neverClose.Contains(c.ProcessName, StringComparer.OrdinalIgnoreCase)),
            text);
        SuggestionList.ItemsSource = matches;
        SuggestionPopup.IsOpen = matches.Count > 0;
    }

    private void AppSearch_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when SuggestionPopup.IsOpen && SuggestionList.Items.Count > 0:
                SuggestionList.SelectedIndex = 0;
                (SuggestionList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
                e.Handled = true;
                break;

            case Key.Enter:
                // Enter picks the top suggestion, or adds exactly what was typed.
                if (SuggestionList.Items.Count > 0 && SuggestionPopup.IsOpen && SuggestionList.Items[0] is AppChoice first)
                {
                    AddNeverClose(first.ProcessName);
                }
                else if (AppSearchBox.Text.Trim().Length > 0)
                {
                    AddNeverClose(AppSearchBox.Text);
                }

                e.Handled = true; // don't trigger Save
                break;

            case Key.Escape when SuggestionPopup.IsOpen:
                SuggestionPopup.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void SuggestionList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is AppChoice choice)
        {
            AddNeverClose(choice.ProcessName);
        }
    }

    private void SuggestionList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SuggestionList.SelectedItem is AppChoice choice)
        {
            AddNeverClose(choice.ProcessName);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SuggestionPopup.IsOpen = false;
            AppSearchBox.Focus();
            e.Handled = true;
        }
    }

    private void BrowseApp_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "Pick an app LaunchStage should never close",
            Filter = "Programs (*.exe)|*.exe"
        };

        if (picker.ShowDialog(this) != true)
        {
            return;
        }

        string processName = Path.GetFileNameWithoutExtension(picker.FileName);
        if (!_catalog.Any(c => string.Equals(c.ProcessName, processName, StringComparison.OrdinalIgnoreCase)))
        {
            _catalog.Add(new AppChoice(AppNames.Get(picker.FileName, processName), processName, picker.FileName, false));
        }

        AddNeverClose(processName);
    }

    // ---------------- Save ----------------

    /// <summary>A small ? next to a section: opens the guide at that topic (its Tag).</summary>
    private void Help_Click(object sender, RoutedEventArgs e) => HelpWindow.ShowTopic((sender as FrameworkElement)?.Tag as string);

    private void Copyright_Click(object sender, MouseButtonEventArgs e) => AppInfo.OpenLicense();

    private void ClearGameHotkey_Click(object sender, RoutedEventArgs e) => GameHotkeyBox.Text = "";

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string? gameHotkey = HotkeyText.Normalize(GameHotkeyBox.Text);
        if (gameHotkey != null && HotkeyText.FindOwner(gameHotkey, null, includeSettings: false) is { } owner)
        {
            HotkeyError.Text = $"{gameHotkey} is already used for {owner}. Pick another one.";
            HotkeyError.Visibility = Visibility.Visible;
            GameHotkeyBox.Focus();
            return;
        }

        var current = App.Instance.Settings;
        var updated = new AppSettings
        {
            StartWithWindows = StartWithWindowsBox.IsChecked == true,
            ShowTrayIcon = TrayBox.IsChecked == true,
            ColorBlindMode = ColorBlindBox.IsChecked == true,
            AdminMode = AdminBox.IsChecked == true,
            AfterActivating = AfterStay.IsChecked == true ? AfterActivating.StayOpen
                : AfterMinimize.IsChecked == true ? AfterActivating.Minimize
                : AfterActivating.Tray,
            Theme = ThemeLight.IsChecked == true ? "Light" : ThemeSystem.IsChecked == true ? "System" : "Dark",
            LauncherLayout = LayoutSmall.IsChecked == true ? "SmallCards"
                : LayoutList.IsChecked == true ? "List"
                : LayoutTiles.IsChecked == true ? "Tiles"
                : "Cards",
            GamePickerHotkey = gameHotkey,
            WelcomeShown = current.WelcomeShown,
            NeverClose = _neverClose.ToList()
        };

        SavedSettings = updated;
        DialogResult = true;
    }

    // ---------------- Profiles ----------------

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileTransfer.Import(this))
        {
            App.Instance.RefreshMain();
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ExportButton, Placement = PlacementMode.Bottom };
        var profiles = ProfileStore.LoadAll().OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();

        if (profiles.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No profiles yet", IsEnabled = false });
        }

        foreach (var profile in profiles)
        {
            var item = new MenuItem { Header = profile.Name };
            var chosen = profile;
            item.Click += (_, _) => ProfileTransfer.Export(this, chosen);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureFolders();
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.ProfilesFolder}\"") { UseShellExecute = true });
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(AppPaths.LogFile))
        {
            Process.Start(new ProcessStartInfo(AppPaths.LogFile) { UseShellExecute = true });
        }
        else
        {
            Dialogs.Info(this, "The log is empty so far.");
        }
    }
}
