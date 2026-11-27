using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Taskitify.Infrastructure;
using Taskitify.Models;
using Taskitify.Services;
using Taskitify.Views;

namespace Taskitify.ViewModels;

public sealed class StudioFieldEditor(StudioField spec, string value, Action<string> apply)
{
    private string _value = value;
    public string Label => LocalizationService.Get("Field." + spec.Label);
    public IReadOnlyList<string>? Choices => spec.Choices;
    public bool HasChoices => Choices is not null;

    public string Value
    {
        get => _value;
        set
        {
            if (value is null || _value == value)
                return;
            _value = value;
            apply(value);
        }
    }
}

public sealed partial class LyricCardViewModel
{
    private BitmapSource? _personalImage;
    private string? _personalPath;
    private string _styleSearch = "";
    private readonly Queue<LyricCardStyleItem> _pendingTiles = new();
    private bool _tilesDirty = true;
    private bool _closed;

    public ListCollectionView Gallery { get; private set; } = null!;
    public IReadOnlyList<StudioFieldEditor> StyleFields { get; private set; } = [];
    public IReadOnlyList<LyricCardChoice<StudioImageSource>> Images { get; private set; } = [];
    public ICommand ChoosePhotoCommand { get; private set; } = null!;
    public bool HasStyleFields => StyleFields.Count > 0;
    public bool HasImageOptions => LyricStudioCatalog.Get(_options.Style).Image;
    public bool CanShowCoverToggle => _options.Style <= LyricCardStyle.Retro;
    public bool CanChooseTypeface => _options.Style is not (LyricCardStyle.Telegram or LyricCardStyle.FloppyDisk
        or LyricCardStyle.Terminal or LyricCardStyle.BlueScreen or LyricCardStyle.Calculator or LyricCardStyle.LyricReceipt
        or LyricCardStyle.ClassifiedDocument or LyricCardStyle.GameBoy or LyricCardStyle.NintendoDs or LyricCardStyle.PokemonEncounter);

    private void InitializeGallery()
    {
        Gallery = new ListCollectionView(StyleItems.ToList())
        {
            Filter = value => value is LyricCardStyleItem tile && CultureInfo.CurrentCulture.CompareInfo.IndexOf(
                tile.Label, _styleSearch.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0,
        };
        Gallery.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LyricCardStyleItem.Category)));
        Images =
        [
            new(StudioImageSource.Album, LocalizationService.Get("StudioImageAlbum")),
            new(StudioImageSource.Personal, LocalizationService.Get("StudioImagePersonal")),
            new(StudioImageSource.None, LocalizationService.Get("StudioImageNone")),
        ];
        ChoosePhotoCommand = new RelayCommand(ChoosePhoto);
        UpdateFields();
    }

    public string StyleSearch
    {
        get => _styleSearch;
        set
        {
            if (!Set(ref _styleSearch, value ?? ""))
                return;
            Gallery.Refresh();
            OnPropertyChanged(nameof(HasStyleResults));
            OnPropertyChanged(nameof(SelectedStyle));
        }
    }

    public bool HasStyleResults => !Gallery.IsEmpty;

    public LyricCardChoice<StudioImageSource>? SelectedImage
    {
        get => Images.FirstOrDefault(choice => choice.Value == (_options.Variants.GetValueOrDefault(_options.Style)?.ImageSource ?? StudioImageSource.Album));
        set
        {
            if (value is null || value.Value == SelectedImage?.Value)
                return;
            if (value.Value == StudioImageSource.Personal && _options.Variants.GetValueOrDefault(_options.Style)?.PhotoPath is null)
            {
                ChoosePhoto();
                OnPropertyChanged();
                return;
            }
            Change(options =>
            {
                Variant(options, options.Style).ImageSource = value.Value;
            });
        }
    }

    private static LyricCardFont TypefaceFor(LyricCardOptions options, LyricCardStyle style) =>
        options.Variants.GetValueOrDefault(style)?.Typeface ?? LyricCardFont.Auto;

    // Older settings and presets stored the font beside the selected composition.
    private static void RememberTypeface(LyricCardOptions options)
    {
        if (options.Font != LyricCardFont.Auto && options.Variants.GetValueOrDefault(options.Style)?.Typeface is null)
            Variant(options, options.Style).Typeface = options.Font;
        options.Font = TypefaceFor(options, options.Style);
    }

    private static LyricCardVariant Variant(LyricCardOptions options, LyricCardStyle style)
    {
        if (!options.Variants.TryGetValue(style, out var variant))
            options.Variants[style] = variant = new LyricCardVariant();
        return variant;
    }

    private void UpdateFields()
    {
        var style = _options.Style;
        var variant = _options.Variants.GetValueOrDefault(style);
        var time = Lines.FirstOrDefault(l => l.IsSelected)?.TimeText ?? "—";
        StyleFields = LyricStudioCatalog.Get(style).Fields.Select(field =>
        {
            var original = field.Default.Replace("{title}", Title).Replace("{artist}", Artist)
                .Replace("{date}", DateTime.Today.ToString("dd MMM yyyy", CultureInfo.CurrentCulture)).Replace("{time}", time)
                .Replace("{duration}", _source.Duration is { } duration ? duration.ToString(@"m\:ss", CultureInfo.CurrentCulture) : "—");
            return new StudioFieldEditor(field, variant?.Fields?.GetValueOrDefault(field.Key) ?? original,
                value => Change(options => Variant(options, style).Fields[field.Key] = value));
        }).ToArray();
        foreach (var name in new[] { nameof(StyleFields), nameof(HasStyleFields), nameof(HasImageOptions),
            nameof(CanShowCoverToggle), nameof(CanChooseTypeface), nameof(SelectedImage) })
            OnPropertyChanged(name);
    }

    private void QueueThumbnails()
    {
        if (_closed)
            return;
        if (_tilesDirty)
        {
            _pendingTiles.Clear();
            foreach (var tile in StyleItems)
                _pendingTiles.Enqueue(tile);
            _tilesDirty = false;
        }
        if (_pendingTiles.Count > 0)
            _thumbnailTimer.Start();
    }

    private void RenderThumbnail()
    {
        if (_closed || !_pendingTiles.TryDequeue(out var tile))
        {
            _thumbnailTimer.Stop();
            return;
        }
        var options = _options.Clone();
        options.Style = tile.Style;
        options.Font = TypefaceFor(options, tile.Style);
        options.Format = LyricCardFormat.Square;
        try
        {
            tile.Thumbnail = LyricCardRenderer.Render(Content(tile.Style, thumbnail: true), options, scale: 0.12);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Debug($"Lyric card thumbnail failed: {ex.GetType().Name}");
        }
    }

    public void Dispose()
    {
        _closed = true;
        _thumbnailTimer.Stop();
        _pendingTiles.Clear();
    }

    private static string StudioImages => Path.Combine(AppPaths.LocalDataDirectory, "studio-images");

    private void ChoosePhoto()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            var image = DecodeImage(dialog.FileName, 1920);
            Directory.CreateDirectory(StudioImages);
            var file = Guid.NewGuid().ToString("N") + ".png";
            File.WriteAllBytes(Path.Combine(StudioImages, file), LyricCardRenderer.EncodePng(image));
            _personalPath = file;
            _personalImage = image;
            Change(options =>
            {
                var variant = Variant(options, options.Style);
                variant.ImageSource = StudioImageSource.Personal;
                variant.PhotoPath = file;
            });
        }
        catch (Exception error) when (IsImageError(error))
        {
            Status = LocalizationService.Get("StudioImageFailed");
        }
    }

    private BitmapSource? ReadPhoto(LyricCardStyle style, bool thumbnail)
    {
        var variant = _options.Variants.GetValueOrDefault(style);
        if (variant is not { ImageSource: StudioImageSource.Personal, PhotoPath: { Length: > 0 } file } || Path.GetFileName(file) != file)
            return null;
        if (!thumbnail && _personalPath == file)
            return _personalImage;
        try
        {
            var photo = DecodeImage(Path.Combine(StudioImages, file), thumbnail ? 300 : 1920);
            if (!thumbnail)
            {
                _personalPath = file;
                _personalImage = photo;
            }
            return photo;
        }
        catch (Exception error) when (IsImageError(error))
        {
            return null;
        }
    }

    private static bool IsImageError(Exception ex) => ex is IOException or UnauthorizedAccessException or NotSupportedException
        or FormatException or ArgumentException or System.Runtime.InteropServices.COMException;

    private static BitmapSource DecodeImage(string path, int size)
    {
        using var stream = File.OpenRead(path);
        var header = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        var width = header.PixelWidth;
        var height = header.PixelHeight;
        stream.Position = 0;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (width >= height) bitmap.DecodePixelWidth = Math.Min(size, width);
        else bitmap.DecodePixelHeight = Math.Min(size, height);
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
