using System.Drawing;
using System.Drawing.Imaging;
namespace Yifan.Platform;

internal static class ScreenGrab
{
    public static string? TryJpegDataUrl(int x, int y, int width, int height, out string? error)
    {
        error = null;
        if (width < 2 || height < 2)
        {
            error = "选区太小。不会假翻。";
            return null;
        }
        try
        {
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
            }
            using var scaled = Scale(bitmap, global::Yifan.Core.Ocr.Ocr.LongestSide);
            using var stream = new MemoryStream();
            var codec = ImageCodecInfo.GetImageEncoders().First(item => item.FormatID == ImageFormat.Jpeg.Guid);
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, 86L);
            scaled.Save(stream, codec, parameters);
            var data = Convert.ToBase64String(stream.ToArray());
            var url = "data:image/jpeg;base64," + data;
            if (!global::Yifan.Core.Ocr.Ocr.IsImageDataUrl(url))
            {
                error = "选区图片太大或无效。不会假翻。";
                return null;
            }
            return url;
        }
        catch (Exception)
        {
            error = "截不到这块屏幕。不会假翻。";
            return null;
        }
    }

    public static string? JpegDataUrl(Bitmap bitmap, out string? error)
    {
        error = null;
        try
        {
            using var scaled = Scale(bitmap, global::Yifan.Core.Ocr.Ocr.LongestSide);
            using var stream = new MemoryStream();
            var codec = ImageCodecInfo.GetImageEncoders().First(item => item.FormatID == ImageFormat.Jpeg.Guid);
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, 86L);
            scaled.Save(stream, codec, parameters);
            var url = "data:image/jpeg;base64," + Convert.ToBase64String(stream.ToArray());
            if (!global::Yifan.Core.Ocr.Ocr.IsImageDataUrl(url))
            {
                error = "选区图片太大或无效。不会假翻。";
                return null;
            }
            return url;
        }
        catch (Exception)
        {
            error = "选区图片太大或无效。不会假翻。";
            return null;
        }
    }

    private static Bitmap Scale(Bitmap source, int longest)
    {
        var edge = Math.Max(source.Width, source.Height);
        if (edge <= longest) return (Bitmap)source.Clone();
        var scale = longest / (double)edge;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(source, 0, 0, width, height);
        return bitmap;
    }
}
