using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Facet.Controls;

/// <summary>The colour an image "reads as" — e.g. album art — for tinting the UI around it.</summary>
internal static class ImageColor
{
    private const int Sample = 16;

    /// <summary>
    /// A vivid average: pixels weigh by saturation × brightness, so a cover's colourful parts win over its
    /// black/white/grey ones; a nearly colourless image gives its plain average. Null if it can't be read.
    /// </summary>
    public static Color? Of(ImageSource? source)
    {
        if (source is not BitmapSource bitmap || bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0) return null;
        try
        {
            var small = new TransformedBitmap(new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0),
                new ScaleTransform((double)Sample / bitmap.PixelWidth, (double)Sample / bitmap.PixelHeight));
            int w = small.PixelWidth, h = small.PixelHeight;
            var pixels = new byte[w * h * 4];
            small.CopyPixels(pixels, w * 4, 0);

            double r = 0, g = 0, b = 0, weight = 0, pr = 0, pg = 0, pb = 0;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                double pb_ = pixels[i] / 255.0, pg_ = pixels[i + 1] / 255.0, pr_ = pixels[i + 2] / 255.0;
                pr += pr_; pg += pg_; pb += pb_;
                var max = Math.Max(pr_, Math.Max(pg_, pb_));
                var min = Math.Min(pr_, Math.Min(pg_, pb_));
                var saturation = max <= 0 ? 0 : (max - min) / max;
                var w_ = saturation * saturation * max;
                r += pr_ * w_; g += pg_ * w_; b += pb_ * w_; weight += w_;
            }
            var n = pixels.Length / 4.0;
            var (cr, cg, cb) = weight > n * 0.02 ? (r / weight, g / weight, b / weight) : (pr / n, pg / n, pb / n);
            return Color.FromRgb(To255(cr), To255(cg), To255(cb));
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static byte To255(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
}
