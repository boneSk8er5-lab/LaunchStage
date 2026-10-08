using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LaunchStage.Core.Desktop;
using LaunchStage.Core.Engine;
using LaunchStage.Core.Models;
using LaunchStage.Core.Storage;
using LaunchStageApp.Services;

namespace LaunchStageApp.Views;

/// <summary>
/// Create a profile from the windows open now, or edit a profile's name, look and options.
/// (The full editor with the monitor preview comes in stage 2.)
/// </summary>
public partial class ProfileDialog : Window
{
    private readonly Profile? _original;
    private readonly List<RadioButton> _iconTiles = new();
    private readonly List<RadioButton> _colorTiles = new();
    private List<WindowRow> _rows = new();

    // Close-with-profile choices, by process name ("discord" -> true).
    private readonly Dictionary<string, bool> _closeChoices = new(StringComparer.OrdinalIgnoreCase);

    // Run-as-administrator choices, same keys.
    private readonly Dictionary<string, bool> _adminChoices = new(StringComparer.OrdinalIgnoreCase);

    // Private / incognito choices for browser windows: "w:<window handle>" for open windows, "a:<index>" for saved apps.
    private readonly Dictionary<string, bool> _privateChoices = new(StringComparer.Ordinal);

    public ProfileDialog(Profile? profile)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _original = profile;

        // Open tall enough to show a good part of the window list, but never bigger than the screen.
        var area = SystemParameters.WorkArea;
        Width = Math.Min(760, area.Width - 40);
        Height = Math.Min(900, area.Height - 40);

        Title = profile == null ? "New profile" : $"{profile.Name}: name, look & options";
        NameBox.Text = profile?.Name ?? "";
        BuildIconTiles(profile?.Icon);
        BuildColorTiles(profile?.Color ?? ProfileIcons.Palette[0]);

        var others = profile?.OtherApps ?? OtherAppsAction.Minimize;
        OtherLeave.IsChecked = others == OtherAppsAction.Leave;
        OtherMinimize.IsChecked = others == OtherAppsAction.Minimize;
        OtherClose.IsChecked = others == OtherAppsAction.Close;

        AllAtOnceBox.IsChecked = profile?.LaunchAllAtOnce ?? false;
        SuggestGamesBox.IsChecked = profile?.ShowSuggestedGames ?? false;

        HotkeyText.Attach(HotkeyBox);
        HotkeyText.Attach(ResnapHotkeyBox);
        HotkeyBox.Text = HotkeyText.Normalize(profile?.Hotkey) ?? "";
        ResnapHotkeyBox.Text = HotkeyText.Normalize(profile?.ResnapHotkey) ?? "";

        PrivateBox.IsChecked = profile?.IsPrivate ?? false;
        if (profile?.IsPrivate == true)
        {
            PinHint.Text = "Leave both boxes blank to keep the current PIN, or type a new one twice to change it.";
        }

        if (profile != null)
        {
            AppsTitle.Text = $"Apps ({profile.Apps.Count} saved)";
            ReplaceAppsBox.Visibility = Visibility.Visible;
            AppsHint.Text = "Saved: " + string.Join(", ", profile.Apps.Select(a => a.Name)) +
                            ". To change them, arrange your windows, tick the box above and pick the windows below.";
            WindowsList.IsEnabled = false;
        }

        RestoreKeptBox.IsChecked = profile?.RestoreKeptApps ?? true;
        CommandsButton.Visibility = profile != null ? Visibility.Visible : Visibility.Collapsed;
        ResetBeforeCloseBox.IsChecked = profile?.ResetPositionsBeforeClosing ?? true;

        if (profile != null)
        {
            foreach (var app in profile.Apps)
            {
                _closeChoices[CloseKey(app)] = app.CloseWithProfile;
                _closeChoices[WindowKey(CloseKey(app), app.CapturedTitle)] = app.CloseWithProfile;
                _adminChoices[CloseKey(app)] = app.RunAsAdmin;
            }
        }

        LoadWindows();
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    // ---------------- Icon and color pickers ----------------

    private void BuildIconTiles(string? selected)
    {
        var tileStyle = (Style)FindResource("IconTile");
        AddIconTile(null, "Aa", ProfileIcons.TextFont, "First letter of the name", selected == null || !ProfileIcons.IsGlyph(selected), tileStyle);
        foreach (var (code, label) in ProfileIcons.Icons)
        {
            bool isSelected = string.Equals(code, selected, StringComparison.OrdinalIgnoreCase);
            AddIconTile(code, ProfileIcons.GlyphText(code, ""), ProfileIcons.IconFont, label, isSelected, tileStyle);
        }
    }

    private void AddIconTile(string? code, string text, FontFamily font, string tip, bool isChecked, Style style)
    {
        var tile = new RadioButton
        {
            Style = style,
            GroupName = "Icon",
            Tag = code,
            ToolTip = tip,
            IsChecked = isChecked,
            Content = new TextBlock { Text = text, FontFamily = font, FontSize = 18 }
        };
        _iconTiles.Add(tile);
        IconPanel.Children.Add(tile);
    }

    private void BuildColorTiles(string selected)
    {
        var tileStyle = (Style)FindResource("IconTile");
        var colors = ProfileIcons.Palette.ToList();
        if (!colors.Contains(selected, StringComparer.OrdinalIgnoreCase))
        {
            colors.Add(selected); // a custom color typed into the profile file
        }

        foreach (string hex in colors)
        {
            var tile = new RadioButton
            {
                Style = tileStyle,
                GroupName = "Color",
                Tag = hex,
                Width = 34,
                Height = 34,
                ToolTip = hex,
                IsChecked = string.Equals(hex, selected, StringComparison.OrdinalIgnoreCase),
                Content = new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(4),
                    Background = ProfileIcons.BrushFor(hex)
                }
            };
            _colorTiles.Add(tile);
            ColorPanel.Children.Add(tile);
        }
    }

    // ---------------- Window list ----------------

    private void LoadWindows()
    {
        var monitors = Monitors.GetAll();
        bool firstLoad = _rows.Count == 0;
        var stillTicked = new HashSet<IntPtr>(_rows.Where(r => r.IsChecked).Select(r => r.Window.Handle));

        var rows = new List<WindowRow>();
        foreach (var window in WindowFinder.GetAppWindows(includeNotInTaskbar: true))
        {
            // LaunchStage is never part of a profile; whether it stays open is a setting.
            if (window.ProcessName.StartsWith("LaunchStage", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var position = ProfileCapture.CapturePosition(window, monitors);
            bool tick = stillTicked.Contains(window.Handle)
                        || (firstLoad && _original != null && _original.Apps.Any(a => WindowMatcher.SameProcess(a, window)));
            rows.Add(new WindowRow(
                window,
                $"{window.AppName}  —  {window.Title}",
                $"Monitor {position.MonitorNumber} · {position.Width}x{position.Height} · {window.State}" + (window.InTaskbar ? "" : " · not in taskbar"),
                tick)
            {
                IsElevated = WindowFinder.IsProcessElevated(window.ProcessId) == true
            });
        }

        _rows = rows;
        WindowsList.ItemsSource = _rows;
        RebuildCloseList();

        if (_rows.Count == 0)
        {
            AppsHint.Text = "No app windows are open. Open the apps for this profile, arrange them, then press Refresh list.";
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadWindows();

    private void ReplaceApps_Changed(object sender, RoutedEventArgs e)
    {
        WindowsList.IsEnabled = ReplaceAppsBox.IsChecked == true;
        RebuildCloseList();
    }

    private void WindowRow_Click(object sender, RoutedEventArgs e) => RebuildCloseList();

    // ---------------- Private profile ----------------

    private void Private_Changed(object sender, RoutedEventArgs e)
    {
        PinPanel.Visibility = PrivateBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (PrivateBox.IsChecked == true && IsLoaded && _original?.IsPrivate != true)
        {
            PinBox.Focus();
        }
    }

    /// <summary>A small ? next to a section: opens the guide at that topic (its Tag).</summary>
    private void Help_Click(object sender, RoutedEventArgs e) => HelpWindow.ShowTopic((sender as FrameworkElement)?.Tag as string);

    // ---------------- Hotkeys ----------------

    private void ClearHotkey_Click(object sender, RoutedEventArgs e) => HotkeyBox.Text = "";

    private void ClearResnapHotkey_Click(object sender, RoutedEventArgs e) => ResnapHotkeyBox.Text = "";

    /// <summary>A plain-language problem with the chosen hotkeys, or null when they're fine.</summary>
    private string? HotkeyProblem(string? hotkey, string? resnapHotkey)
    {
        if (hotkey != null && hotkey == resnapHotkey)
        {
            return "The two hotkeys are the same; pick a different one for each.";
        }

        foreach (string? combo in new[] { hotkey, resnapHotkey })
        {
            if (combo != null && HotkeyText.FindOwner(combo, _original?.Name) is { } owner)
            {
                return $"{combo} is already used for {owner}. Pick another one.";
            }
        }

        return null;
    }

    private void Digits_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsAsciiDigit);

    // ---------------- Close profile choices ----------------

    private static string CloseKey(AppEntry app) => WindowMatcher.GetProcessName(app) ?? app.Name;

    /// <summary>Close choice for one window of an app that has several windows in the profile.</summary>
    private static string WindowKey(string processKey, string? title) => processKey + "\n" + (title ?? "");

    /// <summary>Does "Close profile" close this window? Window choice first, then the app's choice; ticked by default.</summary>
    private bool CloseChoice(string processKey, string? title, bool multiWindow) =>
        multiWindow && _closeChoices.TryGetValue(WindowKey(processKey, title), out bool forWindow)
            ? forWindow
            : !_closeChoices.TryGetValue(processKey, out bool forApp) || forApp;

    /// <summary>
    /// One checkbox per app in the profile: does "Close profile" close it? Apps with several windows (like an
    /// overlay app) get one checkbox per window, so only the main window needs to be closed.
    /// </summary>
    private void RebuildCloseList()
    {
        bool fromWindows = _original == null || ReplaceAppsBox.IsChecked == true;
        var apps = (fromWindows
            ? _rows.Where(r => r.IsChecked).Select(r => (Key: r.Window.ProcessName, Name: r.Window.AppName, Title: (string?)r.Window.Title, Admin: r.IsElevated))
            : _original!.Apps.Select(a => (Key: CloseKey(a), Name: a.Name, Title: a.CapturedTitle, Admin: a.RunAsAdmin))).ToList();

        var windowCounts = apps.GroupBy(a => a.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        ClosePanel.Children.Clear();
        CloseWindowsPanel.Children.Clear();
        AdminPanel.Children.Clear();
        var seenApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenWindows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool missingTitles = false;

        foreach (var (key, name, title, adminNow) in apps)
        {
            bool firstOfApp = seenApps.Add(key);
            bool multiWindow = windowCounts[key] > 1;

            if (firstOfApp)
            {
                // Run as administrator (pre-ticked for apps already running as admin). One per app.
                var adminBox = new CheckBox
                {
                    Content = name,
                    IsChecked = _adminChoices.TryGetValue(key, out bool admin) ? admin : adminNow,
                    Margin = new Thickness(0, 0, 18, 6)
                };
                string adminKey = key;
                adminBox.Click += (_, _) => _adminChoices[adminKey] = adminBox.IsChecked == true;
                AdminPanel.Children.Add(adminBox);
            }

            if (!multiWindow)
            {
                var box = new CheckBox
                {
                    Content = name,
                    IsChecked = CloseChoice(key, title, false),
                    Margin = new Thickness(0, 0, 18, 6)
                };
                string captured = key;
                box.Click += (_, _) => _closeChoices[captured] = box.IsChecked == true;
                ClosePanel.Children.Add(box);
                continue;
            }

            // An app with several windows: a heading once, then one checkbox per window.
            if (firstOfApp)
            {
                CloseWindowsPanel.Children.Add(new TextBlock
                {
                    Text = $"{name} has {windowCounts[key]} windows. If closing its main window closes the others, tick only the main one.",
                    Style = (Style)FindResource("Hint"),
                    Margin = new Thickness(0, 6, 0, 4)
                });
            }

            if (string.IsNullOrEmpty(title))
            {
                missingTitles = true;
            }

            string windowKey = WindowKey(key, title);
            if (!seenWindows.Add(windowKey))
            {
                continue; // two windows with the same title share one checkbox
            }

            var windowBox = new CheckBox
            {
                Content = string.IsNullOrEmpty(title) ? $"{name} (window title unknown)" : title,
                IsChecked = CloseChoice(key, title, true),
                Margin = new Thickness(16, 0, 18, 6)
            };
            windowBox.Click += (_, _) => _closeChoices[windowKey] = windowBox.IsChecked == true;
            CloseWindowsPanel.Children.Add(windowBox);
        }

        if (missingTitles)
        {
            CloseWindowsPanel.Children.Add(new TextBlock
            {
                Text = "This profile was made before window titles were saved, so these windows can't be told apart yet. " +
                       "Tick \"Replace this profile's apps\" above and pick the windows again.",
                Style = (Style)FindResource("Hint"),
                Foreground = (Brush)FindResource("WarnBrush"),
                Margin = new Thickness(0, 2, 0, 4)
            });
        }

        CloseEmpty.Visibility = seenApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RebuildPrivateList(fromWindows);

        AdminHint.Text = seenApps.Count == 0
            ? "Tick some windows above and they'll show up here."
            : App.Instance.Settings.AdminMode
                ? "Ticked apps start with administrator rights. Everything else starts as a normal app."
                : "Ticked apps start with administrator rights. Turn on Admin support in Settings so Windows doesn't ask each time.";
    }

    // ---------------- Private browser windows ----------------

    private static string PrivateKey(WindowRow row) => "w:" + row.Window.Handle.ToInt64();

    private static string PrivateKey(int appIndex) => "a:" + appIndex;

    /// <summary>One "Private / incognito" checkbox per browser window in the profile.</summary>
    private void RebuildPrivateList(bool fromWindows)
    {
        PrivatePanel.Children.Clear();
        var items = fromWindows
            ? _rows.Where(r => r.IsChecked && BrowserPrivacy.IsBrowser(r.Window.ProcessName))
                .Select(r => (Key: PrivateKey(r), Label: $"{r.Window.AppName}: {r.Window.Title}", Default: BrowserPrivacy.IsPrivate(r.Window)))
                .ToList()
            : _original!.Apps.Select((a, i) => (App: a, Index: i))
                .Where(x => BrowserPrivacy.IsBrowser(x.App))
                .Select(x => (Key: PrivateKey(x.Index), Label: $"{x.App.Name}: {x.App.CapturedTitle ?? "window " + (x.Index + 1)}", Default: x.App.PrivateWindow))
                .ToList();

        foreach (var (key, label, isPrivate) in items)
        {
            var box = new CheckBox
            {
                Content = label,
                IsChecked = _privateChoices.TryGetValue(key, out bool chosen) ? chosen : isPrivate,
                Margin = new Thickness(0, 0, 0, 6)
            };
            box.Click += (_, _) => _privateChoices[key] = box.IsChecked == true;
            PrivatePanel.Children.Add(box);
        }

        PrivateSection.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------- Stream Deck commands ----------------

    private void Commands_Click(object sender, RoutedEventArgs e)
    {
        // Uses the saved name: a new name only works once the profile is saved.
        string name = _original?.Name ?? NameBox.Text.Trim();
        if (_original != null && !string.Equals(NameBox.Text.Trim(), _original.Name, StringComparison.Ordinal))
        {
            ErrorText.Text = "Showing commands for the saved name. Save first to use the new name.";
        }

        new CommandsDialog(name) { Owner = this }.ShowDialog();
    }

    // ---------------- Save ----------------

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        string name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            ErrorText.Text = "Give the profile a name.";
            NameBox.Focus();
            return;
        }

        bool isNew = _original == null;
        bool renamed = _original != null &&
                       !string.Equals(ProfileStore.SafeFileName(name), ProfileStore.SafeFileName(_original.Name), StringComparison.OrdinalIgnoreCase);

        if ((isNew || renamed) && File.Exists(ProfileStore.FileFor(name)))
        {
            ErrorText.Text = $"There's already a profile called '{name}'.";
            return;
        }

        bool takeApps = isNew || ReplaceAppsBox.IsChecked == true;
        var chosen = _rows.Where(r => r.IsChecked && !WindowFinder.IsWindowGone(r.Window.Handle)).Select(r => r.Window).ToList();
        if (takeApps && chosen.Count == 0)
        {
            ErrorText.Text = "Tick at least one window for this profile.";
            return;
        }

        string? hotkey = HotkeyText.Normalize(HotkeyBox.Text);
        string? resnapHotkey = HotkeyText.Normalize(ResnapHotkeyBox.Text);
        if (HotkeyProblem(hotkey, resnapHotkey) is { } hotkeyProblem)
        {
            ErrorText.Text = hotkeyProblem;
            return;
        }

        // PIN: checked before anything changes. A private profile keeps its PIN when both boxes are left blank.
        bool makePrivate = PrivateBox.IsChecked == true;
        string pin = PinBox.Password;
        bool keepExistingPin = makePrivate && _original?.IsPrivate == true
                               && pin.Length == 0 && PinConfirmBox.Password.Length == 0;
        if (makePrivate && !keepExistingPin)
        {
            if (!ProfilePin.IsValidPin(pin))
            {
                ErrorText.Text = "The PIN must be exactly 4 digits.";
                PinBox.Focus();
                return;
            }

            if (pin != PinConfirmBox.Password)
            {
                ErrorText.Text = "The two PINs don't match.";
                PinConfirmBox.Clear();
                PinConfirmBox.Focus();
                return;
            }
        }

        var profile = _original ?? new Profile();
        string oldName = profile.Name;

        profile.Name = name;
        profile.Icon = _iconTiles.FirstOrDefault(t => t.IsChecked == true)?.Tag as string;
        profile.Color = _colorTiles.FirstOrDefault(t => t.IsChecked == true)?.Tag as string ?? ProfileIcons.Palette[0];
        profile.OtherApps = OtherClose.IsChecked == true ? OtherAppsAction.Close
            : OtherLeave.IsChecked == true ? OtherAppsAction.Leave
            : OtherAppsAction.Minimize;
        profile.LaunchAllAtOnce = AllAtOnceBox.IsChecked == true;
        profile.ShowSuggestedGames = SuggestGamesBox.IsChecked == true;
        profile.Hotkey = hotkey;
        profile.ResnapHotkey = resnapHotkey;

        if (takeApps)
        {
            // Positions are read now, so windows arranged while this dialog was open are saved correctly.
            var monitors = Monitors.GetAll();
            profile.Apps = chosen.Select(w => ProfileCapture.CreateEntry(w, monitors)).ToList();

            // Private / incognito ticks for the browser windows just picked, and the website each one shows.
            for (int i = 0; i < chosen.Count; i++)
            {
                BrowserAddress.FillIn(profile.Apps[i], chosen[i].Handle);

                if (_privateChoices.TryGetValue("w:" + chosen[i].Handle.ToInt64(), out bool isPrivate))
                {
                    profile.Apps[i].PrivateWindow = isPrivate;
                    BrowserPrivacy.MarkPrivate(chosen[i].Handle, isPrivate); // so this open window counts as private now
                }
            }
        }
        else
        {
            for (int i = 0; i < profile.Apps.Count; i++)
            {
                if (_privateChoices.TryGetValue(PrivateKey(i), out bool isPrivate))
                {
                    profile.Apps[i].PrivateWindow = isPrivate;
                }
            }
        }

        profile.RestoreKeptApps = RestoreKeptBox.IsChecked == true;
        profile.ResetPositionsBeforeClosing = ResetBeforeCloseBox.IsChecked == true;

        if (!makePrivate)
        {
            ProfilePin.Clear(profile);
        }
        else if (!keepExistingPin)
        {
            ProfilePin.Set(profile, pin);
        }

        foreach (var app in profile.Apps)
        {
            bool multiWindow = profile.Apps.Count(a => string.Equals(CloseKey(a), CloseKey(app), StringComparison.OrdinalIgnoreCase)) > 1;
            app.CloseWithProfile = CloseChoice(CloseKey(app), app.CapturedTitle, multiWindow);
            if (_adminChoices.TryGetValue(CloseKey(app), out bool admin))
            {
                app.RunAsAdmin = admin;
            }
        }

        try
        {
            ProfileStore.Save(profile);
            if (renamed)
            {
                ProfileStore.Delete(oldName);
            }
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"Couldn't save: {ex.Message}";
            return;
        }

        DialogResult = true;
    }
}
