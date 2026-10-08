using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using LaunchStageApp.Services;

namespace LaunchStageApp.Views;

/// <summary>
/// The "Getting started" walkthrough: a few short pages shown the first time LaunchStage opens (and from Help).
/// Uses the guide's pictures.
/// </summary>
public partial class WelcomeWindow : Window
{
    private sealed record Page(string Title, string Body, string? Picture);

    private static readonly Page[] Pages =
    {
        new("Welcome to LaunchStage",
            "One press sets up your whole workspace: the right apps open, every window lands in its spot on the right monitor, and apps you don't need get out of the way.\n\nThis takes a minute. Click **Next**.\n\n*LaunchStage is made by Bones_84. This is a test version: please don't share it without permission. All rights reserved.*",
            "launcher.png"),
        new("Make a profile",
            "Open your apps and arrange the windows the way you like them. Then click the dashed **New profile** card, type a name, tick the windows that belong, and click **Save profile**.",
            "new-profile.png"),
        new("Open it",
            "Double-click the profile's card. LaunchStage opens the apps one at a time and puts every window in its spot. The box in the corner shows how it's going.\n\nYou can also give a profile a hotkey, or a button on your Stream Deck or button pad.",
            "status-popup.png"),
        new("Fine-tune it",
            "Right-click the card and click **Edit layout & apps...** to drag windows to new spots, add or remove apps, or choose the websites a browser window opens on.",
            "editor.png"),
        new("Close it",
            "Right-click the card and click **Close profile**. Apps close one at a time, just like clicking X, and LaunchStage waits if one asks to save.\n\nNeed help with anything? Click **Help** at the top of the LaunchStage window, or press `F1`.",
            "launcher-list.png")
    };

    private int _page;

    private WelcomeWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        ShowPage(0);
    }

    /// <summary>Shows the walkthrough. When it closes (finished or skipped), it's marked as seen.</summary>
    public static void ShowTour(Window? owner)
    {
        var tour = new WelcomeWindow();
        if (owner != null && owner.IsVisible)
        {
            tour.Owner = owner;
        }
        else
        {
            tour.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        tour.Closed += (_, _) => App.Instance.MarkWelcomeSeen();
        tour.Show();
    }

    private void ShowPage(int index)
    {
        _page = Math.Clamp(index, 0, Pages.Length - 1);
        var page = Pages[_page];

        StepText.Text = _page == 0 ? "Getting started" : $"Step {_page} of {Pages.Length - 1}";
        TitleText.Text = page.Title;
        BodyText.Inlines.Clear();
        foreach (var part in Guide.ParseInline(page.Body))
        {
            BodyText.Inlines.Add(part.Kind switch
            {
                GuideInlineKind.Bold => new Bold(new Run(part.Text)),
                GuideInlineKind.Italic => new Italic(new Run(part.Text)),
                GuideInlineKind.Code => new Run(part.Text) { FontFamily = new FontFamily("Consolas") },
                _ => new Run(part.Text)
            });
        }

        var picture = page.Picture != null ? Guide.Image(page.Picture) : null;
        Picture.Source = picture;
        if (picture != null)
        {
            Picture.MaxWidth = picture.Width;
        }

        PictureFrame.Visibility = picture != null ? Visibility.Visible : Visibility.Collapsed;

        BackButton.IsEnabled = _page > 0;
        bool last = _page == Pages.Length - 1;
        NextButton.Content = last ? "Let's go" : "Next";
        SkipButton.Visibility = last ? Visibility.Hidden : Visibility.Visible;

        Dots.Children.Clear();
        for (int i = 0; i < Pages.Length; i++)
        {
            var dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(4, 0, 4, 0) };
            dot.SetResourceReference(Shape.FillProperty, i == _page ? "AccentBrush" : "ButtonBorderBrush");
            Dots.Children.Add(dot);
        }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_page == Pages.Length - 1)
        {
            Close();
        }
        else
        {
            ShowPage(_page + 1);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowPage(_page - 1);

    private void Skip_Click(object sender, RoutedEventArgs e) => Close();
}
