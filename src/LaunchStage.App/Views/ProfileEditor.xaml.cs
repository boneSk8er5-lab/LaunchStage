using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LaunchStage.Core.Desktop;
using LaunchStage.Core.Engine;
using LaunchStage.Core.Logging;
using LaunchStage.Core.Models;
using LaunchStage.Core.Storage;
using LaunchStageApp.Services;
using AppWindowState = LaunchStage.Core.Models.WindowState;
using ScreenRect = LaunchStage.Core.Models.Rect;

namespace LaunchStageApp.Views;

/// <summary>
/// The Profile Editor: your monitors drawn to scale with a box for every window in the profile. Drag a box to move
/// it, drag its corner to resize it, and change everything else about an app in the panel on the right.
/// Works on a copy; nothing is saved until you press Save.
/// </summary>
public partial class ProfileEditor : Window
{
    private const int Frame = 7;          // Windows 10/11 windows have an invisible 7 px border on the left, right and bottom
    private const double GripSize = 16;   // bottom-right corner area that resizes instead of moving

    private Profile _profile;
    private string _savedName;
    private readonly List<MonitorInfo> _monitors;
    private readonly Dictionary<AppEntry, Border> _boxes = new();
    private AppEntry? _selected;
    private bool _dirty;
    private bool _syncing;
    private bool _closingConfirmed;

    // Monitor preview scale: canvas = (screen - union) * scale + offset.
    private ScreenRect _union = new();
    private double _scale = 0.1;
    private double _offsetX;
    private double _offsetY;

    // Dragging.
    private AppEntry? _dragApp;
    private Point _dragStart;
    private ScreenRect? _dragStartRect;
    private bool _resizing;

    public ProfileEditor(Profile profile)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);

        var area = SystemParameters.WorkArea;
        Width = Math.Min(1280, area.Width - 40);
        Height = Math.Min(840, area.Height - 40);

        _monitors = Monitors.GetAll();
        _profile = Clone(profile);
        _savedName = profile.Name;
        UpdateTitle();
        BuildMonitorTiles();
        RefreshList(selectFirst: true);

        // The monitor drawing picks its colors when drawn, so draw it again when the theme changes.
        ThemeManager.Changed += Redraw;
        Closed += (_, _) => ThemeManager.Changed -= Redraw;
    }

    // ---------------- Profile copy ----------------

    private static Profile Clone(Profile profile) =>
        JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(profile, ProfileStore.JsonOptions), ProfileStore.JsonOptions)!;

    private void MarkDirty()
    {
        _dirty = true;
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        TitleText.Text = _profile.Name + (_dirty ? "  •  not saved" : "");
        Title = $"Edit {_profile.Name}";
    }

    // ---------------- App list ----------------

    public sealed class AppListItem
    {
        public AppListItem(AppEntry app, int index)
        {
            App = app;
            Title = $"{index + 1}. {app.Name}";
            Detail = Describe(app);
        }

        public AppEntry App { get; }
        public string Title { get; }
        public string Detail { get; }

        private static string Describe(AppEntry app)
        {
            var parts = new List<string>();
            if (app.Behavior == AppBehavior.LaunchOnly || app.Position == null)
            {
                parts.Add("only opened");
            }
            else
            {
                parts.Add($"monitor {app.Position.MonitorNumber} · {app.Position.Width}x{app.Position.Height}");
                if (app.Position.State != AppWindowState.Normal)
                {
                    parts.Add(app.Position.State.ToString().ToLowerInvariant());
                }
            }

            if (app.Behavior == AppBehavior.PositionOnly)
            {
                parts.Add("only if open");
            }

            if (app.RunAsAdmin)
            {
                parts.Add("admin");
            }

            if (app.PrivateWindow)
            {
                parts.Add("private");
            }

            if (app.Websites is { Count: > 0 } sites)
            {
                parts.Add(sites.Count == 1 ? sites[0] : $"{sites.Count} websites");
            }

            if (!app.CloseWithProfile)
            {
                parts.Add("stays open");
            }

            if (!string.IsNullOrEmpty(app.CapturedTitle))
            {
                parts.Add($"\"{app.CapturedTitle}\"");
            }

            return string.Join(" · ", parts);
        }
    }

    private void RefreshList(bool selectFirst = false)
    {
        var keep = _selected;
        var items = _profile.Apps.Select((a, i) => new AppListItem(a, i)).ToList();
        _syncing = true;
        AppsList.ItemsSource = items;
        var target = items.FirstOrDefault(i => ReferenceEquals(i.App, keep)) ?? (selectFirst ? items.FirstOrDefault() : null);
        AppsList.SelectedItem = target;
        _syncing = false;
        Select(target?.App);
    }

    /// <summary>Updates one row's text without rebuilding the list (keeps the selection and scroll).</summary>
    private void RefreshListRow(AppEntry app)
    {
        if (AppsList.ItemsSource is not List<AppListItem> items)
        {
            return;
        }

        int index = items.FindIndex(i => ReferenceEquals(i.App, app));
        if (index < 0)
        {
            return;
        }

        items[index] = new AppListItem(app, index);
        _syncing = true;
        AppsList.Items.Refresh();
        AppsList.SelectedIndex = index;
        _syncing = false;
    }

    private void AppsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing)
        {
            Select((AppsList.SelectedItem as AppListItem)?.App);
        }
    }

    private void Select(AppEntry? app)
    {
        _selected = app;
        if (!_syncing)
        {
            _syncing = true;
            AppsList.SelectedItem = (AppsList.ItemsSource as List<AppListItem>)?.FirstOrDefault(i => ReferenceEquals(i.App, app));
            _syncing = false;
        }

        UpdateBoxStyles();
        ShowFields();
        UpButton.IsEnabled = app != null && _profile.Apps.IndexOf(app) > 0;
        DownButton.IsEnabled = app != null && _profile.Apps.IndexOf(app) < _profile.Apps.Count - 1;
        RemoveButton.IsEnabled = app != null;
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int by)
    {
        if (_selected == null)
        {
            return;
        }

        int index = _profile.Apps.IndexOf(_selected);
        int target = index + by;
        if (target < 0 || target >= _profile.Apps.Count)
        {
            return;
        }

        _profile.Apps.RemoveAt(index);
        _profile.Apps.Insert(target, _selected);
        MarkDirty();
        RefreshList();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null || !Dialogs.Confirm(this, $"Remove {_selected.Name} from this profile?"))
        {
            return;
        }

        int index = _profile.Apps.IndexOf(_selected);
        _profile.Apps.Remove(_selected);
        _selected = _profile.Apps.Count == 0 ? null : _profile.Apps[Math.Min(index, _profile.Apps.Count - 1)];
        MarkDirty();
        RefreshList();
        Redraw();
    }

    // ---------------- Adding apps ----------------

    private void AddWindow_Click(object sender, RoutedEventArgs e)
    {
        var picker = new WindowPicker { Owner = this };
        if (picker.ShowDialog() != true || picker.Chosen == null)
        {
            return;
        }

        var entry = ProfileCapture.CreateEntry(picker.Chosen, _monitors);
        BrowserAddress.FillIn(entry, picker.Chosen.Handle);
        AddEntry(entry);
        SetStatus($"Added {entry.Name} at its current spot." +
                  (entry.Websites is { Count: > 0 } ? $" It opens on {entry.Websites[0]}." : ""));
    }

    private void AddProgram_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Add a program",
            Filter = "Programs and shortcuts (*.exe;*.lnk;*.url)|*.exe;*.lnk;*.url|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string path = dialog.FileName;
        bool isExe = path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        string processName = Path.GetFileNameWithoutExtension(path);
        var entry = new AppEntry
        {
            Name = isExe ? AppNames.Get(path, processName) : processName,
            Path = path,
            ProcessName = isExe ? processName : null,
            Position = DefaultPosition()
        };

        AddEntry(entry);
        SetStatus(isExe
            ? $"Added {entry.Name}. Drag its box to where it should go."
            : $"Added {entry.Name}. It's a shortcut, so type the program's process name under Matching (advanced) to have its window moved.");
    }

    private void AddEntry(AppEntry entry)
    {
        _profile.Apps.Add(entry);
        _selected = entry;
        MarkDirty();
        RefreshList();
        Redraw();
    }

    /// <summary>A 1280x800 window centered on the main monitor.</summary>
    private WindowPosition DefaultPosition()
    {
        var monitor = _monitors.FirstOrDefault(m => m.IsPrimary) ?? _monitors[0];
        int width = Math.Min(1280, monitor.WorkArea.Width);
        int height = Math.Min(800, monitor.WorkArea.Height);
        return new WindowPosition
        {
            Monitor = monitor.DeviceName,
            MonitorNumber = monitor.Number,
            MonitorBounds = monitor.Bounds.Copy(),
            X = monitor.WorkArea.X - monitor.Bounds.X + (monitor.WorkArea.Width - width) / 2,
            Y = monitor.WorkArea.Y - monitor.Bounds.Y + (monitor.WorkArea.Height - height) / 2,
            Width = width,
            Height = height,
            State = AppWindowState.Normal
        };
    }

    // ---------------- Monitor preview ----------------

    private void Board_SizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Board_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Board.Focus();
        if (e.OriginalSource == Board)
        {
            Select(null); // clicked empty space
        }
    }

    private static bool HasSpot(AppEntry app) => app.Position != null && app.Behavior != AppBehavior.LaunchOnly;

    /// <summary>Where the app's window goes, in screen pixels on today's monitors.</summary>
    private ScreenRect ScreenRectFor(AppEntry app)
    {
        var position = app.Position!;
        var monitor = Monitors.Resolve(position, _monitors, out string? note);
        return position.State == AppWindowState.Maximized
            ? monitor.WorkArea.Copy()
            : Monitors.ToScreenRect(position, monitor, exact: note == null);
    }

    private void Redraw()
    {
        Board.Children.Clear();
        _boxes.Clear();
        if (_monitors.Count == 0 || Board.ActualWidth < 20 || Board.ActualHeight < 20)
        {
            return;
        }

        int left = _monitors.Min(m => m.Bounds.X);
        int top = _monitors.Min(m => m.Bounds.Y);
        int right = _monitors.Max(m => m.Bounds.Right);
        int bottom = _monitors.Max(m => m.Bounds.Bottom);
        _union = new ScreenRect(left, top, right - left, bottom - top);

        const double margin = 16;
        _scale = Math.Min((Board.ActualWidth - margin * 2) / _union.Width, (Board.ActualHeight - margin * 2) / _union.Height);
        _offsetX = (Board.ActualWidth - _union.Width * _scale) / 2;
        _offsetY = (Board.ActualHeight - _union.Height * _scale) / 2;

        foreach (var monitor in _monitors)
        {
            var screen = new Border
            {
                Background = (Brush)FindResource("PanelBrush"),
                BorderBrush = (Brush)FindResource("ButtonBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                IsHitTestVisible = false,
                Child = new StackPanel
                {
                    Margin = new Thickness(8, 6, 8, 6),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = monitor.Number.ToString(),
                            FontSize = 28,
                            FontWeight = FontWeights.Bold,
                            Foreground = (Brush)FindResource("ButtonBorderBrush")
                        },
                        new TextBlock
                        {
                            Text = $"{monitor.Bounds.Width}x{monitor.Bounds.Height}{(monitor.IsPrimary ? " · main" : "")}",
                            Foreground = (Brush)FindResource("TextSecondaryBrush"),
                            FontSize = 11
                        }
                    }
                }
            };
            Place(screen, monitor.Bounds);
            Board.Children.Add(screen);
        }

        // Selected box last so it's drawn on top.
        foreach (var app in _profile.Apps.Where(HasSpot).OrderBy(a => ReferenceEquals(a, _selected)))
        {
            AddBox(app);
        }

        UpdateBoxStyles();
    }

    private void AddBox(AppEntry app)
    {
        var label = new TextBlock
        {
            Text = app.Name,
            Margin = new Thickness(6, 4, 6, 4),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)FindResource("TextBrush"),
            IsHitTestVisible = false
        };
        var grip = new TextBlock
        {
            Text = "◢",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 2, 0),
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            IsHitTestVisible = false
        };

        var box = new Border
        {
            Tag = app,
            CornerRadius = new CornerRadius(3),
            Cursor = Cursors.SizeAll,
            ToolTip = app.Name,
            Child = new Grid { Children = { label, grip } }
        };
        box.MouseLeftButtonDown += Box_MouseLeftButtonDown;
        box.MouseMove += Box_MouseMove;
        box.MouseLeftButtonUp += Box_MouseLeftButtonUp;
        box.LostMouseCapture += (_, _) => _dragApp = null;

        Place(box, ScreenRectFor(app));
        _boxes[app] = box;
        Board.Children.Add(box);
    }

    private void UpdateBoxStyles()
    {
        var accent = (SolidColorBrush)FindResource("AccentBrush");
        foreach (var (app, box) in _boxes)
        {
            bool selected = ReferenceEquals(app, _selected);
            var color = selected ? accent.Color : Color.FromRgb(0x9E, 0x9E, 0x9E);
            box.Background = new SolidColorBrush(Color.FromArgb(selected ? (byte)0x66 : (byte)0x40, color.R, color.G, color.B));
            box.BorderBrush = new SolidColorBrush(color);
            box.BorderThickness = new Thickness(selected ? 2 : 1);
            box.Opacity = app.Position?.State == AppWindowState.Minimized ? 0.45 : 1.0;
            Panel.SetZIndex(box, selected ? 2 : 1);
        }
    }

    private void Place(FrameworkElement element, ScreenRect rect)
    {
        Canvas.SetLeft(element, (rect.X - _union.X) * _scale + _offsetX);
        Canvas.SetTop(element, (rect.Y - _union.Y) * _scale + _offsetY);
        element.Width = Math.Max(8, rect.Width * _scale);
        element.Height = Math.Max(8, rect.Height * _scale);
    }

    // ---------------- Dragging ----------------

    private void Box_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border box || box.Tag is not AppEntry app)
        {
            return;
        }

        Select(app);
        Board.Focus(); // so the arrow keys nudge this box
        var local = e.GetPosition(box);
        _resizing = local.X >= box.ActualWidth - GripSize && local.Y >= box.ActualHeight - GripSize;

        // Dragging a maximized window turns it into a normal window of the same size.
        if (app.Position!.State == AppWindowState.Maximized)
        {
            var rect = ScreenRectFor(app);
            app.Position.State = AppWindowState.Normal;
            SetSpot(app, rect);
        }

        _dragApp = app;
        _dragStart = e.GetPosition(Board);
        _dragStartRect = ScreenRectFor(app);
        box.CaptureMouse();
        e.Handled = true;
    }

    private void Box_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Border box)
        {
            return;
        }

        if (_dragApp == null || _dragStartRect == null)
        {
            var local = e.GetPosition(box);
            box.Cursor = local.X >= box.ActualWidth - GripSize && local.Y >= box.ActualHeight - GripSize
                ? Cursors.SizeNWSE
                : Cursors.SizeAll;
            return;
        }

        var now = e.GetPosition(Board);
        int dx = (int)Math.Round((now.X - _dragStart.X) / _scale);
        int dy = (int)Math.Round((now.Y - _dragStart.Y) / _scale);
        var rect = _dragStartRect.Copy();
        if (_resizing)
        {
            rect.Width = Math.Max(200, rect.Width + dx);
            rect.Height = Math.Max(120, rect.Height + dy);
        }
        else
        {
            rect.X += dx;
            rect.Y += dy;
        }

        if (SnapBox.IsChecked == true && (Keyboard.Modifiers & ModifierKeys.Alt) == 0)
        {
            Snap(rect, _resizing);
        }

        SetSpot(_dragApp, rect);
        Place(box, rect);
        ShowFields();
    }

    private void Box_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragApp != null && sender is Border box)
        {
            var app = _dragApp;
            _dragApp = null;
            box.ReleaseMouseCapture();
            RefreshListRow(app);
        }
    }

    /// <summary>Pulls the box's edges onto nearby screen edges and screen halves (allowing for the invisible border).</summary>
    private void Snap(ScreenRect rect, bool resizing)
    {
        double reach = 12 / _scale; // 12 pixels on the preview
        var vertical = new List<int>();
        var horizontal = new List<int>();
        foreach (var m in _monitors)
        {
            var wa = m.WorkArea;
            vertical.AddRange(new[] { wa.X, wa.X + wa.Width / 2, wa.Right });
            horizontal.AddRange(new[] { wa.Y, wa.Y + wa.Height / 2, wa.Bottom });
        }

        if (resizing)
        {
            foreach (int v in vertical.Where(v => Math.Abs(rect.Right - (v + Frame)) < reach).Take(1))
            {
                rect.Width = v + Frame - rect.X;
            }

            foreach (int h in horizontal.Where(h => Math.Abs(rect.Bottom - (h + Frame)) < reach).Take(1))
            {
                rect.Height = h + Frame - rect.Y;
            }

            return;
        }

        foreach (int v in vertical)
        {
            if (Math.Abs(rect.X - (v - Frame)) < reach)
            {
                rect.X = v - Frame;
                break;
            }

            if (Math.Abs(rect.Right - (v + Frame)) < reach)
            {
                rect.X = v + Frame - rect.Width;
                break;
            }
        }

        foreach (int h in horizontal)
        {
            if (Math.Abs(rect.Y - h) < reach)
            {
                rect.Y = h;
                break;
            }

            if (Math.Abs(rect.Bottom - (h + Frame)) < reach)
            {
                rect.Y = h + Frame - rect.Height;
                break;
            }
        }
    }

    /// <summary>Saves a screen rectangle as the app's spot, relative to the monitor it mostly sits on.</summary>
    private void SetSpot(AppEntry app, ScreenRect rect)
    {
        var monitor = Monitors.FromRect(_monitors, rect);
        var position = app.Position ??= new WindowPosition();
        position.Monitor = monitor.DeviceName;
        position.MonitorNumber = monitor.Number;
        position.MonitorBounds = monitor.Bounds.Copy();
        position.X = rect.X - monitor.Bounds.X;
        position.Y = rect.Y - monitor.Bounds.Y;
        position.Width = rect.Width;
        position.Height = rect.Height;
        MarkDirty();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Arrow keys nudge the selected box after you've clicked on the screens (not while typing or in the list).
        if (_selected == null || !HasSpot(_selected) || !Board.IsKeyboardFocused)
        {
            return;
        }

        int step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
        int dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
        int dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
        if (dx == 0 && dy == 0)
        {
            return;
        }

        var rect = ScreenRectFor(_selected);
        if (_selected.Position!.State == AppWindowState.Maximized)
        {
            _selected.Position.State = AppWindowState.Normal;
        }

        rect.X += dx;
        rect.Y += dy;
        SetSpot(_selected, rect);
        if (_boxes.TryGetValue(_selected, out var box))
        {
            Place(box, rect);
        }

        ShowFields();
        RefreshListRow(_selected);
        e.Handled = true;
    }

    // ---------------- Settings panel ----------------

    private void BuildMonitorTiles()
    {
        var style = (Style)FindResource("IconTile");
        foreach (var monitor in _monitors)
        {
            var tile = new RadioButton
            {
                Style = style,
                GroupName = "Monitor",
                Tag = monitor,
                Width = 74,
                Height = 44,
                ToolTip = $"{monitor.DeviceName} · {monitor.Bounds.Width}x{monitor.Bounds.Height}",
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = monitor.Number.ToString(), FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock
                        {
                            Text = $"{monitor.Bounds.Width}x{monitor.Bounds.Height}",
                            Style = (Style)FindResource("Hint"), // grey that follows the theme
                            FontSize = 10,
                            HorizontalAlignment = HorizontalAlignment.Center
                        }
                    }
                }
            };
            tile.Checked += Monitor_Checked;
            MonitorPanel.Children.Add(tile);
        }
    }

    private void ShowFields()
    {
        var app = _selected;
        NoSelectionText.Visibility = app == null ? Visibility.Visible : Visibility.Collapsed;
        SettingsScroll.Visibility = app == null ? Visibility.Collapsed : Visibility.Visible;
        if (app == null)
        {
            return;
        }

        _syncing = true;
        try
        {
            SetText(AppNameBox, app.Name);
            SetText(PathBox, app.Path);
            SetText(ArgumentsBox, app.Arguments ?? "");
            WebsitesPanel.Visibility = BrowserPrivacy.IsBrowser(app) ? Visibility.Visible : Visibility.Collapsed;
            SetText(WebsitesBox, string.Join(Environment.NewLine, app.Websites ?? new List<string>()));
            BehaviorBoth.IsChecked = app.Behavior == AppBehavior.LaunchAndPosition;
            BehaviorPosition.IsChecked = app.Behavior == AppBehavior.PositionOnly;
            BehaviorLaunch.IsChecked = app.Behavior == AppBehavior.LaunchOnly;

            PositionPanel.Visibility = app.Behavior == AppBehavior.LaunchOnly ? Visibility.Collapsed : Visibility.Visible;
            var position = app.Position;
            if (position != null)
            {
                var monitor = Monitors.Resolve(position, _monitors, out _);
                foreach (RadioButton tile in MonitorPanel.Children)
                {
                    tile.IsChecked = ReferenceEquals(tile.Tag, monitor);
                }

                StateNormal.IsChecked = position.State == AppWindowState.Normal;
                StateMax.IsChecked = position.State == AppWindowState.Maximized;
                StateMin.IsChecked = position.State == AppWindowState.Minimized;
                SetText(XBox, position.X.ToString());
                SetText(YBox, position.Y.ToString());
                SetText(WBox, position.Width.ToString());
                SetText(HBox, position.Height.ToString());
            }
            else
            {
                foreach (RadioButton tile in MonitorPanel.Children)
                {
                    tile.IsChecked = false;
                }

                StateNormal.IsChecked = StateMax.IsChecked = StateMin.IsChecked = false;
                SetText(XBox, "");
                SetText(YBox, "");
                SetText(WBox, "");
                SetText(HBox, "");
            }

            AdminBox.IsChecked = app.RunAsAdmin;
            CloseBox.IsChecked = app.CloseWithProfile;
            PrivateBox.Visibility = BrowserPrivacy.IsBrowser(app) ? Visibility.Visible : Visibility.Collapsed;
            PrivateBox.IsChecked = app.PrivateWindow;
            SetText(DelayBox, app.LaunchDelaySeconds.ToString("0.#"));
            SetText(TimeoutBox, app.LaunchTimeoutSeconds.ToString());
            SetText(ProcessBox, app.ProcessName ?? "");
            SetText(TitleContainsBox, app.TitleContains ?? "");
            CapturedTitleText.Text = string.IsNullOrEmpty(app.CapturedTitle)
                ? ""
                : $"Saved window title: \"{app.CapturedTitle}\" (used to tell this app's windows apart).";
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>Sets a text box without moving the caret when the text didn't change (so typing isn't disturbed).</summary>
    private static void SetText(TextBox box, string text)
    {
        if (box.Text != text)
        {
            box.Text = text;
        }
    }

    private void AppName_Changed(object sender, TextChangedEventArgs e)
    {
        if (_syncing || _selected == null)
        {
            return;
        }

        _selected.Name = AppNameBox.Text;
        MarkDirty();
        RefreshListRow(_selected);
        if (_boxes.TryGetValue(_selected, out var box) && box.Child is Grid grid && grid.Children[0] is TextBlock label)
        {
            label.Text = _selected.Name;
            box.ToolTip = _selected.Name;
        }
    }

    private void Path_Changed(object sender, TextChangedEventArgs e)
    {
        if (_syncing || _selected == null)
        {
            return;
        }

        _selected.Path = PathBox.Text.Trim();
        MarkDirty();
    }

    private void Arguments_Changed(object sender, TextChangedEventArgs e)
    {
        if (_syncing || _selected == null)
        {
            return;
        }

        _selected.Arguments = string.IsNullOrWhiteSpace(ArgumentsBox.Text) ? null : ArgumentsBox.Text.Trim();
        MarkDirty();
    }

    private void Websites_Changed(object sender, TextChangedEventArgs e)
    {
        if (_syncing || _selected == null)
        {
            return;
        }

        var sites = WebsitesBox.Text
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
        _selected.Websites = sites.Count == 0 ? null : sites;
        MarkDirty();
        RefreshListRow(_selected);
    }

    private void ReadWebsite_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
        {
            return;
        }

        var window = FindOpenWindow(_selected);
        if (window == null)
        {
            SetStatus($"{_selected.Name} isn't open. Open it on the website you want, then try again.");
            return;
        }

        Mouse.OverrideCursor = Cursors.Wait;
        string? site;
        try
        {
            site = BrowserAddress.TryRead(window.Handle);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        if (site == null)
        {
            SetStatus($"Couldn't read the website from {_selected.Name}. Type it in the Websites box instead.");
            return;
        }

        WebsitesBox.Text = site; // saves it through Websites_Changed
        SetStatus($"{_selected.Name} will open on {site}.");
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Program for {_selected.Name}",
            Filter = "Programs and shortcuts (*.exe;*.lnk;*.url)|*.exe;*.lnk;*.url|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        PathBox.Text = dialog.FileName; // Path_Changed saves it
        if (dialog.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(_selected.ProcessName))
        {
            ProcessBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
        }
    }

    private void Behavior_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncing || _selected == null)
        {
            return;
        }

        _selected.Behavior = BehaviorPosition.IsChecked == true ? AppBehavior.PositionOnly
            : BehaviorLaunch.IsChecked == true ? AppBehavior.LaunchOnly
            : AppBehavior.LaunchAndPosition;

        if (_selected.Behavior != AppBehavior.LaunchOnly && _selected.Position == null)
        {
            _selected.Position = DefaultPosition();
        }

        MarkDirty();
        RefreshListRow(_selected);
        Redraw();
        ShowFields();
    }

    private void Monitor_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncing || _selected == null || sender is not RadioButton { Tag: MonitorInfo monitor })
        {
            return;
        }

        // Keep the same spot on the new monitor, pulled in if it would fall off a smaller screen.
        var position = _selected.Position ??= DefaultPosition();
        int width = Math.Min(position.Width, monitor.Bounds.Width + 2 * Frame);
        int height = Math.Min(position.Height, monitor.Bounds.Height + Frame);
        int x = Math.Clamp(position.X, -Frame, Math.Max(-Frame, monitor.Bounds.Width - width + Frame));
        int y = Math.Clamp(position.Y, 0, Math.Max(0, monitor.Bounds.Height - height + Frame));
        SetSpot(_selected, new ScreenRect(monitor.Bounds.X + x, monitor.Bounds.Y + y, width, height));
        RefreshListRow(_selected);
        Redraw();
        ShowFields();
    }

    private void State_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncing || _selected?.Position == null)
        {
            return;
        }

        _selected.Position.State = StateMax.IsChecked == true ? AppWindowState.Maximized
            : StateMin.IsChecked == true ? AppWindowState.Minimized
            : AppWindowState.Normal;
        MarkDirty();
        RefreshListRow(_selected);
        Redraw();
    }

    private void Number_Changed(object sender, TextChangedEventArgs e)
    {
        if (_syncing || _selected?.Position == null || sender is not TextBox box)
        {
            return;
        }

        if (!int.TryParse(box.Text.Trim(), out int value))
        {
            box.BorderBrush = (Brush)FindResource("BadBrush");
            return;
        }

        box.ClearValue(BorderBrushProperty);
        var position = _selected.Position;
        if (box == XBox)
        {
            position.X = value;
        }
        else if (box == YBox)
        {
            position.Y = value;
        }
        else if (box == WBox)
        {
            position.Width = Math.Max(50, value);
        }
        else if (box == HBox)
        {
            position.Height = Math.Max(50, value);
        }

        MarkDirty();
        if (_boxes.TryGetValue(_selected, out var shape))
        {
            Place(shape, ScreenRectFor(_selected));
        }

        RefreshListRow(_selected);
    }

    private void Options_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
        {
            return;
        }

        _selected.RunAsAdmin = AdminBox.IsChecked == true;
        _selected.CloseWithProfile = CloseBox.IsChecked == true;
        _selected.PrivateWindow = PrivateBox.IsChecked == true;
        MarkDirty();
        RefreshListRow(_selected);
    }

    private void Timing_Changed(object sender, TextChangedEventArgs e)
    {
        if (_syncing || _selected == null)
        {
            return;
        }

        if (double.TryParse(DelayBox.Text.Trim(), out double delay) && delay >= 0)
        {
            _selected.LaunchDelaySeconds = delay;
            DelayBox.ClearValue(BorderBrushProperty);
        }
        else
        {
            DelayBox.BorderBrush = (Brush)FindResource("BadBrush");
        }

        if (int.TryParse(TimeoutBox.Text.Trim(), out int timeout) && timeout > 0)
        {
            _selected.LaunchTimeoutSeconds = timeout;
            TimeoutBox.ClearValue(BorderBrushProperty);
        }
        else
        {
            TimeoutBox.BorderBrush = (Brush)FindResource("BadBrush");
        }

        MarkDirty();
    }

    private void Matching_Changed(object sender, TextChangedEventArgs e)
    {
        if (_syncing || _selected == null)
        {
            return;
        }

        _selected.ProcessName = string.IsNullOrWhiteSpace(ProcessBox.Text) ? null : WindowMatcher.StripExe(ProcessBox.Text.Trim());
        _selected.TitleContains = string.IsNullOrWhiteSpace(TitleContainsBox.Text) ? null : TitleContainsBox.Text.Trim();
        PrivateBox.Visibility = BrowserPrivacy.IsBrowser(_selected) ? Visibility.Visible : Visibility.Collapsed;
        WebsitesPanel.Visibility = PrivateBox.Visibility;
        MarkDirty();
    }

    // ---------------- Trying spots on real windows ----------------

    private WindowInfo? FindOpenWindow(AppEntry app)
    {
        var windows = WindowFinder.GetAppWindows(includeNotInTaskbar: true);
        return WindowMatcher.AssignWindows(_profile, windows).GetValueOrDefault(app);
    }

    private void UseCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
        {
            return;
        }

        var window = FindOpenWindow(_selected);
        if (window == null)
        {
            SetStatus($"{_selected.Name} isn't open, so there's no spot to copy.");
            return;
        }

        _selected.Position = ProfileCapture.CapturePosition(window, _monitors);
        MarkDirty();
        RefreshListRow(_selected);
        Redraw();
        ShowFields();
        SetStatus($"Copied where {_selected.Name} is now.");
    }

    private void MoveNow_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null || !HasSpot(_selected))
        {
            return;
        }

        var window = FindOpenWindow(_selected);
        if (window == null)
        {
            SetStatus($"{_selected.Name} isn't open. Open it first to try the spot.");
            return;
        }

        SetStatus(WindowMover.Apply(window.Handle, ScreenRectFor(_selected), _selected.Position!.State, out string? error)
            ? $"Moved {_selected.Name}."
            : $"Couldn't move {_selected.Name}: {error}.");
    }

    private void ArrangeNow_Click(object sender, RoutedEventArgs e)
    {
        var windows = WindowFinder.GetAppWindows(includeNotInTaskbar: true);
        int moved = 0;
        var problems = new List<string>();
        foreach (var (app, window) in WindowMatcher.AssignWindows(_profile, windows))
        {
            if (!HasSpot(app))
            {
                continue;
            }

            if (WindowMover.Apply(window.Handle, ScreenRectFor(app), app.Position!.State, out string? error))
            {
                moved++;
            }
            else
            {
                problems.Add($"{app.Name}: {error}");
            }
        }

        SetStatus(moved == 0 && problems.Count == 0
            ? "None of this profile's windows are open."
            : $"Moved {moved} window(s)." + (problems.Count > 0 ? " Couldn't move " + string.Join("; ", problems) + "." : ""));
        Activate();
    }

    // ---------------- Other windows ----------------

    /// <summary>The Help button and the small ? links: open the guide at a topic (the Tag).</summary>
    private void Help_Click(object sender, RoutedEventArgs e) => HelpWindow.ShowTopic((sender as FrameworkElement)?.Tag as string);

    private void Commands_Click(object sender, RoutedEventArgs e) =>
        new CommandsDialog(_savedName) { Owner = this }.ShowDialog();

    /// <summary>Opens the name / look / options window, then picks up whatever it changed.</summary>
    private void ProfileSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_dirty)
        {
            if (!Dialogs.Confirm(this, "Save your changes here first? (The options window works on the saved profile.)"))
            {
                return;
            }

            if (!TrySave())
            {
                return;
            }
        }

        Profile? saved;
        try
        {
            saved = ProfileStore.Find(_savedName);
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't read the profile: {ex.Message}");
            return;
        }

        if (saved == null)
        {
            SetStatus("The profile file is missing; press Save first.");
            return;
        }

        new ProfileDialog(saved) { Owner = this }.ShowDialog();

        // ProfileDialog may have renamed it or re-picked the windows; reload from disk.
        var reloaded = ProfileStore.Find(saved.Name) ?? ProfileStore.Find(_savedName);
        if (reloaded != null)
        {
            _profile = reloaded;
            _savedName = reloaded.Name;
            _selected = null;
            _dirty = false;
            UpdateTitle();
            RefreshList(selectFirst: true);
            Redraw();
        }
    }

    // ---------------- Save and close ----------------

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (TrySave())
        {
            _closingConfirmed = true;
            DialogResult = true;
        }
    }

    private bool TrySave()
    {
        var unnamed = _profile.Apps.FirstOrDefault(a => string.IsNullOrWhiteSpace(a.Name));
        if (unnamed != null)
        {
            Select(unnamed);
            SetStatus("Every app needs a name.");
            return false;
        }

        var noPath = _profile.Apps.FirstOrDefault(a => a.Behavior != AppBehavior.PositionOnly && string.IsNullOrWhiteSpace(a.Path));
        if (noPath != null)
        {
            Select(noPath);
            SetStatus($"{noPath.Name} needs a program to open (or choose \"Only put it in place if it's already open\").");
            return false;
        }

        try
        {
            ProfileStore.Save(_profile);
            _dirty = false;
            UpdateTitle();
            Log.Info($"Profile '{_profile.Name}' saved from the editor ({_profile.Apps.Count} apps).");
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't save: {ex.Message}");
            return false;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closingConfirmed || !_dirty)
        {
            return;
        }

        if (!Dialogs.Confirm(this, "Close without saving your changes?"))
        {
            e.Cancel = true;
        }
    }

    private void SetStatus(string text) => StatusText.Text = text;
}
