using System.Windows;
using System.Windows.Media;
using LaunchStage.Core.Logging;
using Microsoft.Win32;

namespace LaunchStageApp.Services;

/// <summary>
/// The app's colors: dark (Bones Caption Studio palette) or light, each with a red-green (deuteranopia) vision assist
/// version. "System" follows the Windows light/dark setting and switches by itself when that changes. Every window
/// uses these colors through DynamicResource, so a change shows up right away everywhere.
/// </summary>
internal static class ThemeManager
{
    private static string _theme = "Dark";
    private static bool _colorBlind;
    private static bool _listening;

    /// <summary>True while the dark colors are in use (for the title bars and the tray menu).</summary>
    public static bool IsDark { get; private set; } = true;

    /// <summary>Raised after the colors changed.</summary>
    public static event Action? Changed;

    /// <param name="theme">"Dark", "Light" or "System" (match Windows).</param>
    public static void Apply(string? theme, bool colorBlind)
    {
        _theme = theme is "Light" or "System" ? theme : "Dark";
        _colorBlind = colorBlind;
        if (_theme == "System" && !_listening)
        {
            SystemEvents.UserPreferenceChanged += OnWindowsSettingChanged;
            _listening = true;
        }

        Reapply();
    }

    private static void OnWindowsSettingChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_theme == "System" && e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            Application.Current?.Dispatcher.InvokeAsync(Reapply);
        }
    }

    private static void Reapply()
    {
        bool dark = _theme switch
        {
            "Light" => false,
            "System" => !WindowsUsesLightApps(),
            _ => true
        };

        var resources = Application.Current.Resources;
        foreach (var (key, hex) in Palette(dark, _colorBlind))
        {
            Set(resources, key, hex);
        }

        bool changed = dark != IsDark;
        IsDark = dark;
        DarkTitleBar.UpdateAll();
        Changed?.Invoke();
        if (changed)
        {
            Log.Info($"Colors switched to {(dark ? "dark" : "light")}.");
        }
    }

    /// <summary>Windows' "Choose your app mode" setting: true when apps should be light.</summary>
    private static bool WindowsUsesLightApps()
    {
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                       "AppsUseLightTheme", 1) is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<(string Key, string Hex)> Palette(bool dark, bool colorBlind)
    {
        // Surfaces and text.
        yield return ("WindowBrush", dark ? "#121212" : "#F3F3F3");
        yield return ("PanelBrush", dark ? "#1E1E1E" : "#FFFFFF");
        yield return ("PanelBorderBrush", dark ? "#2D2D2D" : "#DDDDDD");
        yield return ("InputBrush", dark ? "#151515" : "#F7F7F7");
        yield return ("ButtonBrush", dark ? "#2D2D2D" : "#EBEBEB");
        yield return ("ButtonBorderBrush", dark ? "#3D3D3D" : "#CCCCCC");
        yield return ("ButtonHoverBrush", dark ? "#3D3D3D" : "#DFDFDF");
        yield return ("PressedBrush", dark ? "#222831" : "#CFE3E4");
        yield return ("DisabledBackBrush", dark ? "#151515" : "#F0F0F0");
        yield return ("DisabledBorderBrush", dark ? "#222222" : "#E3E3E3");
        yield return ("TextBrush", dark ? "#E0E0E0" : "#1F1F1F");
        yield return ("TextSecondaryBrush", dark ? "#888888" : "#5F5F5F");
        yield return ("DisabledTextBrush", dark ? "#555555" : "#A6A6A6");
        yield return ("CardHoverBrush", dark ? "#252525" : "#EEF7F7");
        yield return ("DangerButtonBrush", dark ? "#252525" : "#F6F6F6");
        yield return ("ScrollThumbBrush", dark ? "#3D3D3D" : "#C2C2C2");
        yield return ("AccentDisabledBrush", dark ? "#1A3A3A" : "#B5DCDE");

        // Accent and status colors (swapped by red-green vision assist). In assist mode: blue = good,
        // yellow = warning, orange = problem. Every status also has a symbol and words, so color is never the only signal.
        if (dark)
        {
            yield return ("AccentBrush", colorBlind ? "#1565C0" : "#00ADB5");
            yield return ("AccentHoverBrush", colorBlind ? "#1976D2" : "#00FFF5");
            yield return ("OnAccentBrush", colorBlind ? "#FFFFFF" : "#121212");
            yield return ("AccentTextBrush", "#EEEEEE");
            // Delete / Close App: orange in assist mode (the same "problem" color as everywhere else).
            yield return ("DangerTextBrush", colorBlind ? "#FF9800" : "#FF5A5A");
            yield return ("DangerHoverBrush", colorBlind ? "#E65100" : "#3A1E1E");
            yield return ("DangerHoverTextBrush", "#FFFFFF");
            yield return ("DangerBorderBrush", colorBlind ? "#FF9800" : "#FF5A5A");
            yield return ("GoodBrush", colorBlind ? "#64B5F6" : "#00ADB5");
            yield return ("WarnBrush", colorBlind ? "#FFD54F" : "#FFC857");
            yield return ("BadBrush", colorBlind ? "#FF9800" : "#FF5A5A");
        }
        else
        {
            // Darker shades, so they stay readable on white.
            yield return ("AccentBrush", colorBlind ? "#1565C0" : "#00838F");
            yield return ("AccentHoverBrush", colorBlind ? "#1976D2" : "#00ADB5");
            yield return ("OnAccentBrush", "#FFFFFF");
            yield return ("AccentTextBrush", "#FFFFFF");
            yield return ("DangerTextBrush", colorBlind ? "#D84315" : "#C62828");
            yield return ("DangerHoverBrush", colorBlind ? "#FBE9E7" : "#FDECEC");
            yield return ("DangerHoverTextBrush", colorBlind ? "#BF360C" : "#B71C1C");
            yield return ("DangerBorderBrush", colorBlind ? "#E65100" : "#E57373");
            yield return ("GoodBrush", colorBlind ? "#1565C0" : "#00838F");
            yield return ("WarnBrush", colorBlind ? "#8D6E00" : "#9A6700");
            yield return ("BadBrush", colorBlind ? "#E65100" : "#C62828");
        }
    }

    private static void Set(ResourceDictionary resources, string key, string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        resources[key] = brush;
    }
}
