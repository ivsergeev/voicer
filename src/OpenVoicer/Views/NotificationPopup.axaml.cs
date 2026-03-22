using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

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
    private bool _isClosed;

    public event Action? CancelRequested;
    public event Action? RemoveRequested;

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
            _isClosed = true;
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
        if (type == "processing")
        {
            // Persistent popup with cancel button — no auto-close
            autoClose = false;
            ButtonPanel.IsVisible = true;
            RejectButton.IsVisible = false;
            ApproveButton.IsVisible = false;
            CloseButton.IsVisible = false;
            OkButton.IsVisible = false;
            CancelButton.IsVisible = true;
        }
        else if (type == "context")
        {
            // Persistent context popup with remove button
            autoClose = false;
            ButtonPanel.IsVisible = true;
            RejectButton.IsVisible = false;
            ApproveButton.IsVisible = false;
            CloseButton.Content = "Удалить";
            CloseButton.IsVisible = true;
            OkButton.IsVisible = false;
            CancelButton.IsVisible = false;
        }
        else if (type == "done")
        {
            // Agent response: no auto-close, expandable, OK button
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
            // Show full text, hide preview, widen popup
            DescriptionText.IsVisible = false;
            SetFormattedText(FullText, _fullTextContent!);
            FullTextScroll.IsVisible = true;
            ExpandHint.Text = "\u25B2";
            MaxWidth = 600;
        }
        else
        {
            // Show preview, hide full text, restore width
            FullTextScroll.IsVisible = false;
            DescriptionText.IsVisible = true;
            ExpandHint.Text = "\u25BC";
            MaxWidth = 400;
        }

        // Reposition after size change
        Dispatcher.UIThread.Post(RepositionAll, DispatcherPriority.Loaded);
    }

    private static (Color dotColor, Color bgColor, bool autoClose) GetTypeStyle(string type)
    {
        return type switch
        {
            "processing" => (
                Color.FromRgb(0x60, 0xA5, 0xFA), // blue dot
                Color.FromArgb(0xF2, 0x1E, 0x3A, 0x5F), // deep navy bg
                false
            ),
            "context" => (
                Color.FromRgb(0xA7, 0x8B, 0xFA), // purple dot
                Color.FromArgb(0xF2, 0x3B, 0x26, 0x50), // dark purple bg
                false
            ),
            "agent" => (
                Color.FromRgb(0x60, 0xA5, 0xFA), // blue dot
                Color.FromArgb(0xF2, 0x1E, 0x3A, 0x5F), // deep navy bg
                true
            ),
            "subagent" => (
                Color.FromRgb(0x5E, 0xEA, 0xD4), // teal dot
                Color.FromArgb(0xF2, 0x14, 0x3C, 0x41), // dark teal bg
                true
            ),
            "approval" => (
                Color.FromRgb(0xFB, 0xBF, 0x24), // amber dot
                Color.FromArgb(0xF2, 0x4E, 0x34, 0x12), // dark amber bg
                false
            ),
            "done" => (
                Color.FromRgb(0x4A, 0xDE, 0x80), // green dot
                Color.FromArgb(0xF2, 0x1A, 0x3E, 0x30), // dark green bg
                false
            ),
            "error" => (
                Color.FromRgb(0xF8, 0x71, 0x71), // red dot
                Color.FromArgb(0xF2, 0x50, 0x1E, 0x1E), // dark red bg
                false
            ),
            _ => (
                Color.FromRgb(0x9C, 0xA3, 0xAF), // gray dot
                Color.FromArgb(0xF2, 0x2D, 0x2D, 0x32), // dark gray bg
                true
            ),
        };
    }

    private static readonly FontFamily MonoFont = new("Cascadia Code,Consolas,Menlo,monospace");
    private static readonly IBrush CodeFg = new SolidColorBrush(Color.FromRgb(0x5E, 0xEA, 0xD4)); // teal
    private static readonly IBrush CodeBlockFg = new SolidColorBrush(Color.FromRgb(0x7D, 0xD3, 0xFC)); // light blue
    private static readonly IBrush BoldFg = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
    private static readonly IBrush TextFg = new SolidColorBrush(Color.FromArgb(0xDD, 0xE0, 0xE7, 0xEF));
    private static readonly IBrush HeadingFg = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)); // amber

    /// <summary>
    /// Renders markdown-like text into formatted Inlines on a SelectableTextBlock.
    /// Handles: fenced code blocks, inline code, bold, headings.
    /// </summary>
    private static void SetFormattedText(SelectableTextBlock target, string markdown)
    {
        // Clear Text property first — it overrides Inlines if set
        target.Text = null;

        // Access Inlines (lazily created by TextBlock)
        var inlines = target.Inlines;
        if (inlines == null) return;
        inlines.Clear();

        // Split into fenced code blocks and text segments
        var parts = Regex.Split(markdown, @"(```[\s\S]*?```)");

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;

            if (part.StartsWith("```") && part.EndsWith("```"))
            {
                // Fenced code block — strip ``` delimiters and optional lang tag
                var code = part[3..^3];
                var nlIdx = code.IndexOf('\n');
                if (nlIdx >= 0 && nlIdx < 20 && !code[..nlIdx].Contains(' '))
                    code = code[(nlIdx + 1)..]; // strip language tag line
                code = code.Trim('\r', '\n');

                inlines.Add(new LineBreak());
                inlines.Add(new Run(code)
                {
                    FontFamily = MonoFont,
                    Foreground = CodeBlockFg,
                    FontSize = 12,
                });
                inlines.Add(new LineBreak());
            }
            else
            {
                // Regular text — process line by line for headings, then inline formatting
                var lines = part.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (i > 0) inlines.Add(new LineBreak());

                    // Heading (# ... )
                    var headingMatch = Regex.Match(line, @"^(#{1,3})\s+(.+)$");
                    if (headingMatch.Success)
                    {
                        inlines.Add(new Run(headingMatch.Groups[2].Value)
                        {
                            Foreground = HeadingFg,
                            FontWeight = FontWeight.Bold,
                        });
                        continue;
                    }

                    // Inline formatting: bold and inline code
                    AddInlineFormatted(inlines, line);
                }
            }
        }
    }

    /// <summary>
    /// Parses a single line for **bold** and `inline code`, adds Runs to inlines.
    /// </summary>
    private static void AddInlineFormatted(InlineCollection inlines, string text)
    {
        // Pattern: **bold** or `code`
        var pattern = @"(\*\*(.+?)\*\*|`([^`]+)`)";
        int lastEnd = 0;

        foreach (Match m in Regex.Matches(text, pattern))
        {
            // Text before match
            if (m.Index > lastEnd)
            {
                inlines.Add(new Run(text[lastEnd..m.Index]) { Foreground = TextFg });
            }

            if (m.Value.StartsWith("**"))
            {
                inlines.Add(new Run(m.Groups[2].Value)
                {
                    Foreground = BoldFg,
                    FontWeight = FontWeight.Bold,
                });
            }
            else if (m.Value.StartsWith("`"))
            {
                inlines.Add(new Run(m.Groups[3].Value)
                {
                    FontFamily = MonoFont,
                    Foreground = CodeFg,
                    FontSize = 12,
                });
            }

            lastEnd = m.Index + m.Length;
        }

        // Remaining text
        if (lastEnd < text.Length)
        {
            inlines.Add(new Run(text[lastEnd..]) { Foreground = TextFg });
        }
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
        if (_isClosed) return;
        _onApprove?.Invoke();
        _timer.Stop();
        Close();
    }

    private void Reject_Click(object? sender, RoutedEventArgs e)
    {
        if (_isClosed) return;
        _onReject?.Invoke();
        _timer.Stop();
        Close();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        if (_isClosed) return;
        RemoveRequested?.Invoke();
        _timer.Stop();
        Close();
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        if (_isClosed) return;
        _timer.Stop();
        Close();
    }

    /// <summary>
    /// Transitions a persistent popup to a timed "done" state: updates title, description,
    /// style, and starts auto-close timer.
    /// </summary>
    public void Complete(string title, string? description, string? badge = null, double duration = 4)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Complete(title, description, badge, duration));
            return;
        }

        if (_isClosed) return;

        var (dotColor, bgColor, _) = GetTypeStyle("done");
        StatusDot.Foreground = new SolidColorBrush(dotColor);
        TitleText.Text = title;
        PopupBorder.Background = new SolidColorBrush(bgColor);

        _fullTextContent = description;
        _isExpanded = false;

        if (!string.IsNullOrEmpty(description))
        {
            DescriptionText.Text = description;
            DescriptionText.IsVisible = true;
            _isExpandable = true;
            ExpandHint.IsVisible = true;
            ExpandHint.Text = "\u25BC";
        }
        else
        {
            DescriptionText.IsVisible = false;
            _isExpandable = false;
            ExpandHint.IsVisible = false;
            FullTextScroll.IsVisible = false;
        }

        if (!string.IsNullOrEmpty(badge))
        {
            BadgePill.IsVisible = true;
            BadgeText.Text = badge;
        }

        // Switch buttons: hide Cancel, show OK
        CancelButton.IsVisible = false;
        OkButton.IsVisible = true;
        ButtonPanel.IsVisible = true;
        RejectButton.IsVisible = false;
        ApproveButton.IsVisible = false;
        CloseButton.IsVisible = false;

        // Reposition after content change
        Dispatcher.UIThread.Post(RepositionAll, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Closes this popup from external code.
    /// </summary>
    public void Dismiss()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Dismiss);
            return;
        }

        if (_isClosed) return;
        _timer.Stop();
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        // Fire event — handler may call Complete() to transition popup to "cancelling" state.
        // Do NOT close the popup here; the handler or AgentIdle/timeout will dismiss it.
        CancelRequested?.Invoke();
        _timer.Stop();
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isExpandable)
        {
            // Don't interfere with text selection in expanded mode or button clicks
            if (e.Source is Button) return;
            if (_isExpanded && IsInsideFullText(e))
                return; // let SelectableTextBlock handle selection

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

    private bool IsInsideFullText(PointerPressedEventArgs e)
    {
        // Check if click is inside the FullTextScroll or FullText area
        var source = e.Source as Visual;
        while (source != null)
        {
            if (source == FullTextScroll || source == FullText)
                return true;
            source = source.GetVisualParent() as Visual;
        }
        return false;
    }
}
