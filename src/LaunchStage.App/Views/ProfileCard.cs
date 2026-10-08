using System.Windows;
using System.Windows.Media;
using LaunchStage.Core.Desktop;
using LaunchStage.Core.Models;
using LaunchStageApp.Services;

namespace LaunchStageApp.Views;

/// <summary>What one profile button on the launcher shows.</summary>
public sealed class ProfileCard
{
    public ProfileCard(Profile profile) => Profile = profile;

    public Profile Profile { get; }

    public string Name => Profile.Name;

    public string Glyph => ProfileIcons.GlyphText(Profile.Icon, Profile.Name);

    public FontFamily GlyphFont => ProfileIcons.IsGlyph(Profile.Icon) ? ProfileIcons.IconFont : ProfileIcons.TextFont;

    public Brush ColorBrush => ProfileIcons.BrushFor(Profile.Color);

    public Visibility FavoriteVisibility => Profile.Favorite ? Visibility.Visible : Visibility.Collapsed;

    public Visibility LockVisibility => Profile.IsPrivate ? Visibility.Visible : Visibility.Collapsed;

    public string FavoriteMenuText => Profile.Favorite ? "Remove from favorites" : "Add to favorites";

    public string Summary
    {
        get
        {
            if (Profile.IsPrivate)
            {
                return "Private · PIN required";
            }

            int count = Profile.Apps.Count;
            string others = Profile.OtherApps switch
            {
                OtherAppsAction.Close => "closes other apps",
                OtherAppsAction.Minimize => "minimizes other apps",
                _ => "leaves other apps"
            };
            return $"{count} app{(count == 1 ? "" : "s")} · {others}";
        }
    }
}

/// <summary>The dashed "New profile" button at the end of the list.</summary>
public sealed class NewProfileCard
{
}

/// <summary>One open window in the New/Edit profile list.</summary>
public sealed class WindowRow
{
    public WindowRow(WindowInfo window, string title, string detail, bool isChecked)
    {
        Window = window;
        Title = title;
        Detail = detail;
        IsChecked = isChecked;
    }

    public WindowInfo Window { get; }

    /// <summary>The app is running as administrator right now.</summary>
    public bool IsElevated { get; init; }
    public string Title { get; }
    public string Detail { get; }
    public bool IsChecked { get; set; }
}
