using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LaunchStage.Core.Logging;
using LaunchStage.Core.Models;
using LaunchStage.Core.Storage;
using LaunchStageApp.Services;

namespace LaunchStageApp.Views;

/// <summary>The launcher: one card per profile. Double-click a card to activate it.</summary>
public partial class MainWindow : Window
{
    private bool _syncingToggle;
    private bool _syncingLayout;

    /// <summary>The launcher layouts, as saved in settings.json (LauncherLayout).</summary>
    public static readonly string[] Layouts = { "Cards", "SmallCards", "List", "Tiles" };

    public MainWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        SyncColorBlindToggle();
        ApplyLayout();
        RefreshProfiles();
        CopyrightText.Text = AppInfo.Copyright;
    }

    private void Copyright_Click(object sender, MouseButtonEventArgs e) => AppInfo.OpenLicense();

    // ---------------- Layout ----------------

    /// <summary>Shows the profiles in the layout chosen in settings (Cards when it's unknown).</summary>
    public void ApplyLayout()
    {
        string layout = Layouts.FirstOrDefault(l => string.Equals(l, App.Instance.Settings.LauncherLayout, StringComparison.OrdinalIgnoreCase)) ?? "Cards";
        ProfilesList.ItemsPanel = (ItemsPanelTemplate)FindResource(layout == "List" ? "StackItems" : "WrapItems");
        ProfilesList.ItemTemplateSelector = new LayoutSelector(layout);

        _syncingLayout = true;
        LayoutCardsButton.IsChecked = layout == "Cards";
        LayoutSmallButton.IsChecked = layout == "SmallCards";
        LayoutListButton.IsChecked = layout == "List";
        LayoutTilesButton.IsChecked = layout == "Tiles";
        _syncingLayout = false;
    }

    private void Layout_Checked(object sender, RoutedEventArgs e)
    {
        if (!_syncingLayout && sender is FrameworkElement { Tag: string layout })
        {
            App.Instance.SetLayout(layout);
        }
    }

    /// <summary>Picks "Card_&lt;layout&gt;" or "New_&lt;layout&gt;" from the window's resources.</summary>
    private sealed class LayoutSelector : DataTemplateSelector
    {
        private readonly string _layout;

        public LayoutSelector(string layout) => _layout = layout;

        public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
            (container as FrameworkElement)?.TryFindResource($"{(item is NewProfileCard ? "New" : "Card")}_{_layout}") as DataTemplate;
    }

    public void RefreshProfiles()
    {
        var items = new List<object>();
        try
        {
            var profiles = ProfileStore.LoadAll()
                .OrderByDescending(p => p.Favorite)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var profile in profiles)
            {
                items.Add(new ProfileCard(profile));
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't list profiles: {ex.Message}");
        }

        items.Add(new NewProfileCard());
        ProfilesList.ItemsSource = items;

        // Profiles may have been added, renamed, deleted or given new hotkeys. (Not while exiting: a dialog that was
        // open when LaunchStage was asked to exit closes and lands here.)
        if (Application.Current is App app && !app.IsExiting)
        {
            app.ReloadHotkeys();
        }
    }

    /// <summary>Locks the controls while a profile is being set up (like Caption Studio does during work).</summary>
    public void SetBusy(bool busy)
    {
        ProfilesList.IsEnabled = !busy;
        ImportButton.IsEnabled = !busy;
        SettingsButton.IsEnabled = !busy;
        BusyText.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SyncColorBlindToggle()
    {
        _syncingToggle = true;
        ColorBlindToggle.IsChecked = App.Instance.Settings.ColorBlindMode;
        _syncingToggle = false;
    }

    // ---------------- Cards ----------------

    private static ProfileCard? CardFrom(object sender) => (sender as FrameworkElement)?.DataContext as ProfileCard;

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && CardFrom(sender) is { } card)
        {
            e.Handled = true;
            _ = App.Instance.Runner.RunAsync(card.Profile.Name);
        }
    }

    private void CardActivate_Click(object sender, RoutedEventArgs e)
    {
        if (CardFrom(sender) is { } card)
        {
            _ = App.Instance.Runner.RunAsync(card.Profile.Name);
        }
    }

    private void CardCloseProfile_Click(object sender, RoutedEventArgs e)
    {
        if (CardFrom(sender) is { } card)
        {
            _ = App.Instance.Runner.CloseAsync(card.Profile.Name);
        }
    }

    private void CardResnap_Click(object sender, RoutedEventArgs e)
    {
        if (CardFrom(sender) is { } card)
        {
            _ = App.Instance.Runner.ResnapAsync(card.Profile.Name);
        }
    }

    private void CardEdit_Click(object sender, RoutedEventArgs e)
    {
        if (CardFrom(sender) is { } card && PinGate.Unlock(card.Profile, "edit it"))
        {
            new ProfileEditor(card.Profile) { Owner = this }.ShowDialog();
            RefreshProfiles();
        }
    }

    private void CardSettings_Click(object sender, RoutedEventArgs e)
    {
        if (CardFrom(sender) is { } card && PinGate.Unlock(card.Profile, "edit it"))
        {
            OpenProfileDialog(card.Profile);
        }
    }

    /// <summary>Makes a full copy ("Stream (2)") and opens its name, look &amp; options window to rename it.</summary>
    private void CardDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (CardFrom(sender) is not { } card || !PinGate.Unlock(card.Profile, "copy it"))
        {
            return;
        }

        Profile copy;
        try
        {
            copy = ProfileStore.Duplicate(card.Profile);
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't duplicate '{card.Name}': {ex}");
            Dialogs.Warn(this, $"Couldn't copy '{card.Name}': {ex.Message}");
            return;
        }

        RefreshProfiles();
        OpenProfileDialog(copy);
    }

    private void CardCommands_Click(object sender, RoutedEventArgs e)
    {
        if (CardFrom(sender) is { } card)
        {
            new CommandsDialog(card.Profile.Name) { Owner = this }.ShowDialog();
        }
    }

    private void CardFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (CardFrom(sender) is not { } card)
        {
            return;
        }

        card.Profile.Favorite = !card.Profile.Favorite;
        TrySave(card.Profile);
        RefreshProfiles();
    }

    private void CardExport_Click(object sender, RoutedEventArgs e)
    {
        if (CardFrom(sender) is { } card && PinGate.Unlock(card.Profile, "export it"))
        {
            ProfileTransfer.Export(this, card.Profile);
        }
    }

    private void CardDelete_Click(object sender, RoutedEventArgs e)
    {
        if (CardFrom(sender) is not { } card || !PinGate.Unlock(card.Profile, "delete it"))
        {
            return;
        }

        if (!Dialogs.Confirm(this, $"Delete the profile '{card.Name}'?\n\nThis can't be undone. (Export it first if you might want it back.)"))
        {
            return;
        }

        try
        {
            ProfileStore.Delete(card.Profile.Name);
        }
        catch (Exception ex)
        {
            Dialogs.Warn(this, $"Couldn't delete it: {ex.Message}");
        }

        RefreshProfiles();
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e) => OpenProfileDialog(null);

    private void OpenProfileDialog(Profile? profile)
    {
        var dialog = new ProfileDialog(profile) { Owner = this };
        dialog.ShowDialog();
        RefreshProfiles();
    }

    private void TrySave(Profile profile)
    {
        try
        {
            ProfileStore.Save(profile);
        }
        catch (Exception ex)
        {
            Dialogs.Warn(this, $"Couldn't save '{profile.Name}': {ex.Message}");
        }
    }

    // ---------------- Header and footer ----------------

    private void ColorBlindToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_syncingToggle)
        {
            App.Instance.SetColorBlind(ColorBlindToggle.IsChecked == true);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => App.Instance.OpenSettings();

    private void Help_Click(object sender, RoutedEventArgs e) => HelpWindow.ShowTopic();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            e.Handled = true;
            HelpWindow.ShowTopic();
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileTransfer.Import(this))
        {
            RefreshProfiles();
        }
    }

    private void CloseApp_Click(object sender, RoutedEventArgs e) => App.Instance.ExitApp();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (App.Instance.IsExiting)
        {
            return;
        }

        if (App.Instance.HasTray)
        {
            // The X hides to the tray; "Close App" exits.
            e.Cancel = true;
            Hide();
        }
        else
        {
            App.Instance.ExitApp();
        }
    }
}
