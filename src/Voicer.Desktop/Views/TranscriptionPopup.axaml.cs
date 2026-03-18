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

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
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

    public void Show(string text, string mode)
    {
        MessageText.Text = text;

        var color = mode switch
        {
            "insert" => Color.FromArgb(0xE0, 0x1B, 0x5E, 0x20),   // green
            "ws_sel" => Color.FromArgb(0xE0, 0x4A, 0x14, 0x8C),   // purple
            _ => Color.FromArgb(0xE0, 0x0D, 0x47, 0xA1),          // blue
        };
        PopupBorder.Background = new SolidColorBrush(color);

        _activePopups.Add(this);
        Show();

        // Position after layout
        Dispatcher.UIThread.Post(() =>
        {
            RepositionAll();
        }, DispatcherPriority.Loaded);

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
