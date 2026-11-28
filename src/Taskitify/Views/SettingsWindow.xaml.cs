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

    public SettingsPage? ActivePage { get; private set; }
    public event EventHandler? ActivePageChanged;

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
        LoadAppearance(MiniAppearance, vm.MiniLook);
        LoadAppearance(FloatingAppearance, vm.FloatingLook);
        LoadAppearance(PresentationAppearance, vm.PresentationLook);
        if (vm.MinimalLook is { } minimal)
            LoadToolbar(FloatingToolbarEditor, minimal, minimal.MoveTool);
        LoadToolbar(PresentationToolbarEditor, vm.PresentationToolbar, vm.PresentationToolbar.MoveTool);
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
            _vm.FloatingLook?.SetPreviewActive(false);
            _vm.PresentationLook?.SetPreviewActive(false);
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
        // The floating player stays on top of everything (maximized and full screen too): while the
        // settings are in use they go above it, and back to a normal window once another one is.
        Activated += (_, _) => Topmost = Application.Current.Windows.OfType<MiniPlayerWindow>().Any(w => w.IsVisible && w.IsPinned);
        Deactivated += (_, _) => Topmost = false;
    }

    private void LoadToolbar(ContentControl host, object viewModel, Action<PlayerToolItem, int> move)
    {
        var toolbar = (StackPanel)((DataTemplate)Resources["PlayerToolbar"]).LoadContent();
        toolbar.DataContext = viewModel;
        host.Content = toolbar;
        if (toolbar.FindName("ToolList") is ItemsControl tools)
            WirePlayerToolDrag(tools, move);
    }

    private void LoadAppearance(ContentControl host, MiniPlayerLookViewModel? look)
    {
        var appearance = (StackPanel)((DataTemplate)Resources["PlayerAppearance"]).LoadContent();
        appearance.DataContext = look;
        host.Content = appearance;
        if (look is not null && appearance.FindName("LookNameBox") is TextBox nameBox)
            look.LookSaved += (_, _) => Dispatcher.InvokeAsync(() =>
            {
                nameBox.Focus();
                nameBox.SelectAll();
            }, DispatcherPriority.Input);
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
            .OrderByDescending(entry => terms.All(term => compare.IndexOf(entry.Title, term, options) >= 0))
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
            var sections = body.Children.OfType<FrameworkElement>()
                .Where(section => section.Visibility != Visibility.Collapsed)
                .SelectMany(section => section is ContentControl { Content: StackPanel panel }
                    ? panel.Children.OfType<FrameworkElement>().Where(child => child.Visibility != Visibility.Collapsed)
                    : new[] { section });
            foreach (var section in sections)
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
                    var labels = texts.Where(text => text.Style == labelStyle).ToList();
                    var hints = string.Join(" ", texts.Where(text => text.Style == hintStyle).Select(text => text.Text));
                    foreach (var label in labels)
                        yield return new SearchMatch(page, labels.Count == 1 ? row : label, label.Text, pageTitle.Text, $"{label.Text} {hints} {context}");
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

    /// <summary>
    /// The floating player's simulated button bar: a click shows or hides a button; pressed, held
    /// and dragged, it lifts and takes the place of the one it passes (the player follows live).
    /// </summary>
    private static void WirePlayerToolDrag(ItemsControl PlayerToolList, Action<PlayerToolItem, int> move)
    {
        PlayerToolItem? pressed = null;
        FrameworkElement? pressedFace = null;
        Point pressedAt = default;
        var dragged = false;
        static PlayerToolItem? ItemAt(object source) =>
            (source as FrameworkElement ?? (source as FrameworkContentElement)?.Parent as FrameworkElement)?.DataContext as PlayerToolItem;
        static FrameworkElement? FaceOf(DependencyObject? d)
        {
            for (; d is not null; d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                     ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            {
                if (d is FrameworkElement { Name: "Tool" } face)
                    return face;
            }
            return null;
        }
        static void Lift(FrameworkElement? face, bool up)
        {
            if (face?.RenderTransform is not System.Windows.Media.ScaleTransform scale || scale.IsFrozen)
                return;
            scale.ScaleX = scale.ScaleY = up ? 1.18 : 1;
            face.Opacity = up ? 0.85 : 1;
        }

        PlayerToolList.PreviewMouseLeftButtonDown += (_, e) =>
        {
            pressed = ItemAt(e.OriginalSource);
            pressedFace = FaceOf(e.OriginalSource as DependencyObject);
            pressedAt = e.GetPosition(PlayerToolList);
            dragged = false;
            Lift(pressedFace, pressed is not null);
            if (pressed is not null)
                PlayerToolList.CaptureMouse();
        };
        PlayerToolList.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (PlayerToolList.IsMouseCaptured)
                PlayerToolList.ReleaseMouseCapture();
            Lift(pressedFace, false);
            // A click (no drag): show or hide it.
            if (pressed is { } item && !dragged)
                item.IsShown = !item.IsShown;
            pressed = null;
            pressedFace = null;
        };
        PlayerToolList.PreviewMouseMove += (_, e) =>
        {
            if (pressed is not { } item || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
                return;
            var moved = e.GetPosition(PlayerToolList) - pressedAt;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            dragged = true;
            if (PlayerToolList.IsMouseCaptured)
                PlayerToolList.ReleaseMouseCapture();
            var face = pressedFace;
            pressed = null;
            DragDrop.DoDragDrop(PlayerToolList, new DataObject(typeof(PlayerToolItem), item), DragDropEffects.Move);
            Lift(face, false);
            pressedFace = null;
        };
        PlayerToolList.DragOver += (_, e) =>
        {
            e.Effects = DragDropEffects.None;
            if (e.Data.GetData(typeof(PlayerToolItem)) is not PlayerToolItem item || !PlayerToolList.Items.Contains(item))
                return;
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            if (ItemAt(e.OriginalSource) is { } target && !ReferenceEquals(target, item))
                move(item, PlayerToolList.Items.IndexOf(target));
        };
        PlayerToolList.Drop += (_, e) => e.Handled = true;
    }

    private void UpdatePlayerPreviews()
    {
        var page = SearchResults.Visibility == Visibility.Visible ? (SettingsPage?)null : (SettingsPage)Pages.SelectedIndex;
        _vm.MiniLook?.SetPreviewActive(page is SettingsPage.Bar or SettingsPage.MinimizedBar or SettingsPage.MiniPlayer,
            showLooks: page == SettingsPage.MiniPlayer);
        _vm.FloatingLook?.SetPreviewActive(page == SettingsPage.Floating);
        _vm.PresentationLook?.SetPreviewActive(page == SettingsPage.Player);
        if (page == SettingsPage.LyricsSources)
            _vm.RefreshCacheSummary();
        _vm.MinimalLook?.SetActive(page == SettingsPage.Minimal);
        if (ActivePage != page)
        {
            ActivePage = page;
            ActivePageChanged?.Invoke(this, EventArgs.Empty);
        }
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
    Player,
    Floating,
    Minimal,
    Lyrics,
    LyricsSources,
    Position,
    Advanced,
}
