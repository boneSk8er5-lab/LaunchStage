using System.Globalization;
using System.Windows.Media;

namespace LaunchStageApp.Services;

/// <summary>The icons and colors a profile button can use.</summary>
internal static class ProfileIcons
{
    /// <summary>Windows' built-in icon font codes (Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on Windows 10).</summary>
    public static readonly (string Code, string Label)[] Icons =
    {
        ("E714", "Video"),
        ("E722", "Camera"),
        ("E720", "Microphone"),
        ("E8D6", "Music"),
        ("E7FC", "Game"),
        ("E943", "Code"),
        ("E70F", "Edit"),
        ("E790", "Color"),
        ("E787", "Calendar"),
        ("EA80", "Ideas"),
        ("E8F1", "Library"),
        ("E8B7", "Folder"),
        ("E774", "Web"),
        ("E80F", "Home"),
        ("E713", "Settings")
    };

    public static readonly string[] Palette =
    {
        "#00ADB5", // teal
        "#1E88E5", // blue
        "#7E57C2", // purple
        "#EC407A", // pink
        "#FF7043", // orange
        "#FFB300", // amber
        "#43A047", // green
        "#8D6E63", // brown
        "#78909C"  // slate
    };

    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily TextFont = new("Segoe UI");

    public static bool IsGlyph(string? code) =>
        code != null && int.TryParse(code, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);

    /// <summary>The icon character, or the first letter of the name when no icon is picked.</summary>
    public static string GlyphText(string? code, string name)
    {
        if (code != null && int.TryParse(code, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
        {
            return char.ConvertFromUtf32(value);
        }

        string trimmed = name.Trim();
        return trimmed.Length > 0 ? trimmed[..1].ToUpperInvariant() : "?";
    }

    public static Brush BrushFor(string? hex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex))
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                brush.Freeze();
                return brush;
            }
        }
        catch (FormatException)
        {
            // Fall through to teal.
        }

        var teal = new SolidColorBrush(Color.FromRgb(0x00, 0xAD, 0xB5));
        teal.Freeze();
        return teal;
    }
}
