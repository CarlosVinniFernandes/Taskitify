using System.Globalization;
using System.Windows;
using Taskitify.ViewModels;
using Taskitify.Services;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Taskitify.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;
    private readonly SpotifyLyricsSession _spotify;
    private TextBox? _searchBox;

    private sealed record SearchMatch(TabItem Page, FrameworkElement Target, string Title, string PageTitle, string SearchText);

    /// <summary>Opens the given page (the mini player's menu goes straight to its own).</summary>
    public void ShowPage(SettingsPage page)
    {
        _searchBox?.Clear();
        Pages.SelectedIndex = (int)page;
    }

    public SettingsWindow(SettingsViewModel vm, SpotifyLyricsSession spotify)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        _spotify = spotify;
        _spotify.StatusChanged += OnSpotifyStatus;
        Loaded += (_, _) =>
        {
            UpdatePlayerPreviews();
            if (_searchBox is null && Pages.Template.FindName("SearchBox", Pages) is TextBox searchBox)
            {
                _searchBox = searchBox;
                searchBox.TextChanged += (_, _) => UpdateSearch();
            }
        };
        PreviewKeyDown += OnSearchKeyDown;
        Pages.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(Pages, source) is TabItem)
                _searchBox?.Clear();
        };
        Pages.SelectionChanged += (_, e) =>
        {
            if (IsLoaded && ReferenceEquals(e.Source, Pages))
            {
                _searchBox?.Clear();
                UpdatePlayerPreviews();
            }
        };
        LocalizationService.Changed += OnSearchLanguageChanged;
        Closed += (_, _) =>
        {
            _spotify.StatusChanged -= OnSpotifyStatus;
            LocalizationService.Changed -= OnSearchLanguageChanged;
            _vm.MiniLook?.SetPreviewActive(false);
            _vm.MinimalLook?.SetActive(false);
        };
        OnSpotifyStatus(this, EventArgs.Empty);
        // A new Minimal Mode shortcut is typed right here.
        PreviewKeyDown += (_, e) =>
        {
            if (vm.MinimalLook?.Capture(e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key, System.Windows.Input.Keyboard.Modifiers) == true)
                e.Handled = true;
        };
        Deactivated += (_, _) => { if (vm.MinimalLook is { IsCapturing: true } look) look.IsCapturing = false; };
        // A saved look gets its name right away: the cursor waits in it, the default name selected.
        if (vm.MiniLook is { } look)
            look.LookSaved += (_, _) => Dispatcher.InvokeAsync(() =>
            {
                LookNameBox.Focus();
                LookNameBox.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnSearchLanguageChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(UpdateSearch, DispatcherPriority.Loaded);

    private void UpdateSearch()
    {
        if (_searchBox is null)
            return;
        var terms = _searchBox.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var searching = terms.Length > 0;
        var visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        if (SearchResults.Visibility != visibility)
        {
            foreach (TabItem page in Pages.Items)
                ((ScrollViewer)page.Content).Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
            SearchResults.Visibility = visibility;
            UpdatePlayerPreviews();
        }
        if (!searching)
        {
            SearchMatches.ItemsSource = null;
            return;
        }

        var compare = CultureInfo.GetCultureInfo(LocalizationService.Language).CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        var matches = GetSearchEntries()
            .Where(entry => terms.All(term => compare.IndexOf(entry.SearchText, term, options) >= 0))
            .DistinctBy(entry => (entry.Page, entry.Title))
            .ToList();
        SearchMatches.ItemsSource = matches;
        NoSearchResults.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchResults.ScrollToTop();
    }

    private IEnumerable<SearchMatch> GetSearchEntries()
    {
        var pageTitleStyle = (Style)Resources["PageTitle"];
        var pageHintStyle = (Style)Resources["PageHint"];
        var groupHeaderStyle = (Style)Resources["GroupHeader"];
        var groupHintStyle = (Style)Resources["GroupHint"];
        var labelStyle = (Style)FindResource("SettingLabel");
        var hintStyle = (Style)FindResource("SettingHint");
        var rowStyle = (Style)Resources["Row"];
        var firstRowStyle = (Style)Resources["FirstRow"];

        foreach (TabItem page in Pages.Items)
        {
            var body = (StackPanel)((ScrollViewer)page.Content).Content;
            var pageTitle = body.Children.OfType<TextBlock>().First(text => text.Style == pageTitleStyle);
            var pageHint = body.Children.OfType<TextBlock>().FirstOrDefault(text => text.Style == pageHintStyle)?.Text;
            yield return new SearchMatch(page, pageTitle, pageTitle.Text, pageTitle.Text, $"{pageTitle.Text} {pageHint}");

            TextBlock? groupHeader = null;
            string? groupHint = null;
            foreach (FrameworkElement section in body.Children)
            {
                if (section is TextBlock text)
                {
                    if (text.Style == groupHeaderStyle)
                    {
                        groupHeader = text;
                        groupHint = null;
                    }
                    else if (text.Style == groupHintStyle)
                        groupHint = text.Text;
                    continue;
                }

                var context = $"{pageTitle.Text} {groupHeader?.Text} {groupHint}";
                if (groupHeader is not null)
                    yield return new SearchMatch(page, groupHeader, groupHeader.Text, pageTitle.Text, context);

                var rows = Descendants(section).OfType<Border>().Where(row => row.Style == rowStyle || row.Style == firstRowStyle).ToList();
                foreach (var row in rows.Count > 0 ? rows.Cast<FrameworkElement>() : new[] { section })
                {
                    var texts = Descendants(row).OfType<TextBlock>().ToList();
                    var label = texts.FirstOrDefault(text => text.Style == labelStyle);
                    if (label is null)
                        continue;
                    var hints = string.Join(" ", texts.Where(text => text.Style == hintStyle).Select(text => text.Text));
                    yield return new SearchMatch(page, row, label.Text, pageTitle.Text, $"{label.Text} {hints} {context}");
                }
                groupHeader = null;
                groupHint = null;
            }
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject element)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (_searchBox is null || _vm.MinimalLook?.IsCapturing == true)
            return;
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _searchBox.Focus();
            _searchBox.SelectAll();
        }
        else if (e.Key == Key.Escape && SearchResults.IsVisible)
        {
            _searchBox.Clear();
            _searchBox.Focus();
        }
        else if (e.Key == Key.Enter && _searchBox.IsKeyboardFocused && SearchMatches.Items.Count > 0)
            OpenSearchMatch((SearchMatch)SearchMatches.Items[0]);
        else
            return;
        e.Handled = true;
    }

    private void OnSearchResultClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SearchMatch match })
            OpenSearchMatch(match);
    }

    private void OpenSearchMatch(SearchMatch match)
    {
        _searchBox?.Clear();
        Pages.SelectedItem = match.Page;
        match.Page.Focus();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => match.Target.BringIntoView());
    }

    private void UpdatePlayerPreviews()
    {
        var page = SearchResults.Visibility == Visibility.Visible ? (SettingsPage?)null : (SettingsPage)Pages.SelectedIndex;
        _vm.MiniLook?.SetPreviewActive(page is SettingsPage.Bar or SettingsPage.MinimizedBar or SettingsPage.MiniPlayer or SettingsPage.NotMinimal,
            showLooks: page is SettingsPage.MiniPlayer or SettingsPage.NotMinimal);
        _vm.MinimalLook?.SetActive(page == SettingsPage.Minimal);
    }

    private void OnSpotifyStatus(object? sender, EventArgs e) =>
        SpotifyLyricsStatus.SetResourceReference(TextBlock.TextProperty, "Loc." + _spotify.StatusKey);

    private async void OnConnectSpotify(object sender, RoutedEventArgs e)
    {
        ConnectSpotifyButton.IsEnabled = DisconnectSpotifyButton.IsEnabled = false;
        try { await _spotify.ConnectAsync(); }
        finally { ConnectSpotifyButton.IsEnabled = DisconnectSpotifyButton.IsEnabled = true; }
    }

    private async void OnDisconnectSpotify(object sender, RoutedEventArgs e)
    {
        ConnectSpotifyButton.IsEnabled = DisconnectSpotifyButton.IsEnabled = false;
        try { await _spotify.DisconnectAsync(); }
        finally { ConnectSpotifyButton.IsEnabled = DisconnectSpotifyButton.IsEnabled = true; }
    }

    private void OnAutoAppsOpened(object? sender, EventArgs e) => _vm.MinimalLook?.RefreshRunningApps();

    private void OnOffsetMinus(object sender, RoutedEventArgs e) => _vm.NudgeOffset(-250);

    private void OnOffsetPlus(object sender, RoutedEventArgs e) => _vm.NudgeOffset(250);

    private void OnOffsetReset(object sender, RoutedEventArgs e) => _vm.SyncOffsetMs = 0;

    private void OnTrackOffsetMinus(object sender, RoutedEventArgs e) => _vm.NudgeTrackOffset(-100);

    private void OnTrackOffsetPlus(object sender, RoutedEventArgs e) => _vm.NudgeTrackOffset(100);

    private void OnTrackOffsetReset(object sender, RoutedEventArgs e) => _vm.TrackOffsetMs = 0;
}

/// <summary>The settings pages, in their order.</summary>
public enum SettingsPage
{
    General,
    Bar,
    MinimizedBar,
    MiniPlayer,
    Minimal,
    NotMinimal,
    Lyrics,
    Position,
    Advanced,
}
