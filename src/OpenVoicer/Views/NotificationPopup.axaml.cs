using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace OpenVoicer.Views;

public partial class NotificationPopup : Window
{
    private static readonly List<NotificationPopup> _activePopups = [];

    private readonly DispatcherTimer _timer;
    private Action? _onApprove;
    private Action? _onReject;
    private string? _fullTextContent;
    private bool _isExpanded;
    private bool _isExpandable;

    public NotificationPopup()
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

    public void Show(string title, string? description, string type,
        string? badge = null, double duration = 4,
        Action? onApprove = null, Action? onReject = null)
    {
        _onApprove = onApprove;
        _onReject = onReject;
        _fullTextContent = description;
        _isExpanded = false;
        _isExpandable = false;

        var (dotColor, bgColor, autoClose) = GetTypeStyle(type);

        StatusDot.Foreground = new SolidColorBrush(dotColor);
        TitleText.Text = title;
        PopupBorder.Background = new SolidColorBrush(bgColor);

        if (!string.IsNullOrEmpty(badge))
        {
            BadgePill.IsVisible = true;
            BadgeText.Text = badge;
        }

        if (!string.IsNullOrEmpty(description))
        {
            DescriptionText.Text = description;
            DescriptionText.IsVisible = true;
        }

        // Buttons & behavior per type
        if (type == "done")
        {
            // Agent response: no auto-close, expandable, OK button only
            autoClose = false;
            _isExpandable = !string.IsNullOrEmpty(description);
            ExpandHint.IsVisible = _isExpandable;
            ButtonPanel.IsVisible = true;
            RejectButton.IsVisible = false;
            ApproveButton.IsVisible = false;
            CloseButton.IsVisible = false;
            OkButton.IsVisible = true;
        }
        else if (type == "approval" && onApprove != null)
        {
            ButtonPanel.IsVisible = true;
            RejectButton.IsVisible = true;
            ApproveButton.IsVisible = true;
        }
        else if (type == "error")
        {
            ButtonPanel.IsVisible = true;
            CloseButton.IsVisible = true;
        }

        _activePopups.Add(this);
        Show();

        Dispatcher.UIThread.Post(RepositionAll, DispatcherPriority.Loaded);

        if (autoClose)
        {
            _timer.Interval = TimeSpan.FromSeconds(duration);
            _timer.Start();
        }
    }

    private void ToggleExpand()
    {
        if (!_isExpandable || string.IsNullOrEmpty(_fullTextContent)) return;

        _isExpanded = !_isExpanded;

        if (_isExpanded)
        {
            // Show full text, hide preview
            DescriptionText.IsVisible = false;
            FullText.Text = _fullTextContent;
            FullTextScroll.IsVisible = true;
            ExpandHint.Text = "▲";
        }
        else
        {
            // Show preview, hide full text
            FullTextScroll.IsVisible = false;
            DescriptionText.IsVisible = true;
            ExpandHint.Text = "▼";
        }

        // Reposition after size change
        Dispatcher.UIThread.Post(RepositionAll, DispatcherPriority.Loaded);
    }

    private static (Color dotColor, Color bgColor, bool autoClose) GetTypeStyle(string type)
    {
        return type switch
        {
            "agent" => (
                Color.FromRgb(0x42, 0xA5, 0xF5), // blue dot
                Color.FromArgb(0xE8, 0x15, 0x65, 0xC0), // blue bg
                true
            ),
            "subagent" => (
                Color.FromRgb(0x4D, 0xD0, 0xE1), // cyan dot
                Color.FromArgb(0xE8, 0x00, 0x83, 0x8F), // teal bg
                true
            ),
            "approval" => (
                Color.FromRgb(0xFF, 0xA0, 0x00), // orange dot
                Color.FromArgb(0xE8, 0xE6, 0x51, 0x00), // orange bg
                false
            ),
            "done" => (
                Color.FromRgb(0x66, 0xBB, 0x6A), // green dot
                Color.FromArgb(0xE8, 0x2E, 0x7D, 0x32), // green bg
                false // overridden: no auto-close for agent responses
            ),
            "error" => (
                Color.FromRgb(0xEF, 0x53, 0x50), // red dot
                Color.FromArgb(0xE8, 0xC6, 0x28, 0x28), // red bg
                false
            ),
            _ => (
                Color.FromRgb(0x99, 0x99, 0x99),
                Color.FromArgb(0xE8, 0x61, 0x61, 0x61),
                true
            ),
        };
    }

    /// <summary>
    /// Positions popups from top-right corner, stacking downward.
    /// </summary>
    private static void RepositionAll()
    {
        double topOffset = 0;

        for (int i = 0; i < _activePopups.Count; i++)
        {
            var p = _activePopups[i];
            var screen = p.Screens.Primary;
            if (screen == null) continue;

            var workArea = screen.WorkingArea;
            var scaling = screen.Scaling;
            var margin = (int)(12 * scaling);

            p.Position = new PixelPoint(
                workArea.Right - (int)(p.Width * scaling) - margin,
                workArea.Y + (int)(topOffset * scaling) + margin);

            topOffset += p.Height + 4;
        }
    }

    private void Approve_Click(object? sender, RoutedEventArgs e)
    {
        _onApprove?.Invoke();
        _timer.Stop();
        Close();
    }

    private void Reject_Click(object? sender, RoutedEventArgs e)
    {
        _onReject?.Invoke();
        _timer.Stop();
        Close();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        _timer.Stop();
        Close();
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        _timer.Stop();
        Close();
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // For expandable popups: click toggles expand/collapse
        if (_isExpandable)
        {
            // Don't toggle if clicking on buttons
            if (e.Source is Button) return;
            ToggleExpand();
            e.Handled = true;
            return;
        }

        // For auto-close popups: click dismisses
        if (_timer.IsEnabled || !ButtonPanel.IsVisible)
        {
            _timer.Stop();
            Close();
        }
    }
}
