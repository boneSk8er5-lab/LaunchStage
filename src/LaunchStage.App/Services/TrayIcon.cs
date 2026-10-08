using System.Windows;
using LaunchStage.Core.Desktop;
using LaunchStage.Core.Engine;
using LaunchStage.Core.Logging;
using LaunchStage.Core.Models;
using LaunchStage.Core.Storage;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace LaunchStageApp.Services;

/// <summary>
/// The tray icon. Double left-click opens LaunchStage; right-click shows a menu of profiles to activate.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ContextMenuStrip _menu;
    private bool _disposed;

    public TrayIcon()
    {
        _menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
            Font = new Drawing.Font("Segoe UI", 9.5f)
        };
        _menu.Opening += (_, _) => RebuildMenu();
        RebuildMenu();

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "LaunchStage",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                OpenRequested?.Invoke();
            }
        };
    }

    public event Action? OpenRequested;
    public event Action? SettingsRequested;

    public event Action? GamePickerRequested;
    public event Action? HelpRequested;
    public event Action? ExitRequested;
    public event Action<string>? ActivateRequested;
    public event Action<string>? CloseRequested;
    public event Action<string>? ResnapRequested;

    /// <summary>When it returns true, profile items are greyed out (a profile is already activating).</summary>
    public Func<bool>? IsBusy { get; set; }

    private void RebuildMenu()
    {
        // Colors follow the app's theme (checked each time the menu opens).
        bool dark = ThemeManager.IsDark;
        var colors = new MenuColors(dark);
        _menu.Renderer = new Forms.ToolStripProfessionalRenderer(colors);
        _menu.BackColor = colors.Back;
        _menu.ForeColor = dark ? Drawing.Color.FromArgb(224, 224, 224) : Drawing.Color.FromArgb(31, 31, 31);

        _menu.Items.Clear();
        _menu.Items.Add(new Forms.ToolStripLabel("Activate a profile")
        {
            ForeColor = dark ? Drawing.Color.FromArgb(136, 136, 136) : Drawing.Color.FromArgb(95, 95, 95)
        });

        bool busy = IsBusy?.Invoke() ?? false;
        List<Profile> profiles;
        try
        {
            profiles = ProfileStore.LoadAll()
                .OrderByDescending(p => p.Favorite)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error($"Tray menu couldn't list profiles: {ex.Message}");
            profiles = new List<Profile>();
        }

        if (profiles.Count == 0)
        {
            _menu.Items.Add(new Forms.ToolStripMenuItem("No profiles yet") { Enabled = false });
        }

        foreach (var profile in profiles)
        {
            string name = profile.Name;
            var item = new Forms.ToolStripMenuItem((profile.Favorite ? "★  " : "     ") + name + (profile.IsPrivate ? "  🔒" : "")) { Enabled = !busy };
            item.Click += (_, _) => Later(() => ActivateRequested?.Invoke(name));
            _menu.Items.Add(item);
        }

        if (profiles.Count > 0)
        {
            // Only profiles that are open right now can be closed or put back in place.
            var openWindows = WindowFinder.GetAppWindows(includeNotInTaskbar: true);
            var openProfiles = profiles.Where(p => WindowSnapshot.IsProfileOpen(p, openWindows)).ToList();
            _menu.Items.Add(new Forms.ToolStripSeparator());
            _menu.Items.Add(OpenProfilesMenu("Close a profile", openProfiles, busy, name => CloseRequested?.Invoke(name)));
            _menu.Items.Add(OpenProfilesMenu("Put windows back", openProfiles, busy, name => ResnapRequested?.Invoke(name)));
        }

        _menu.Items.Add(new Forms.ToolStripSeparator());
        AddAction("Open LaunchStage", () => OpenRequested?.Invoke(), bold: true);
        AddAction("Play a game...", () => GamePickerRequested?.Invoke());
        AddAction("Settings", () => SettingsRequested?.Invoke());
        AddAction("Help", () => HelpRequested?.Invoke());
        _menu.Items.Add(new Forms.ToolStripSeparator());
        AddAction("Exit", () => ExitRequested?.Invoke());
    }

    /// <summary>A submenu listing the open profiles; greyed out when none are open.</summary>
    private Forms.ToolStripMenuItem OpenProfilesMenu(string text, List<Profile> openProfiles, bool busy, Action<string> action)
    {
        var menu = new Forms.ToolStripMenuItem(text)
        {
            Enabled = !busy && openProfiles.Count > 0,
            ToolTipText = openProfiles.Count == 0 ? "No profile is open right now" : null
        };

        foreach (var profile in openProfiles)
        {
            string name = profile.Name;
            var item = new Forms.ToolStripMenuItem(name + (profile.IsPrivate ? "  🔒" : ""))
            {
                BackColor = _menu.BackColor,
                ForeColor = _menu.ForeColor
            };
            item.Click += (_, _) => Later(() => action(name));
            menu.DropDownItems.Add(item);
        }

        if (menu.DropDown is Forms.ToolStripDropDownMenu dropDown)
        {
            dropDown.ShowImageMargin = false;
            dropDown.BackColor = _menu.BackColor;
            dropDown.ForeColor = _menu.ForeColor;
            dropDown.Renderer = _menu.Renderer;
        }

        return menu;
    }

    private void AddAction(string text, Action action, bool bold = false)
    {
        var item = new Forms.ToolStripMenuItem(text);
        if (bold)
        {
            item.Font = new Drawing.Font(_menu.Font, Drawing.FontStyle.Bold);
        }

        item.Click += (_, _) => Later(action);
        _menu.Items.Add(item);
    }

    // Let the menu finish closing before opening windows or dialogs.
    private static void Later(Action action) => Application.Current.Dispatcher.InvokeAsync(action);

    private static Drawing.Icon LoadIcon()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/LaunchStage.ico"));
            if (resource != null)
            {
                using var stream = resource.Stream;
                return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Couldn't load the tray icon: {ex.Message}");
        }

        return Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }

    /// <summary>Dark or light colors for the tray menu so it matches the app.</summary>
    private sealed class MenuColors : Forms.ProfessionalColorTable
    {
        public MenuColors(bool dark)
        {
            Back = dark ? Drawing.Color.FromArgb(30, 30, 30) : Drawing.Color.FromArgb(255, 255, 255);
            Hover = dark ? Drawing.Color.FromArgb(45, 45, 45) : Drawing.Color.FromArgb(235, 235, 235);
            Edge = dark ? Drawing.Color.FromArgb(61, 61, 61) : Drawing.Color.FromArgb(204, 204, 204);
        }

        public Drawing.Color Back { get; }
        private Drawing.Color Hover { get; }
        private Drawing.Color Edge { get; }

        public override Drawing.Color ToolStripDropDownBackground => Back;
        public override Drawing.Color ImageMarginGradientBegin => Back;
        public override Drawing.Color ImageMarginGradientMiddle => Back;
        public override Drawing.Color ImageMarginGradientEnd => Back;
        public override Drawing.Color MenuBorder => Edge;
        public override Drawing.Color MenuItemBorder => Edge;
        public override Drawing.Color MenuItemSelected => Hover;
        public override Drawing.Color MenuItemSelectedGradientBegin => Hover;
        public override Drawing.Color MenuItemSelectedGradientEnd => Hover;
        public override Drawing.Color MenuItemPressedGradientBegin => Hover;
        public override Drawing.Color MenuItemPressedGradientEnd => Hover;
        public override Drawing.Color SeparatorDark => Edge;
        public override Drawing.Color SeparatorLight => Back;
    }
}
