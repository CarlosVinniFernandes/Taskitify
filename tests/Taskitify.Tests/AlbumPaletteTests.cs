using Taskitify.Core.Imaging;

namespace Taskitify.Tests;

public class AlbumPaletteTests
{
    private static byte[] Image(int w, int h, Func<int, int, (byte R, byte G, byte B)> pixel)
    {
        var data = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var (r, g, b) = pixel(x, y);
                var i = ((y * w) + x) * 4;
                data[i] = b;
                data[i + 1] = g;
                data[i + 2] = r;
                data[i + 3] = 255;
            }
        }
        return data;
    }

    private static double Hue(Rgb c) => AlbumPalette.ToHsl(c).H * 360;

    [Fact]
    public void Solid_colour_keeps_its_hue_and_gets_darker()
    {
        var (start, end) = AlbumPalette.Extract(Image(16, 16, (_, _) => (220, 30, 30)), 16, 16, 64);

        Assert.InRange(Hue(start), 0, 10);
        Assert.InRange(Hue(end), 0, 10);
        Assert.True(start.Luminance < end.Luminance, "left side is the darker one");
    }

    [Fact]
    public void Vibrant_minority_colour_becomes_the_accent()
    {
        // Mostly grey cover with a blue band: the gradient should end in blue.
        var img = Image(32, 32, (x, _) => x < 24 ? ((byte)120, (byte)120, (byte)120) : ((byte)30, (byte)90, (byte)230));
        var (start, end) = AlbumPalette.Extract(img, 32, 32, 128);

        Assert.InRange(Hue(end), 200, 240);
        Assert.True(AlbumPalette.ToHsl(start).S < 0.1, "dominant grey stays grey");
    }

    [Theory]
    [InlineData(255, 255, 255)]
    [InlineData(255, 240, 0)]
    [InlineData(0, 255, 120)]
    [InlineData(0, 0, 0)]
    public void White_text_is_always_readable(byte r, byte g, byte b)
    {
        var (start, end) = AlbumPalette.Extract(Image(8, 8, (_, _) => (r, g, b)), 8, 8, 32);

        Assert.True(start.ContrastWithWhite >= 4.5, $"start contrast {start.ContrastWithWhite:0.0}");
        Assert.True(end.ContrastWithWhite >= 4.5, $"end contrast {end.ContrastWithWhite:0.0}");
    }

    [Fact]
    public void Empty_or_transparent_image_uses_fallback()
    {
        Assert.Equal(AlbumPalette.Fallback, AlbumPalette.Extract([], 0, 0, 0));
        Assert.Equal(AlbumPalette.Fallback, AlbumPalette.Extract(new byte[64], 4, 4, 16)); // alpha 0
    }

    [Fact]
    public void Hsl_round_trip()
    {
        foreach (var c in new[] { new Rgb(10, 200, 90), new Rgb(250, 10, 10), new Rgb(128, 128, 128) })
        {
            var (h, s, l) = AlbumPalette.ToHsl(c);
            var back = AlbumPalette.FromHsl(h, s, l);
            Assert.InRange(Math.Abs(back.R - c.R), 0, 1);
            Assert.InRange(Math.Abs(back.G - c.G), 0, 1);
            Assert.InRange(Math.Abs(back.B - c.B), 0, 1);
        }
    }

    [Fact]
    public void Swatches_follow_the_cover_most_present_first()
    {
        // Two thirds blue, one third red.
        var swatches = AlbumPalette.Swatches(Image(30, 30, (x, _) => x < 20 ? ((byte)30, (byte)60, (byte)200) : ((byte)210, (byte)40, (byte)40)), 30, 30, 120);
        Assert.Equal(2, swatches.Count);
        Assert.True(swatches[0].Color.B > swatches[0].Color.R);
        Assert.True(swatches[1].Color.R > swatches[1].Color.B);
        Assert.InRange(swatches[0].Share, 0.6, 0.7);
    }

    [Fact]
    public void Swatches_of_a_grey_cover_are_grey()
    {
        var swatches = AlbumPalette.Swatches(Image(20, 20, (x, y) => (x + y) % 2 == 0 ? ((byte)235, (byte)235, (byte)235) : ((byte)90, (byte)90, (byte)90)), 20, 20, 80);
        Assert.All(swatches, s => Assert.True(AlbumPalette.ToHsl(s.Color).S < 0.05));
    }
}
