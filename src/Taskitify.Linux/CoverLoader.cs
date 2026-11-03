using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Taskitify.Core.Imaging;

namespace Taskitify.Linux;

/// <summary>A downloaded cover and the gradient made from it.</summary>
internal sealed record Cover(Bitmap Image, Rgb Start, Rgb End);

internal sealed class CoverLoader(HttpClient http)
{
    private const int PaletteSize = 48;

    /// <summary>Fetch and decode the cover. Null when the URL is empty, unreachable or not an image.</summary>
    public async Task<Cover?> LoadAsync(string? url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        try
        {
            var bytes = await FetchAsync(url, ct).ConfigureAwait(false);
            if (bytes is null)
                return null;

            using var full = new MemoryStream(bytes);
            var image = new Bitmap(full);

            using var small = new MemoryStream(bytes);
            using var thumb = WriteableBitmap.DecodeToWidth(small, PaletteSize);
            var (start, end) = Palette(thumb);
            return new Cover(image, start, end);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or ArgumentException or NotSupportedException or TaskCanceledException)
        {
            return null;
        }
    }

    private async Task<byte[]?> FetchAsync(string url, CancellationToken ct)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile)
                return await File.ReadAllBytesAsync(uri.LocalPath, ct).ConfigureAwait(false);
            if (uri.Scheme is "http" or "https")
                return await http.GetByteArrayAsync(uri, ct).ConfigureAwait(false);
        }
        return null;
    }

    private static (Rgb Start, Rgb End) Palette(WriteableBitmap thumb)
    {
        using var fb = thumb.Lock();
        var width = fb.Size.Width;
        var height = fb.Size.Height;
        var stride = fb.RowBytes;
        var buffer = new byte[stride * height];
        System.Runtime.InteropServices.Marshal.Copy(fb.Address, buffer, 0, buffer.Length);

        // AlbumPalette reads BGRA; Skia may hand us RGBA.
        if (fb.Format == PixelFormat.Rgba8888)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var i = (y * stride) + (x * 4);
                    (buffer[i], buffer[i + 2]) = (buffer[i + 2], buffer[i]);
                }
            }
        }

        return AlbumPalette.Extract(buffer, width, height, stride);
    }
}
