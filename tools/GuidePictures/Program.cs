using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LaunchStage.Core.Desktop;
using LaunchStage.Core.Engine;
using LaunchStage.Core.Games;
using LaunchStage.Core.Models;
using LaunchStage.Core.Storage;
using LaunchStageApp.Views;

// Takes the guide's pictures. Without arguments: makes the demo data, then runs itself once per picture (one
// LaunchStage "app" per run keeps WPF happy). With a picture name: draws just that picture.
namespace GuidePictures;

internal static class Program
{
    private static readonly string[] Pictures =
    {
        "launcher", "launcher-list", "new-profile", "profile-options", "editor", "status-popup",
        "settings", "commands", "game-picker", "pin"
    };

    private const double Scale = 1.5; // drawn at 150% so the text is sharp

    [STAThread]
    private static int Main(string[] args)
    {
        string demoData = Path.Combine(Path.GetTempPath(), "LaunchStage guide demo");
        Environment.SetEnvironmentVariable("LAUNCHSTAGE_DATA", demoData); // must happen before LaunchStage reads its folder
        string output = Path.Combine(FindRepo(), "guide", "images");

        if (args.Length == 0)
        {
            MakeDemoData(demoData);
            Directory.CreateDirectory(output);
            foreach (string picture in Pictures)
            {
                var run = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, picture) { UseShellExecute = false })!;
                run.WaitForExit();
                Console.WriteLine(run.ExitCode == 0 ? $"  {picture}.png" : $"  {picture}.png FAILED");
            }

            Directory.Delete(demoData, recursive: true);
            Console.WriteLine($"Pictures saved in {output}");
            return 0;
        }

        try
        {
            Draw(args[0], Path.Combine(output, args[0] + ".png"));
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return 1;
        }
    }

    private static string FindRepo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "guide")) && File.Exists(Path.Combine(dir.FullName, "build.cmd")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Couldn't find the LaunchStage folder (with guide and build.cmd).");
    }

    // ---------------- Demo data ----------------

    private static void MakeDemoData(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        AppPaths.EnsureFolders();
        SettingsStore.Save(new AppSettings
        {
            WelcomeShown = true,
            AdminMode = false,
            GamePickerHotkey = "Ctrl+Alt+G",
            NeverClose = new List<string> { "Spotify", "KeePass" }
        });

        var monitors = Monitors.GetAll();
        var main = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        var side = monitors.FirstOrDefault(m => !m.IsPrimary) ?? main;

        ProfileStore.Save(new Profile
        {
            Name = "Stream",
            Icon = "E714",
            Color = "#00ADB5",
            Favorite = true,
            OtherApps = OtherAppsAction.Minimize,
            Hotkey = "Ctrl+Alt+1",
            ResnapHotkey = "Ctrl+Alt+Shift+1",
            ShowSuggestedGames = true,
            Apps = new List<AppEntry>
            {
                App("OBS Studio", "obs64", @"C:\Program Files\obs-studio\bin\64bit\obs64.exe", main, 0, 0, 0.62, 0.7),
                App("Discord", "Discord", @"C:\Program Files\Discord\Discord.exe", main, 0.62, 0, 0.38, 0.7),
                Browser("Brave Browser", "Stream manager - Kick - Brave", "https://dashboard.kick.com", main, 0, 0.7, 0.5, 0.3),
                Browser("Brave Browser", "Twitch - Brave", "https://www.twitch.tv", main, 0.5, 0.7, 0.5, 0.3),
                App("Spotify", "Spotify", @"C:\Program Files\Spotify\Spotify.exe", side, 0, 0, 1, 0.5, close: false)
            }
        });
        ProfileStore.Save(new Profile
        {
            Name = "Coding",
            Icon = "E943",
            Color = "#1E88E5",
            OtherApps = OtherAppsAction.Minimize,
            Apps = new List<AppEntry>
            {
                App("Visual Studio Code", "Code", @"C:\Program Files\Microsoft VS Code\Code.exe", main, 0, 0, 0.66, 1),
                App("Windows Terminal", "WindowsTerminal", @"C:\Program Files\WindowsApps\WindowsTerminal.exe", main, 0.66, 0, 0.34, 1)
            }
        });
        ProfileStore.Save(new Profile
        {
            Name = "Gaming",
            Icon = "E7FC",
            Color = "#7E57C2",
            OtherApps = OtherAppsAction.Close,
            ShowSuggestedGames = true,
            Apps = new List<AppEntry> { App("Discord", "Discord", @"C:\Program Files\Discord\Discord.exe", side, 0, 0, 1, 1) }
        });
        var late = new Profile
        {
            Name = "Late night",
            Icon = "E8D6",
            Color = "#EC407A",
            OtherApps = OtherAppsAction.Leave,
            Apps = new List<AppEntry> { App("Spotify", "Spotify", @"C:\Program Files\Spotify\Spotify.exe", main, 0.25, 0.15, 0.5, 0.7) }
        };
        ProfilePin.Set(late, "1234");
        ProfileStore.Save(late);
    }

    private static AppEntry App(string name, string process, string path, MonitorInfo monitor,
        double x, double y, double width, double height, bool close = true) => new()
    {
        Name = name,
        ProcessName = process,
        Path = path,
        CapturedTitle = name,
        CloseWithProfile = close,
        Position = new WindowPosition
        {
            Monitor = monitor.DeviceName,
            MonitorNumber = monitor.Number,
            MonitorBounds = monitor.Bounds.Copy(),
            X = (int)(monitor.WorkArea.Width * x) + monitor.WorkArea.X - monitor.Bounds.X,
            Y = (int)(monitor.WorkArea.Height * y) + monitor.WorkArea.Y - monitor.Bounds.Y,
            Width = (int)(monitor.WorkArea.Width * width),
            Height = (int)(monitor.WorkArea.Height * height)
        }
    };

    private static AppEntry Browser(string name, string title, string site, MonitorInfo monitor,
        double x, double y, double width, double height)
    {
        var entry = App(name, "brave", @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe", monitor, x, y, width, height);
        entry.CapturedTitle = title;
        entry.Websites = new List<string> { site };
        return entry;
    }

    /// <summary>Open windows as the New profile window would list them (made up, so nothing real shows).</summary>
    private static List<WindowRow> DemoRows()
    {
        WindowRow Row(string app, string title, bool ticked)
        {
            var window = new WindowInfo { Handle = new IntPtr(Random.Shared.Next(1000, 999999)), Title = title, ProcessName = app };
            return new WindowRow(window, $"{app}  —  {title}", "Monitor 1 · 1920x1080 · Normal", ticked);
        }

        return new List<WindowRow>
        {
            Row("OBS Studio", "OBS 31.0 - Profile: Stream - Scenes: Main", true),
            Row("Discord", "#general | My Server - Discord", true),
            Row("Brave Browser", "Stream manager - Kick - Brave", true),
            Row("Brave Browser", "Twitch - Brave", true),
            Row("Spotify", "Spotify Premium", false),
            Row("File Explorer", "Downloads - File Explorer", false)
        };
    }

    // ---------------- Drawing ----------------

    private static void Draw(string picture, string file)
    {
        typeof(Application).GetField("_resourceAssembly", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, typeof(LaunchStageApp.App).Assembly);
        var app = new LaunchStageApp.App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        typeof(LaunchStageApp.App).GetProperty("Settings")!.SetValue(app, SettingsStore.Load());
        Call(typeof(LaunchStageApp.App).Assembly.GetType("LaunchStageApp.Services.ThemeManager")!, "Apply", null, "Dark", false);

        Window window;
        double width = 900, height = 600;
        Action? prepare = null;
        Profile? stream = ProfileStore.Find("Stream");

        switch (picture)
        {
            case "launcher":
            case "launcher-list":
                app.Settings.LauncherLayout = picture == "launcher" ? "Cards" : "List";
                window = new MainWindow();
                width = 860;
                height = picture == "launcher" ? 470 : 500;
                break;

            case "new-profile":
            case "profile-options":
            {
                var dialog = new ProfileDialog(picture == "new-profile" ? null : stream);
                prepare = () =>
                {
                    var rows = DemoRows();
                    if (picture == "profile-options")
                    {
                        rows.ForEach(r => r.IsChecked = false); // the saved profile's apps are listed below instead
                    }

                    Set(dialog, "_rows", rows);
                    ((ItemsControl)dialog.FindName("WindowsList")).ItemsSource = rows;
                    Call(typeof(ProfileDialog), "RebuildCloseList", dialog);
                    if (picture == "new-profile")
                    {
                        ((TextBox)dialog.FindName("NameBox")).Text = "Stream";
                    }
                    else
                    {
                        FindScroller(dialog)?.ScrollToBottom();
                    }
                };
                window = dialog;
                width = 760;
                height = 900;
                break;
            }

            case "editor":
                window = new ProfileEditor(stream!);
                width = 1240;
                height = 760;
                break;

            case "status-popup":
            {
                var popup = new StatusPopup("Stream");
                popup.SizeChanged += (_, _) => { popup.Left = -6000; popup.Top = -6000; }; // it moves itself to the corner
                prepare = () => popup.ShowResult(new ActivationResult(), "Stream");
                window = popup;
                width = double.NaN;
                break;
            }

            case "settings":
                window = new SettingsWindow();
                height = 1080;
                width = 560;
                break;

            case "commands":
            {
                var commands = new CommandsDialog("Stream");
                prepare = () =>
                {
                    const string exe = @"C:\Program Files\LaunchStage\LaunchStage.exe";
                    ((TextBox)commands.FindName("ActivateBox")).Text = $"\"{exe}\" --activate \"Stream\"";
                    ((TextBox)commands.FindName("ToggleBox")).Text = $"\"{exe}\" --toggle \"Stream\"";
                    ((TextBox)commands.FindName("CloseBox")).Text = $"\"{exe}\" --close \"Stream\"";
                    ((TextBox)commands.FindName("ResnapBox")).Text = $"\"{exe}\" --resnap \"Stream\"";
                    ((TextBox)commands.FindName("GamesBox")).Text = $"\"{exe}\" --games";
                    ((TextBox)commands.FindName("PathBox")).Text = exe;
                };
                window = commands;
                width = 640;
                height = double.NaN;
                break;
            }

            case "game-picker":
            {
                var picker = new GamePicker("Stream");
                prepare = () =>
                {
                    Wait(TimeSpan.FromSeconds(2)); // let it finish looking for games, then show demo games instead
                    var games = new List<GameEntry>
                    {
                        Game("Rocket League", GameSource.Epic, favorite: true, played: -1),
                        Game("Counter-Strike 2", GameSource.Steam, played: -0.1),
                        Game("Elden Ring", GameSource.Steam, played: -6),
                        Game("Minecraft", GameSource.Manual),
                        Game("Fortnite", GameSource.Epic),
                        Game("Stardew Valley", GameSource.Steam)
                    };
                    Set(picker, "_games", games);
                    Call(typeof(GamePicker), "ShowGames", picker);
                };
                window = picker;
                width = 520;
                height = 600;
                break;
            }

            case "pin":
                window = new PinDialog(ProfileStore.Find("Late night")!, "open it");
                width = double.NaN;
                height = double.NaN;
                break;

            // Not used in the guide; handy for checking the Help window and the walkthrough after changes.
            case "test-help":
            {
                var help = (Window)Activator.CreateInstance(typeof(HelpWindow), nonPublic: true)!;
                var guide = typeof(LaunchStageApp.App).Assembly.GetType("LaunchStageApp.Services.Guide")!;
                prepare = () => Call(typeof(HelpWindow), "Select", help,
                    guide.GetMethod("Find", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, new object[] { "first-profile" })!);
                window = help;
                width = 1040;
                height = 900;
                break;
            }

            case "test-welcome":
            {
                var tour = (Window)Activator.CreateInstance(typeof(WelcomeWindow), nonPublic: true)!;
                prepare = () => Call(typeof(WelcomeWindow), "ShowPage", tour, 1);
                window = tour;
                width = 780;
                height = 640;
                break;
            }

            default:
                throw new ArgumentException($"Unknown picture '{picture}'.");
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -6000;
        window.Top = -6000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        if (!double.IsNaN(width))
        {
            window.SizeToContent = double.IsNaN(height) ? SizeToContent.Height : SizeToContent.Manual;
            window.Width = width;
        }

        if (!double.IsNaN(height))
        {
            window.MaxHeight = double.PositiveInfinity;
            window.Height = height;
        }

        window.Show();
        Wait(TimeSpan.FromMilliseconds(400));
        prepare?.Invoke();
        Wait(TimeSpan.FromMilliseconds(600));
        Save(window, file);
    }

    private static GameEntry Game(string name, GameSource source, bool favorite = false, double? played = null) => new()
    {
        Id = $"demo:{name}",
        Name = name,
        Source = source,
        Favorite = favorite,
        LastPlayed = played is { } days ? DateTime.Now.AddDays(days) : null
    };

    /// <summary>Saves the inside of the window (no title bar) as a PNG, drawn at 150%.</summary>
    private static void Save(Window window, string file)
    {
        var content = (FrameworkElement)window.Content;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var area = new System.Windows.Rect(0, 0, content.ActualWidth, content.ActualHeight);
            dc.DrawRectangle(window.Background ?? Brushes.Black, null, area);
            dc.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null, area);
        }

        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * Scale), (int)Math.Ceiling(content.ActualHeight * Scale),
            96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        png.Save(stream);
    }

    private static ScrollViewer? FindScroller(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer scroller)
            {
                return scroller;
            }

            if (FindScroller(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Lets WPF do its work (layout, loading, drawing) for a while.</summary>
    private static void Wait(TimeSpan time)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = time };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static void Call(Type type, string method, object? target, params object[] args) =>
        type.GetMethod(method, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(target, args);
}
