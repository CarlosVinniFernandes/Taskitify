using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Taskitify.Infrastructure;
using Taskitify.Services;
using Taskitify.Views;

namespace Taskitify.ViewModels;

/// <summary>The song a card is made for, taken when the card window opens.</summary>
public sealed record LyricCardSource(
    string Title,
    string Artist,
    IReadOnlyList<string> Lines,
    int CurrentLine,
    BitmapSource? Cover,
    Color PaletteStart,
    Color PaletteEnd,
    Func<Task<byte[]?>>? LargeCover);

public sealed class LyricCardLine : ObservableObject
{
    private bool _isSelected;

    public LyricCardLine(int index, string text)
    {
        Index = index;
        Text = text;
    }

    public int Index { get; }

    public string Text { get; }

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

public sealed record LyricCardOption<T>(T Value, string Label, string Glyph);

public sealed class LyricCardSwatch(Color color)
{
    public Color Color { get; } = color;

    public SolidColorBrush Brush { get; } = Frozen(color);

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

/// <summary>
/// The lyric card maker: which lines (up to <see cref="MaxLines"/>), the style, colour and
/// format, a live preview, and copying or saving the image.
/// </summary>
public sealed class LyricCardViewModel : ObservableObject
{
    public const int MaxLines = 6;

    // Extra colours besides the album's own: deep, saturated tones that hold white or black text.
    private static readonly Color[] Presets =
    [
        Color.FromRgb(0x0E, 0x7C, 0x86), Color.FromRgb(0xE2, 0x4E, 0x5A), Color.FromRgb(0xE8, 0xA8, 0x38), Color.FromRgb(0x2F, 0x6B, 0x4F),
        Color.FromRgb(0x5B, 0x3F, 0x9E), Color.FromRgb(0xF2, 0x8F, 0xB1), Color.FromRgb(0x1F, 0x2A, 0x44), Color.FromRgb(0xEE, 0xE6, 0xD6),
    ];

    private readonly LyricCardSource _source;
    private BitmapSource? _cover;
    private LyricCardOption<LyricCardStyle> _style;
    private LyricCardOption<LyricCardFormat> _format;
    private LyricCardSwatch _swatch;
    private FrameworkElement? _preview;
    private string? _status;
    private bool _updating;

    public LyricCardViewModel(LyricCardSource source)
    {
        _source = source;
        _cover = source.Cover;
        Title = source.Title;
        Artist = source.Artist;
        for (var i = 0; i < source.Lines.Count; i++)
        {
            var line = new LyricCardLine(i, source.Lines[i]);
            line.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LyricCardLine.IsSelected) && !_updating)
                    OnSelectionChanged(line);
            };
            Lines.Add(line);
        }
        if (Lines.Count > 0)
        {
            _updating = true;
            Lines[Math.Clamp(source.CurrentLine, 0, Lines.Count - 1)].IsSelected = true;
            _updating = false;
        }

        Styles =
        [
            new(LyricCardStyle.Classic, LocalizationService.Get("CardStyleClassic"), ""),
            new(LyricCardStyle.Gradient, LocalizationService.Get("CardStyleGradient"), ""),
            new(LyricCardStyle.Blur, LocalizationService.Get("CardStyleBlur"), ""),
            new(LyricCardStyle.Polaroid, LocalizationService.Get("CardStylePolaroid"), ""),
            new(LyricCardStyle.Minimal, LocalizationService.Get("CardStyleMinimal"), ""),
        ];
        Formats =
        [
            new(LyricCardFormat.Story, LocalizationService.Get("CardFormatStory"), ""),
            new(LyricCardFormat.Square, LocalizationService.Get("CardFormatSquare"), ""),
        ];
        // The album's colours first (the darker one is the usual pick), then the presets.
        var album = new[] { source.PaletteStart, source.PaletteEnd }.Distinct();
        foreach (var color in album.Concat(Presets))
            Swatches.Add(new LyricCardSwatch(color));
        _style = Styles[0];
        _format = Formats[0];
        _swatch = Swatches[0];

        CopyCommand = new RelayCommand(Copy, () => HasSelection);
        SaveCommand = new RelayCommand(Save, () => HasSelection);
        Rebuild();
        _ = LoadLargeCoverAsync();
    }

    public string Title { get; }

    public string Artist { get; }

    public ObservableCollection<LyricCardLine> Lines { get; } = [];

    public IReadOnlyList<LyricCardOption<LyricCardStyle>> Styles { get; }

    public IReadOnlyList<LyricCardOption<LyricCardFormat>> Formats { get; }

    public ObservableCollection<LyricCardSwatch> Swatches { get; } = [];

    public ICommand CopyCommand { get; }

    public ICommand SaveCommand { get; }

    public bool HasLines => Lines.Count > 0;

    public bool HasSelection => Lines.Any(l => l.IsSelected);

    /// <summary>The blurred-cover style uses the cover's own colours, so it needs no colour.</summary>
    public bool UsesColor => SelectedStyle.Value is LyricCardStyle.Classic or LyricCardStyle.Polaroid or LyricCardStyle.Minimal;

    public LyricCardOption<LyricCardStyle> SelectedStyle
    {
        get => _style;
        set
        {
            if (value is not null && Set(ref _style, value))
            {
                OnPropertyChanged(nameof(UsesColor));
                Rebuild();
            }
        }
    }

    public LyricCardOption<LyricCardFormat> SelectedFormat
    {
        get => _format;
        set
        {
            if (value is not null && Set(ref _format, value))
            {
                OnPropertyChanged(nameof(PreviewHeight));
                Rebuild();
            }
        }
    }

    public LyricCardSwatch SelectedSwatch
    {
        get => _swatch;
        set
        {
            if (value is not null && Set(ref _swatch, value))
                Rebuild();
        }
    }

    /// <summary>The card itself, at full size; the window scales it down.</summary>
    public FrameworkElement? Preview { get => _preview; private set => Set(ref _preview, value); }

    public double PreviewHeight => LyricCardRenderer.HeightOf(SelectedFormat.Value);

    /// <summary>"Copied", "Saved", or how many lines are left.</summary>
    public string? Status { get => _status; private set => Set(ref _status, value); }

    public string SelectionHint => LocalizationService.Format("CardLinesHint", MaxLines);

    private LyricCardContent Content() => new(
        Title,
        Artist,
        Lines.Where(l => l.IsSelected).Select(l => l.Text).ToList(),
        _cover,
        SelectedSwatch.Color,
        _source.PaletteStart,
        _source.PaletteEnd);

    private void OnSelectionChanged(LyricCardLine line)
    {
        if (line.IsSelected && Lines.Count(l => l.IsSelected) > MaxLines)
        {
            _updating = true;
            line.IsSelected = false;
            _updating = false;
            Status = LocalizationService.Format("CardLinesMax", MaxLines);
            return;
        }
        Status = null;
        OnPropertyChanged(nameof(HasSelection));
        CommandManager.InvalidateRequerySuggested();
        Rebuild();
    }

    private void Rebuild() => Preview = LyricCardRenderer.Build(Content(), SelectedStyle.Value, SelectedFormat.Value);

    private BitmapSource Render() => LyricCardRenderer.Render(Content(), SelectedStyle.Value, SelectedFormat.Value);

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
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Taskitify");
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            FileName = SafeFileName($"{Artist} - {Title}") + ".png",
            InitialDirectory = Directory.Exists(folder) ? folder : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
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
            Rebuild();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The small cover stays.
            Log.Debug($"Lyric card cover not loaded: {ex.GetType().Name}");
        }
    }
}
