using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Taskitify.Core.Imaging;
using Taskitify.Infrastructure;
using Taskitify.Models;
using Taskitify.Services;
using Taskitify.Views;

namespace Taskitify.ViewModels;

/// <summary>The song a card is made for, taken when the card window opens.</summary>
public sealed record LyricCardSource(
    string Title,
    string Artist,
    string? Album,
    IReadOnlyList<LyricCardLine> Lines,
    int CurrentLine,
    BitmapSource? Cover,
    Color PaletteStart,
    Color PaletteEnd,
    TimeSpan? Duration,
    string? Credit,
    Func<Task<byte[]?>>? LargeCover);

public sealed class LyricCardLineItem(int index, LyricCardLine line) : ObservableObject
{
    private bool _isSelected;

    public int Index { get; } = index;

    public LyricCardLine Line { get; } = line;

    public string Text => Line.Text;

    /// <summary>"1:23" when the lyrics are synced.</summary>
    public string? TimeText => Line.Time is { } t ? $"{(int)t.TotalMinutes}:{t.Seconds:00}" : null;

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

public sealed class LyricCardStyleItem(LyricCardStyle style, string label) : ObservableObject
{
    private ImageSource? _thumbnail;

    public LyricCardStyle Style { get; } = style;

    public string Label { get; } = label;

    /// <summary>The style drawn with the chosen lines, small.</summary>
    public ImageSource? Thumbnail { get => _thumbnail; set => Set(ref _thumbnail, value); }
}

public sealed record LyricCardChoice<T>(T Value, string Label, string Glyph = "");

public sealed class LyricCardSwatch(Color color, string? hex, string label)
{
    public Color Color { get; } = color;

    /// <summary>What is saved: null for the album's own colour.</summary>
    public string? Hex { get; } = hex;

    public string Label { get; } = label;

    public SolidColorBrush Brush { get; } = Frozen(color);

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

/// <summary>
/// The lyric card maker: which lines (up to <see cref="MaxLines"/>), the style, format and
/// finer settings, a live preview and copying or saving the image. The last settings are
/// remembered, and any of them can be kept as a named preset.
/// </summary>
public sealed class LyricCardViewModel : ObservableObject
{
    public const int MaxLines = 6;
    public const int MaxPresets = 12;

    // Deep, saturated tones that hold white or black text.
    private static readonly string[] Presets =
    [
        "#0E7C86", "#E24E5A", "#E8A838", "#2F6B4F", "#5B3F9E", "#F28FB1", "#1F2A44", "#EEE6D6", "#121212", "#C8F169",
    ];

    private readonly LyricCardSource _source;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _thumbnailTimer;
    private BitmapSource? _cover;
    private LyricCardOptions _options;
    private ImageSource? _preview;
    private bool _previewQueued;
    private string? _status;
    private bool _updating;
    private int _tab;
    private string _customColor = "";
    private string _presetName = "";

    public LyricCardViewModel(LyricCardSource source, SettingsService settings)
    {
        _source = source;
        _settings = settings;
        _cover = source.Cover;
        _options = settings.Current.LyricCard.Clone();
        // A fixed colour from an older version or another song: start from this album's.
        if (ParseHex(_options.Color) is not null)
            _options.Color = null;
        Title = source.Title;
        Artist = source.Artist;

        for (var i = 0; i < source.Lines.Count; i++)
        {
            var item = new LyricCardLineItem(i, source.Lines[i]);
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LyricCardLineItem.IsSelected) && !_updating)
                    OnSelectionChanged(item);
            };
            Lines.Add(item);
        }
        // The line playing is the card's first line, so it is never empty when there are lyrics.
        if (Lines.Count > 0)
        {
            _updating = true;
            Lines[Math.Clamp(source.CurrentLine, 0, Lines.Count - 1)].IsSelected = true;
            _updating = false;
        }

        StyleItems = Enum.GetValues<LyricCardStyle>().Select(s => new LyricCardStyleItem(s, LocalizationService.Get("CardStyle" + s))).ToList();
        Formats =
        [
            new(LyricCardFormat.Story, "9:16", ""), new(LyricCardFormat.Portrait, "4:5", ""),
            new(LyricCardFormat.Square, "1:1", ""), new(LyricCardFormat.Landscape, "16:9", ""),
        ];
        Fonts = Enum.GetValues<LyricCardFont>().Select(f => new LyricCardChoice<LyricCardFont>(f, LocalizationService.Get("CardFont" + f))).ToList();
        Aligns =
        [
            new(LyricCardAlign.Auto, LocalizationService.Get("CardAlignAuto"), ""), new(LyricCardAlign.Left, LocalizationService.Get("CardAlignLeft"), ""),
            new(LyricCardAlign.Center, LocalizationService.Get("CardAlignCenter"), ""), new(LyricCardAlign.Right, LocalizationService.Get("CardAlignRight"), ""),
        ];
        Marks =
        [
            new(LyricCardMark.Spotify, "Spotify"), new(LyricCardMark.Taskitify, "Taskitify"), new(LyricCardMark.None, LocalizationService.Get("CardMarkNone")),
        ];
        // The album's colours first: as it is, a livelier version, its second colour; then presets.
        Swatches.Add(new LyricCardSwatch(AutomaticAccent, null, LocalizationService.Get("CardColorAlbum")));
        // Album colours are saved by role, not value: the next song gets its own.
        Swatches.Add(new LyricCardSwatch(LyricCardRenderer.Vivid(source.PaletteStart), AlbumVivid, LocalizationService.Get("CardColorAlbumVivid")));
        Swatches.Add(new LyricCardSwatch(source.PaletteEnd, AlbumSecond, LocalizationService.Get("CardColorAlbumSecond")));
        foreach (var hex in Presets)
            AddSwatch((Color)ColorConverter.ConvertFromString(hex), hex);
        _customColor = ParseHex(_options.Color) is null ? "" : _options.Color!;

        foreach (var preset in settings.Current.LyricCardPresets)
            SavedPresets.Add(preset);

        CopyCommand = new RelayCommand(Copy);
        SaveCommand = new RelayCommand(Save);
        ClearCommand = new RelayCommand(() => SetAll(false), () => HasSelection);
        SavePresetCommand = new RelayCommand(SavePreset, () => !string.IsNullOrWhiteSpace(PresetName));
        ApplyPresetCommand = new RelayCommand(p => { if (p is LyricCardPreset preset) Apply(preset.Options.Clone()); });
        DeletePresetCommand = new RelayCommand(p => DeletePreset(p as LyricCardPreset));
        ResetCommand = new RelayCommand(() => Apply(new LyricCardOptions { Style = _options.Style, Format = _options.Format }));
        SelectTabCommand = new RelayCommand(p => Tab = int.TryParse(p?.ToString(), out var t) ? t : 0);

        _thumbnailTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(180) };
        _thumbnailTimer.Tick += (_, _) =>
        {
            _thumbnailTimer.Stop();
            RenderThumbnails();
        };
        Rebuild(thumbnails: true);
        _ = LoadLargeCoverAsync();
    }

    public string Title { get; }

    public string Artist { get; }

    /// <summary>"Lyrics provided by Musixmatch" under the preview, for Spotify's lyrics.</summary>
    public string? Credit => _source.Credit;

    public ObservableCollection<LyricCardLineItem> Lines { get; } = [];

    public IReadOnlyList<LyricCardStyleItem> StyleItems { get; }

    public IReadOnlyList<LyricCardChoice<LyricCardFormat>> Formats { get; }

    public IReadOnlyList<LyricCardChoice<LyricCardFont>> Fonts { get; }

    public IReadOnlyList<LyricCardChoice<LyricCardAlign>> Aligns { get; }

    public IReadOnlyList<LyricCardChoice<LyricCardMark>> Marks { get; }

    public ObservableCollection<LyricCardSwatch> Swatches { get; } = [];

    public ObservableCollection<LyricCardPreset> SavedPresets { get; } = [];

    public ICommand CopyCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand ClearCommand { get; }

    public ICommand SavePresetCommand { get; }

    public ICommand ApplyPresetCommand { get; }

    public ICommand DeletePresetCommand { get; }

    public ICommand ResetCommand { get; }

    public ICommand SelectTabCommand { get; }

    /// <summary>0 lines, 1 style, 2 customize.</summary>
    public int Tab { get => _tab; set => Set(ref _tab, value); }

    public bool HasLines => Lines.Count > 0;

    public bool HasSelection => SelectedCount > 0;

    public int SelectedCount => Lines.Count(l => l.IsSelected);

    /// <summary>"3/6".</summary>
    public string Counter => $"{SelectedCount}/{MaxLines}";

    public bool HasPresets => SavedPresets.Count > 0;

    // ---- Options (each change redraws the preview at once and is remembered) ----

    public LyricCardStyleItem? SelectedStyle
    {
        get => StyleItems.FirstOrDefault(s => s.Style == _options.Style);
        set { if (value is not null) Change(o => o.Style = value.Style, thumbnails: false); }
    }

    public LyricCardChoice<LyricCardFormat>? SelectedFormat
    {
        get => Formats.FirstOrDefault(f => f.Value == _options.Format);
        set { if (value is not null) Change(o => o.Format = value.Value); }
    }

    public LyricCardChoice<LyricCardFont>? SelectedFont
    {
        get => Fonts.FirstOrDefault(f => f.Value == _options.Font);
        set { if (value is not null) Change(o => o.Font = value.Value); }
    }

    public LyricCardChoice<LyricCardAlign>? SelectedAlign
    {
        get => Aligns.FirstOrDefault(a => a.Value == _options.Align);
        set { if (value is not null) Change(o => o.Align = value.Value); }
    }

    public LyricCardChoice<LyricCardMark>? SelectedMark
    {
        get => Marks.FirstOrDefault(m => m.Value == _options.Mark);
        set { if (value is not null) Change(o => o.Mark = value.Value); }
    }

    /// <summary>The lyrics' size, in percent of the style's own.</summary>
    public double FontPercent
    {
        get => Math.Round(_options.FontScale * 100);
        set => Change(o => o.FontScale = Math.Clamp(Math.Round(value / 5) * 5, 60, 150) / 100);
    }

    public bool ShowCover
    {
        get => _options.ShowCover;
        set => Change(o => o.ShowCover = value);
    }

    public bool ShowTimestamp
    {
        get => _options.ShowTimestamp;
        set => Change(o => o.ShowTimestamp = value);
    }

    /// <summary>The timestamp needs synced lyrics.</summary>
    public bool CanShowTimestamp => Lines.Any(l => l.Line.Time is not null);

    public LyricCardSwatch? SelectedSwatch
    {
        get => Swatches.FirstOrDefault(s => string.Equals(s.Hex, _options.Color, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is null) return;
            _customColor = ParseHex(value.Hex) is null ? "" : value.Hex!;
            OnPropertyChanged(nameof(CustomColor));
            Change(o => o.Color = value.Hex);
        }
    }

    /// <summary>Any colour as #RRGGBB; applied once it is a valid one.</summary>
    public string CustomColor
    {
        get => _customColor;
        set
        {
            _customColor = value?.Trim() ?? "";
            OnPropertyChanged();
            if (ParseHex(_customColor) is { } color)
                Change(o => o.Color = Hex(color));
        }
    }

    public string PresetName
    {
        get => _presetName;
        set
        {
            Set(ref _presetName, value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>
    /// The exported image itself, at full size: the window scales it down with high-quality
    /// filtering, so the preview is smooth and exactly what is copied or saved.
    /// </summary>
    public ImageSource? Preview { get => _preview; private set => Set(ref _preview, value); }

    public double PreviewWidth => LyricCardRenderer.SizeOf(_options.Format).Width;

    public double PreviewHeight => LyricCardRenderer.SizeOf(_options.Format).Height;

    /// <summary>"Copied", "Saved", or why not.</summary>
    public string? Status { get => _status; private set => Set(ref _status, value); }

    internal const string AlbumVivid = "album-vivid";
    internal const string AlbumSecond = "album-second";

    private Color Accent => _options.Color switch
    {
        AlbumVivid => LyricCardRenderer.Vivid(_source.PaletteStart),
        AlbumSecond => _source.PaletteEnd,
        var other => ParseHex(other) ?? AutomaticAccent,
    };

    private Color AutomaticAccent
    {
        get
        {
            // The second tone tracks the cover's chromatic accent. Lift it out of the
            // dark range reserved for backgrounds, preserving the hue and neutral covers.
            var end = _source.PaletteEnd;
            var (h, s, l) = AlbumPalette.ToHsl(new Rgb(end.R, end.G, end.B));
            var lightness = Math.Max(s < 0.13 ? 0.32 : 0.41, l);
            var saturation = Math.Min(s, 0.78);
            var selection = AlbumPalette.FromHsl(h, saturation, lightness);
            while (selection.ContrastWithWhite < 4.5 && lightness > 0.08)
            {
                lightness -= 0.02;
                selection = AlbumPalette.FromHsl(h, saturation, lightness);
            }
            return Color.FromRgb(selection.R, selection.G, selection.B);
        }
    }

    /// <summary>
    /// What is remembered for the next card: everything but a fixed colour (that one belongs to
    /// this card, or to a preset), so a new song starts in its own album's colours.
    /// </summary>
    private LyricCardOptions Remembered()
    {
        var remembered = _options.Clone();
        if (ParseHex(remembered.Color) is not null)
            remembered.Color = null;
        return remembered;
    }

    private LyricCardContent Content() => new(
        Title,
        Artist,
        _source.Album,
        Lines.Where(l => l.IsSelected).Select(l => l.Line).ToList(),
        _cover,
        Accent,
        _source.PaletteStart,
        _source.PaletteEnd,
        _source.Duration);

    private void Change(Action<LyricCardOptions> change, bool thumbnails = true)
    {
        change(_options);
        _settings.Update(s => s.LyricCard = Remembered());
        RaiseOptions();
        Rebuild(thumbnails);
    }

    private void Apply(LyricCardOptions options)
    {
        _options = options;
        _customColor = ParseHex(options.Color) is null ? "" : options.Color!;
        _settings.Update(s => s.LyricCard = Remembered());
        OnPropertyChanged(nameof(CustomColor));
        RaiseOptions();
        Rebuild(thumbnails: true);
    }

    private void RaiseOptions()
    {
        foreach (var name in new[]
        {
            nameof(SelectedStyle), nameof(SelectedFormat), nameof(SelectedFont), nameof(SelectedAlign), nameof(SelectedMark), nameof(FontPercent),
            nameof(ShowCover), nameof(ShowTimestamp), nameof(SelectedSwatch), nameof(PreviewWidth), nameof(PreviewHeight),
        })
            OnPropertyChanged(name);
    }

    private void OnSelectionChanged(LyricCardLineItem line)
    {
        if (line.IsSelected && SelectedCount > MaxLines)
        {
            _updating = true;
            line.IsSelected = false;
            _updating = false;
            Status = LocalizationService.Format("CardLinesMax", MaxLines);
            return;
        }
        Status = null;
        RaiseSelection();
        Rebuild(thumbnails: true);
    }

    private void SetAll(bool selected)
    {
        _updating = true;
        foreach (var line in Lines)
            line.IsSelected = selected;
        _updating = false;
        RaiseSelection();
        Rebuild(thumbnails: true);
    }

    private void RaiseSelection()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(Counter));
        CommandManager.InvalidateRequerySuggested();
    }

    private void Rebuild(bool thumbnails)
    {
        // Several changes in a row (a slider drag) draw once.
        if (!_previewQueued)
        {
            _previewQueued = true;
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Render, () =>
            {
                _previewQueued = false;
                Preview = Render();
            });
        }
        if (thumbnails)
        {
            _thumbnailTimer.Stop();
            _thumbnailTimer.Start();
        }
    }

    /// <summary>Every style with the chosen lines, square and small, for the gallery.</summary>
    private void RenderThumbnails()
    {
        var content = Content();
        foreach (var item in StyleItems)
        {
            var options = _options.Clone();
            options.Style = item.Style;
            options.Format = LyricCardFormat.Square;
            try
            {
                item.Thumbnail = LyricCardRenderer.Render(content, options, scale: 0.16);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log.Debug($"Lyric card thumbnail failed: {ex.GetType().Name}");
            }
        }
    }

    private BitmapSource Render() => LyricCardRenderer.Render(Content(), _options);

    private void Copy()
    {
        try
        {
            Clipboard.SetImage(Render());
            Status = LocalizationService.Get("CardCopied");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or System.Runtime.InteropServices.ExternalException)
        {
            // Another app holding the clipboard.
            Log.Warn($"Could not copy the lyric card: {ex.Message}");
            Status = LocalizationService.Get("CardCopyFailed");
        }
    }

    private void Save()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var folder = Path.Combine(pictures, "Taskitify");
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            FileName = SafeFileName($"{Artist} - {Title}") + ".png",
            InitialDirectory = Directory.Exists(folder) ? folder : pictures,
            AddExtension = true,
            DefaultExt = ".png",
        };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            File.WriteAllBytes(dialog.FileName, LyricCardRenderer.EncodePng(Render()));
            Status = LocalizationService.Get("CardSaved");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not save the lyric card: {ex.Message}");
            Status = LocalizationService.Get("CardSaveFailed");
        }
    }

    private void SavePreset()
    {
        var name = PresetName.Trim();
        if (name.Length == 0)
            return;
        var preset = new LyricCardPreset { Name = name[..Math.Min(name.Length, 40)], Options = _options.Clone() };
        if (SavedPresets.FirstOrDefault(p => string.Equals(p.Name, preset.Name, StringComparison.CurrentCultureIgnoreCase)) is { } existing)
            SavedPresets.Remove(existing);
        else if (SavedPresets.Count >= MaxPresets)
            SavedPresets.RemoveAt(SavedPresets.Count - 1);
        SavedPresets.Insert(0, preset);
        PersistPresets();
        PresetName = "";
        Status = LocalizationService.Format("CardPresetSaved", preset.Name);
    }

    private void DeletePreset(LyricCardPreset? preset)
    {
        if (preset is null || !SavedPresets.Remove(preset))
            return;
        PersistPresets();
    }

    private void PersistPresets()
    {
        var list = SavedPresets.ToList();
        _settings.Update(s => s.LyricCardPresets = list);
        OnPropertyChanged(nameof(HasPresets));
    }

    private void AddSwatch(Color color, string label)
    {
        var hex = Hex(color);
        if (Swatches.Any(s => s.Hex == hex || (s.Hex is null && Hex(s.Color) == hex)))
            return;
        Swatches.Add(new LyricCardSwatch(color, hex, label));
    }

    internal static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    internal static Color? ParseHex(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var hex = text.Trim().TrimStart('#');
        if (hex.Length == 3)
            hex = string.Concat(hex.Select(c => $"{c}{c}"));
        return hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : null;
    }

    internal static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return clean.Length == 0 ? "Taskitify" : clean[..Math.Min(clean.Length, 120)];
    }

    /// <summary>A sharper cover than the 256 px the players use, when Spotify's CDN has one.</summary>
    private async Task LoadLargeCoverAsync()
    {
        if (_source.LargeCover is not { } load)
            return;
        try
        {
            if (await load() is not { Length: > 0 } bytes)
                return;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            _cover = image;
            Rebuild(thumbnails: true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The small cover stays.
            Log.Debug($"Lyric card cover not loaded: {ex.GetType().Name}");
        }
    }
}
