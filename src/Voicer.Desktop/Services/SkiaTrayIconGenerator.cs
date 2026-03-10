using System.Runtime.InteropServices;
using SkiaSharp;
using Voicer.Core.Interfaces;

namespace Voicer.Desktop.Services;

public class SkiaTrayIconGenerator : ITrayIconGenerator
{
    private readonly Dictionary<string, byte[]> _cache = new();
    private static readonly SKTypeface s_badgeTypeface = ResolveBadgeTypeface();

    public Stream CreateIconStream(string iconType)
    {
        if (_cache.TryGetValue(iconType, out var cached))
            return new MemoryStream(cached, writable: false);

        var bytes = iconType switch
        {
            "idle" => RenderIcon(new SKColor(46, 125, 50), SKColors.White),
            "recording_ws" => RenderIcon(new SKColor(13, 71, 161), SKColors.White, "\u25CF", new SKColor(66, 133, 244)),
            "recording_insert" => RenderIcon(new SKColor(27, 94, 32), SKColors.White, "\u25CF", new SKColor(76, 175, 80)),
            "processing_ws" => RenderIcon(new SKColor(13, 71, 161), new SKColor(200, 220, 255), "\u2026", new SKColor(66, 133, 244)),
            "processing_insert" => RenderIcon(new SKColor(27, 94, 32), new SKColor(200, 255, 200), "\u2026", new SKColor(76, 175, 80)),
            "no_model" => RenderIcon(new SKColor(97, 97, 97), new SKColor(200, 200, 200), "!", new SKColor(180, 40, 40)),
            _ => RenderIcon(new SKColor(46, 125, 50), SKColors.White),
        };

        _cache[iconType] = bytes;
        return new MemoryStream(bytes, writable: false);
    }

    public void ClearCache() => _cache.Clear();

    private static SKTypeface ResolveBadgeTypeface()
    {
        string[][] candidates;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            candidates = [["Segoe UI"]];
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            candidates = [["SF Pro", "Helvetica Neue", "Helvetica"]];
        else
            candidates = [["Liberation Sans", "DejaVu Sans", "Noto Sans"]];

        foreach (var group in candidates)
        {
            foreach (var family in group)
            {
                var tf = SKTypeface.FromFamilyName(family, SKFontStyle.Bold);
                if (tf != null && tf.FamilyName != SKTypeface.Default.FamilyName)
                    return tf;
            }
        }

        return SKTypeface.FromFamilyName(null, SKFontStyle.Bold);
    }

    private static byte[] RenderIcon(SKColor bgColor, SKColor fgColor, string? badge = null, SKColor? badgeColor = null)
    {
        const int size = 32;
        using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.Transparent);

        using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var fgPaint = new SKPaint { Color = fgColor, IsAntialias = true, Style = SKPaintStyle.Fill };

        // Circular background
        canvas.DrawCircle(16, 16, 15, bgPaint);

        // Microphone head (rounded rect capsule)
        var micRect = new SKRoundRect(new SKRect(12, 5, 20, 17), 4, 4);
        canvas.DrawRoundRect(micRect, fgPaint);

        // Arc under mic
        using var arcPaint = new SKPaint
        {
            Color = fgColor, IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.6f, StrokeCap = SKStrokeCap.Round
        };
        canvas.DrawArc(new SKRect(9, 10, 23, 20), 0, 180, false, arcPaint);

        // Stand line
        canvas.DrawLine(16, 20, 16, 23, arcPaint);

        // Base
        canvas.DrawLine(12, 23, 20, 23, arcPaint);

        // Badge circle
        if (badge != null && badgeColor != null)
        {
            using var badgeBrush = new SKPaint { Color = badgeColor.Value, IsAntialias = true, Style = SKPaintStyle.Fill };
            canvas.DrawCircle(26, 6, 6, badgeBrush);

            using var badgeBorderPen = new SKPaint
            {
                Color = bgColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f
            };
            canvas.DrawCircle(26, 6, 6, badgeBorderPen);

            using var textPaint = new SKPaint
            {
                Color = SKColors.White, IsAntialias = true, TextSize = 8f,
                TextAlign = SKTextAlign.Center, Typeface = s_badgeTypeface
            };
            canvas.DrawText(badge, 26, 9, textPaint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
