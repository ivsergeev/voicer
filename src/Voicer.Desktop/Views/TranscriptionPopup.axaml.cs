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

    public void Show(string text, string mode, double durationSeconds = 4, int maxLength = 0)
    {
        var displayText = maxLength > 0 && text.Length > maxLength
            ? text[..maxLength] + "..."
            : text;
        MessageText.Text = displayText;

        var color = mode switch
        {
            // Outgoing — WS (blue/indigo)
            "ws" => Color.FromArgb(0xC8, 0x15, 0x65, 0xC0),           // blue 800
            "ws_sel" => Color.FromArgb(0xC8, 0x28, 0x35, 0x93),       // indigo 800

            // Outgoing — insert (purple)
            "insert" => Color.FromArgb(0xC8, 0x6A, 0x1B, 0x9A),       // purple 800

            // Incoming (green family)
            "ack_ok" => Color.FromArgb(0xC8, 0x2E, 0x7D, 0x32),       // green
            "ack_progress" => Color.FromArgb(0xC8, 0x55, 0x8B, 0x2F), // olive
            "ack_done" => Color.FromArgb(0xC8, 0x1B, 0x5E, 0x20),     // dark green
            "ack_error" => Color.FromArgb(0xC8, 0xC6, 0x28, 0x28),    // red

            // Service
            "no_clients" => Color.FromArgb(0xC8, 0x61, 0x61, 0x61),   // gray
            _ => Color.FromArgb(0xC8, 0x61, 0x61, 0x61),              // gray (fallback)
        };
        PopupBorder.Background = new SolidColorBrush(color);

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
