using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LaunchStage.Core.Engine;
using LaunchStage.Core.Games;
using LaunchStage.Core.Logging;
using LaunchStageApp.Services;

namespace LaunchStageApp.Views;

/// <summary>
/// "Pick a game": pops up after a profile with "Suggest a game" finishes (or from the tray). Lists installed
/// Steam and Epic games plus games you added, favorites and most recently played first.
/// </summary>
public partial class GamePicker : Window
{
    public sealed class Row
    {
        public Row(GameEntry game)
        {
            Game = game;
            Name = game.Name;
            Detail = $"{game.Source} · {(game.LastPlayed is { } played ? "played " + Ago(played) : "not played from LaunchStage yet")}"
                     + (game.Hidden ? " · hidden" : "");
            StarVisibility = game.Favorite ? Visibility.Visible : Visibility.Collapsed;
            NameOpacity = game.Hidden ? 0.5 : 1.0;
        }

        public GameEntry Game { get; }
        public string Name { get; }
        public string Detail { get; }
        public Visibility StarVisibility { get; }
        public double NameOpacity { get; }

        private static string Ago(DateTime when)
        {
            var span = DateTime.Now - when;
            if (span.TotalMinutes < 2)
            {
                return "just now";
            }

            if (span.TotalHours < 1)
            {
                return $"{(int)span.TotalMinutes} minutes ago";
            }

            if (span.TotalDays < 1)
            {
                return $"{(int)span.TotalHours} hour{((int)span.TotalHours == 1 ? "" : "s")} ago";
            }

            if (span.TotalDays < 2)
            {
                return "yesterday";
            }

            return span.TotalDays < 30 ? $"{(int)span.TotalDays} days ago" : when.ToString("MMM d");
        }
    }

    private List<GameEntry> _games = new();

    /// <param name="profileName">The profile that just finished, or null when opened from the tray.</param>
    public GamePicker(string? profileName = null)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        if (profileName != null)
        {
            TitleText.Text = $"{profileName} is ready. Play a game?";
        }

        EmptyText.Visibility = Visibility.Visible;
        Loaded += async (_, _) =>
        {
            Activate();
            SearchBox.Focus();
            try
            {
                _games = await Task.Run(() => GameLibrary.Load());
            }
            catch (Exception ex)
            {
                Log.Error($"Couldn't list games: {ex}");
                _games = new List<GameEntry>();
            }

            ShowGames();
        };
    }

    private void ShowGames()
    {
        string search = SearchBox.Text.Trim();
        bool showHidden = ShowHiddenBox.IsChecked == true;
        var rows = GameLibrary.Sorted(_games)
            .Where(g => showHidden || !g.Hidden)
            .Where(g => search.Length == 0 || g.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Select(g => new Row(g))
            .ToList();

        var keep = (GamesList.SelectedItem as Row)?.Game;
        GamesList.ItemsSource = rows;
        GamesList.SelectedItem = rows.FirstOrDefault(r => ReferenceEquals(r.Game, keep)) ?? rows.FirstOrDefault();

        EmptyText.Text = _games.Count == 0
            ? "No games found. Steam and Epic games show up here by themselves; add others with + Add game."
            : "No games match.";
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PlayButton.IsEnabled = rows.Count > 0;
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => ShowGames();

    private void ShowHidden_Changed(object sender, RoutedEventArgs e) => ShowGames();

    // Arrow keys in the search box move through the list; Enter plays.
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (SearchBox.IsKeyboardFocused && (e.Key == Key.Down || e.Key == Key.Up) && GamesList.Items.Count > 0)
        {
            int index = Math.Clamp(GamesList.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, GamesList.Items.Count - 1);
            GamesList.SelectedIndex = index;
            GamesList.ScrollIntoView(GamesList.SelectedItem);
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    // ---------------- Playing ----------------

    private void Play_Click(object sender, RoutedEventArgs e) => PlaySelected();

    private void GamesList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => PlaySelected();

    private void PlaySelected()
    {
        if (GamesList.SelectedItem is not Row row)
        {
            return;
        }

        var game = row.Game;
        if (!Launcher.Launch(GameLibrary.ToAppEntry(game), out string? error))
        {
            StatusText.Text = $"{game.Name} didn't start: {error}.";
            StatusText.Visibility = Visibility.Visible;
            Log.Warn($"Game '{game.Name}' didn't start: {error}.");
            return;
        }

        Log.Info($"Started game '{game.Name}' ({game.Launch}) from the game picker.");
        game.LastPlayed = DateTime.Now;
        GameLibrary.Save(_games);
        Close();
    }

    private void NotNow_Click(object sender, RoutedEventArgs e) => Close();

    // ---------------- Opening ----------------

    private static GamePicker? _open;

    /// <summary>Shows the picker (or brings the open one forward). It doesn't block anything else.</summary>
    public static GamePicker ShowPicker(string? profileName)
    {
        if (_open != null)
        {
            _open.Activate();
            return _open;
        }

        var picker = new GamePicker(profileName);
        picker.Closed += (_, _) => _open = null;
        _open = picker;
        picker.Show();
        return picker;
    }

    // ---------------- Managing the list ----------------

    private void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Add a game",
            Filter = "Games and shortcuts (*.exe;*.lnk;*.url)|*.exe;*.lnk;*.url|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var game = GameLibrary.CreateManual(dialog.FileName);
        _games.Add(game);
        GameLibrary.Save(_games);
        SearchBox.Text = "";
        ShowGames();
        GamesList.SelectedItem = (GamesList.ItemsSource as List<Row>)?.FirstOrDefault(r => ReferenceEquals(r.Game, game));
        GamesList.ScrollIntoView(GamesList.SelectedItem);
    }

    private GameEntry? SelectedGame => (GamesList.SelectedItem as Row)?.Game;

    protected override void OnContextMenuOpening(ContextMenuEventArgs e)
    {
        var game = SelectedGame;
        FavoriteItem.Header = game?.Favorite == true ? "Remove from favorites" : "Add to favorites";
        HideItem.Header = game?.Hidden == true ? "Show in this list again" : "Hide from this list";
        RemoveItem.Visibility = game?.Source == GameSource.Manual ? Visibility.Visible : Visibility.Collapsed;
        base.OnContextMenuOpening(e);
    }

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGame is { } game)
        {
            game.Favorite = !game.Favorite;
            GameLibrary.Save(_games);
            ShowGames();
        }
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGame is { } game)
        {
            game.Hidden = !game.Hidden;
            GameLibrary.Save(_games);
            ShowGames();
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGame is { Source: GameSource.Manual } game && Dialogs.Confirm(this, $"Remove {game.Name} from your games?"))
        {
            _games.Remove(game);
            GameLibrary.Save(_games);
            ShowGames();
        }
    }
}
