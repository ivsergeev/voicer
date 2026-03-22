using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Voicer.Desktop.Views;

public partial class TranscriptionPopup : Window
{
    private static readonly List<TranscriptionPopup> _activePopups = [];

    private readonly DispatcherTimer _timer;

    public TranscriptionPopup()
    {
        InitializeComponent();

        _timer = new DispatcherTimer();
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Close();
        };

        Closed += (_, _) =>
        {
            _activePopups.Remove(this);
            RepositionAll();
        };
    }

    /// <summary>
    /// Shows a popup with v2 protocol fields.
    /// </summary>
    /// <param name="text">Transcribed text (may be empty for SendTag)</param>
    /// <param name="context">Selected text / clipboard context (null if none)</param>
    /// <param name="tag">Tag string (null if none)</param>
    /// <param name="mode">Mode: "insert" | "ws" | "ws_sel" | "ws_tag" | "no_clients"</param>
    /// <param name="durationSeconds">Auto-close duration</param>
    /// <param name="maxLength">Max text length (0 = no limit)</param>
    /// <param name="infoMessage">Optional info/error message shown below text</param>
    public void Show(string text, string? context, string? tag, string mode,
        double durationSeconds = 4, int maxLength = 0, string? infoMessage = null)
    {
        // Header
        var (headerString, dotColor, bgColor) = GetHeaderInfo(mode);
        StatusDot.Text = "\u25CF"; // ●
        StatusDot.Foreground = new SolidColorBrush(dotColor);
        HeaderText.Text = headerString;
        PopupBorder.Background = new SolidColorBrush(bgColor);

        // Tag pill
        if (!string.IsNullOrEmpty(tag))
        {
            TagPill.IsVisible = true;
            TagText.Text = tag;
        }

        // Transcription text
        if (!string.IsNullOrEmpty(text))
        {
            var displayText = maxLength > 0 && text.Length > maxLength
                ? text[..maxLength] + "..."
                : text;
            MessageText.Text = $"\u201C{displayText}\u201D"; // "text"
            MessageText.IsVisible = true;
        }

        // Context preview
        if (!string.IsNullOrEmpty(context))
        {
            var firstLine = context.Split('\n')[0];
            if (firstLine.Length > 60) firstLine = firstLine[..60] + "\u2026";
            ContextText.Text = firstLine;
            ContextBorder.IsVisible = true;
        }

        // Info message
        if (!string.IsNullOrEmpty(infoMessage))
        {
            InfoText.Text = infoMessage;
            InfoText.IsVisible = true;
        }

        _activePopups.Add(this);
        Show();

        // Position after layout
        Dispatcher.UIThread.Post(() =>
        {
            RepositionAll();
        }, DispatcherPriority.Loaded);

        _timer.Interval = TimeSpan.FromSeconds(durationSeconds);
        _timer.Start();
    }

    private static (string header, Color dotColor, Color bgColor) GetHeaderInfo(string mode)
    {
        return mode switch
        {
            "ws" => (
                "Отправлено",
                Color.FromRgb(0x4A, 0xDE, 0x80), // green dot
                Color.FromArgb(0xF2, 0x1E, 0x3A, 0x5F) // deep navy bg
            ),
            "ws_sel" => (
                "Отправлено",
                Color.FromRgb(0x4A, 0xDE, 0x80),
                Color.FromArgb(0xF2, 0x23, 0x2A, 0x4B) // dark indigo bg
            ),
            "ws_tag" => (
                "Отправлено",
                Color.FromRgb(0x4A, 0xDE, 0x80),
                Color.FromArgb(0xF2, 0x14, 0x3C, 0x41) // dark teal bg
            ),
            "insert" => (
                "Вставлено",
                Color.FromRgb(0x4A, 0xDE, 0x80),
                Color.FromArgb(0xF2, 0x3B, 0x26, 0x50) // dark purple bg
            ),
            "no_clients" => (
                "Не доставлено",
                Color.FromRgb(0xFB, 0xBF, 0x24), // amber dot
                Color.FromArgb(0xF2, 0x2D, 0x2D, 0x32) // dark gray bg
            ),
            "client_connected" => (
                "Подключён",
                Color.FromRgb(0x4A, 0xDE, 0x80), // green dot
                Color.FromArgb(0xF2, 0x1A, 0x3E, 0x30) // dark green bg
            ),
            "client_disconnected" => (
                "Отключён",
                Color.FromRgb(0x9C, 0xA3, 0xAF), // gray dot
                Color.FromArgb(0xF2, 0x2D, 0x2D, 0x32) // dark gray bg
            ),
            _ => ("", Color.FromRgb(0x9C, 0xA3, 0xAF), Color.FromArgb(0xF2, 0x2D, 0x2D, 0x32)),
        };
    }

    private static void RepositionAll()
    {
        double bottomOffset = 0;

        for (int i = _activePopups.Count - 1; i >= 0; i--)
        {
            var p = _activePopups[i];
            var screen = p.Screens.Primary;
            if (screen == null) continue;

            var workArea = screen.WorkingArea;
            var scaling = screen.Scaling;
            var margin = (int)(12 * scaling);

            // workArea is in physical pixels, Width/Height are in DIPs → multiply by scaling
            p.Position = new PixelPoint(
                workArea.Right - (int)(p.Width * scaling) - margin,
                workArea.Bottom - (int)(bottomOffset * scaling) - (int)(p.Height * scaling) - margin);

            bottomOffset += p.Height + 4;
        }
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _timer.Stop();
        Close();
    }
}
