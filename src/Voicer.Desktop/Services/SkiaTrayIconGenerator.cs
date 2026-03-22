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
            "idle" => RenderIcon(new SKColor(0x1E, 0x3A, 0x5F), new SKColor(0xE0, 0xE7, 0xEF)),
            "idle_claimed" => RenderIcon(new SKColor(0x1E, 0x3A, 0x5F), new SKColor(0xE0, 0xE7, 0xEF)),
            "idle_no_clients" => RenderIcon(new SKColor(0x2D, 0x2D, 0x32), new SKColor(0x6B, 0x72, 0x80)),
            "recording_ws" => RenderIcon(new SKColor(0x1E, 0x3A, 0x5F), SKColors.White, "\u25CF", new SKColor(0x60, 0xA5, 0xFA)),
            "recording_insert" => RenderIcon(new SKColor(0x3B, 0x26, 0x50), SKColors.White, "\u25CF", new SKColor(0xA7, 0x8B, 0xFA)),
            "recording_ws_sel" => RenderIcon(new SKColor(0x23, 0x2A, 0x4B), SKColors.White, "\u25CF", new SKColor(0x81, 0x8C, 0xF8)),
            "recording_ws_noclaim" => RenderIcon(new SKColor(0x2D, 0x2D, 0x32), new SKColor(0x6B, 0x72, 0x80), "\u25CF", new SKColor(0x6B, 0x72, 0x80)),
            "recording_ws_sel_noclaim" => RenderIcon(new SKColor(0x2D, 0x2D, 0x32), new SKColor(0x6B, 0x72, 0x80), "\u25CF", new SKColor(0x6B, 0x72, 0x80)),
            "processing_ws" => RenderIcon(new SKColor(0x1E, 0x3A, 0x5F), new SKColor(0x94, 0xB8, 0xDB), "\u2026", new SKColor(0x60, 0xA5, 0xFA)),
            "processing_insert" => RenderIcon(new SKColor(0x3B, 0x26, 0x50), new SKColor(0xC4, 0xB5, 0xD8), "\u2026", new SKColor(0xA7, 0x8B, 0xFA)),
            "processing_ws_sel" => RenderIcon(new SKColor(0x23, 0x2A, 0x4B), new SKColor(0xA0, 0xA8, 0xD0), "\u2026", new SKColor(0x81, 0x8C, 0xF8)),
            "processing_ws_noclaim" => RenderIcon(new SKColor(0x2D, 0x2D, 0x32), new SKColor(0x6B, 0x72, 0x80), "\u2026", new SKColor(0x6B, 0x72, 0x80)),
            "processing_ws_sel_noclaim" => RenderIcon(new SKColor(0x2D, 0x2D, 0x32), new SKColor(0x6B, 0x72, 0x80), "\u2026", new SKColor(0x6B, 0x72, 0x80)),
            "no_model" => RenderIcon(new SKColor(0x2D, 0x2D, 0x32), new SKColor(0x6B, 0x72, 0x80), "!", new SKColor(0x50, 0x1E, 0x1E)),
            _ => RenderIcon(new SKColor(0x1E, 0x3A, 0x5F), new SKColor(0xE0, 0xE7, 0xEF)),
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
