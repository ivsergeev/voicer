using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Serilog;
using Voicer.Core.Interfaces;
using Voicer.Core.Models;

namespace Voicer.Desktop.Views;

public partial class SettingsWindow : Window
{
    private const int MOD_ALT = 0x0001;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_SHIFT = 0x0004;

    private readonly AppSettings _settings;
    private readonly IAudioCaptureService _audioCaptureService;
    private readonly IAutoStartService _autoStartService;
    private readonly IPlatformInfo _platformInfo;
    private int _selectedInsertVkCode;
    private int _selectedInsertModifiers;
    private List<(string id, string name)> _devices = [];

    // Dynamic WS hotkey actions
    private readonly List<WsActionEditor> _wsActionEditors = [];

    public event Action<AppSettings>? SettingsChanged;

    // Design-time constructor
    public SettingsWindow() : this(new AppSettings(), null!, null!, null!) { }

    public SettingsWindow(AppSettings settings,
        IAudioCaptureService audioCaptureService,
        IAutoStartService autoStartService,
        IPlatformInfo platformInfo)
    {
        InitializeComponent();

        _audioCaptureService = audioCaptureService;
        _autoStartService = autoStartService;
        _platformInfo = platformInfo;

        _settings = new AppSettings
        {
            ModelDirectory = settings.ModelDirectory,
            ModelFileName = settings.ModelFileName,
            TokensFileName = settings.TokensFileName,
            WebSocketPort = settings.WebSocketPort,
            InsertHotkeyModifiers = settings.InsertHotkeyModifiers,
            InsertHotkeyKey = settings.InsertHotkeyKey,
            WsHotkeyActions = settings.WsHotkeyActions.Select(a => new HotkeyAction
            {
                Modifiers = a.Modifiers,
                KeyCode = a.KeyCode,
                Action = a.Action,
                Tag = a.Tag,
                Label = a.Label,
            }).ToList(),
            ModelThreads = settings.ModelThreads,
            MicrophoneDeviceId = settings.MicrophoneDeviceId,
            ShowPopup = settings.ShowPopup,
            PopupDurationSeconds = settings.PopupDurationSeconds,
            PopupMaxLength = settings.PopupMaxLength,
            NormalizeAudio = settings.NormalizeAudio,
        };

        _selectedInsertVkCode = settings.InsertHotkeyKey;
        _selectedInsertModifiers = settings.InsertHotkeyModifiers;

        LoadMicrophones();

        InsertHotkeyTextBox.Text = _platformInfo.GetHotkeyDisplayName(_selectedInsertModifiers, _selectedInsertVkCode);
        PortTextBox.Text = _settings.WebSocketPort.ToString();
        ThreadsTextBox.Text = _settings.ModelThreads.ToString();
        ShowPopupCheckBox.IsChecked = _settings.ShowPopup;
        PopupDurationTextBox.Text = _settings.PopupDurationSeconds.ToString("0.#");
        PopupMaxLengthTextBox.Text = _settings.PopupMaxLength.ToString();
        NormalizeAudioCheckBox.IsChecked = _settings.NormalizeAudio;
        try { AutostartCheckBox.IsChecked = _autoStartService.IsEnabled(); }
        catch (Exception ex) { Log.Warning(ex, "Failed to read autostart state"); }

        // Build WS hotkey action cards
        foreach (var action in _settings.WsHotkeyActions)
        {
            AddWsActionCard(action);
        }
    }

    private void LoadMicrophones()
    {
        try
        {
            _devices = _audioCaptureService.GetMicrophoneDevices();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to enumerate microphone devices");
            _devices = new List<(string id, string name)>();
        }

        var names = _devices.Select(d => d.name).ToList();
        MicrophoneCombo.ItemsSource = names;

        int selectedIdx = _devices.FindIndex(d => d.id == _settings.MicrophoneDeviceId);
        MicrophoneCombo.SelectedIndex = selectedIdx >= 0 ? selectedIdx : 0;
    }

    private static int AvaloniaModifiersToMod(KeyModifiers km)
    {
        int mod = 0;
        if (km.HasFlag(KeyModifiers.Control)) mod |= MOD_CONTROL;
        if (km.HasFlag(KeyModifiers.Alt)) mod |= MOD_ALT;
        if (km.HasFlag(KeyModifiers.Shift)) mod |= MOD_SHIFT;
        return mod;
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftShift or Key.RightShift
            or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin;

    // --- Insert hotkey ---

    private void InsertHotkeyTextBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        InsertHotkeyTextBox.Text = "Нажмите клавишу...";
    }

    private void InsertHotkeyTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        InsertHotkeyTextBox.Text = _selectedInsertVkCode != 0
            ? _platformInfo.GetHotkeyDisplayName(_selectedInsertModifiers, _selectedInsertVkCode)
            : "Нажмите клавишу...";
    }

    private void InsertHotkeyTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;

        int mod = AvaloniaModifiersToMod(e.KeyModifiers);

        if (IsModifierKey(e.Key))
        {
            InsertHotkeyTextBox.Text = mod > 0
                ? _platformInfo.GetHotkeyDisplayName(mod, 0) + "..."
                : "Нажмите клавишу...";
            return;
        }

        _selectedInsertModifiers = mod;
        _selectedInsertVkCode = _platformInfo.KeyToVkCode(e.Key);
        InsertHotkeyTextBox.Text = _platformInfo.GetHotkeyDisplayName(_selectedInsertModifiers, _selectedInsertVkCode);
    }

    // --- WS hotkey action cards ---

    private class WsActionEditor
    {
        public Border Card = null!;
        public int Modifiers;
        public int KeyCode;
        public WsActionType ActionType;
        public string? Tag;
        public TextBox HotkeyTextBox = null!;
        public TextBox TagTextBox = null!;
        public RadioButton RadioTranscribe = null!;
        public RadioButton RadioWithContext = null!;
        public RadioButton RadioSendTag = null!;
        public TextBlock? ErrorText;
    }

    private void AddWsActionCard(HotkeyAction action)
    {
        var editor = new WsActionEditor
        {
            Modifiers = action.Modifiers,
            KeyCode = action.KeyCode,
            ActionType = action.Action,
            Tag = action.Tag,
        };

        var card = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 12),
            BorderBrush = new SolidColorBrush(Color.Parse("#E0E0E0")),
            BorderThickness = new Thickness(1),
        };

        var stack = new StackPanel { Spacing = 8 };

        // Row 1: Hotkey + record button + delete
        var hotkeyRow = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto,Auto"),
        };

        var hotkeyLabel = new TextBlock
        {
            Text = "Клавиша",
            Foreground = new SolidColorBrush(Color.Parse("#555")),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(hotkeyLabel, 0);

        var hotkeyTextBox = new TextBox
        {
            IsReadOnly = true,
            Text = _platformInfo.GetHotkeyDisplayName(action.Modifiers, action.KeyCode),
            Cursor = new Cursor(StandardCursorType.Hand),
            MinWidth = 100,
        };
        editor.HotkeyTextBox = hotkeyTextBox;
        hotkeyTextBox.GotFocus += (_, _) => hotkeyTextBox.Text = "Нажмите клавишу...";
        hotkeyTextBox.LostFocus += (_, _) =>
        {
            // Restore display if no new key was pressed
            hotkeyTextBox.Text = editor.KeyCode != 0
                ? _platformInfo.GetHotkeyDisplayName(editor.Modifiers, editor.KeyCode)
                : "Нажмите клавишу...";
        };
        hotkeyTextBox.KeyDown += (_, e) =>
        {
            e.Handled = true;
            int mod = AvaloniaModifiersToMod(e.KeyModifiers);
            if (IsModifierKey(e.Key))
            {
                hotkeyTextBox.Text = mod > 0
                    ? _platformInfo.GetHotkeyDisplayName(mod, 0) + "..."
                    : "Нажмите клавишу...";
                return;
            }
            editor.Modifiers = mod;
            editor.KeyCode = _platformInfo.KeyToVkCode(e.Key);
            hotkeyTextBox.Text = _platformInfo.GetHotkeyDisplayName(editor.Modifiers, editor.KeyCode);
        };
        Grid.SetColumn(hotkeyTextBox, 1);

        var deleteBtn = new Button
        {
            Content = "✕",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(6, 2),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        deleteBtn.Click += (_, _) => RemoveWsActionCard(editor);
        Grid.SetColumn(deleteBtn, 3);

        hotkeyRow.Children.Add(hotkeyLabel);
        hotkeyRow.Children.Add(hotkeyTextBox);
        hotkeyRow.Children.Add(deleteBtn);
        stack.Children.Add(hotkeyRow);

        // Row 2: Action radio buttons
        var radioPanel = new StackPanel { Spacing = 2 };

        var radioTranscribe = new RadioButton
        {
            Content = "Распознать → отправить текст",
            FontSize = 12,
            IsChecked = action.Action == WsActionType.TranscribeAndSend,
            GroupName = $"action_{_wsActionEditors.Count}",
        };
        var radioWithContext = new RadioButton
        {
            Content = "Распознать → отправить текст + контекст",
            FontSize = 12,
            IsChecked = action.Action == WsActionType.TranscribeWithContext,
            GroupName = $"action_{_wsActionEditors.Count}",
        };
        var radioSendTag = new RadioButton
        {
            Content = "Отправить тег (без записи)",
            FontSize = 12,
            IsChecked = action.Action == WsActionType.SendTag,
            GroupName = $"action_{_wsActionEditors.Count}",
        };

        editor.RadioTranscribe = radioTranscribe;
        editor.RadioWithContext = radioWithContext;
        editor.RadioSendTag = radioSendTag;

        radioTranscribe.IsCheckedChanged += (_, _) => { if (radioTranscribe.IsChecked == true) editor.ActionType = WsActionType.TranscribeAndSend; };
        radioWithContext.IsCheckedChanged += (_, _) => { if (radioWithContext.IsChecked == true) editor.ActionType = WsActionType.TranscribeWithContext; };
        radioSendTag.IsCheckedChanged += (_, _) => { if (radioSendTag.IsChecked == true) editor.ActionType = WsActionType.SendTag; };

        radioPanel.Children.Add(radioTranscribe);
        radioPanel.Children.Add(radioWithContext);
        radioPanel.Children.Add(radioSendTag);
        stack.Children.Add(radioPanel);

        // Row 3: Tag field
        var tagRow = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*"),
        };
        var tagLabel = new TextBlock
        {
            Text = "Тег",
            Foreground = new SolidColorBrush(Color.Parse("#555")),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(tagLabel, 0);

        var tagTextBox = new TextBox
        {
            Text = action.Tag ?? "",
            Watermark = "необязательно",
            FontSize = 12,
        };
        editor.TagTextBox = tagTextBox;
        tagTextBox.TextChanged += (_, _) => editor.Tag = string.IsNullOrWhiteSpace(tagTextBox.Text) ? null : tagTextBox.Text.Trim();
        Grid.SetColumn(tagTextBox, 1);

        tagRow.Children.Add(tagLabel);
        tagRow.Children.Add(tagTextBox);
        stack.Children.Add(tagRow);

        // Hint
        var hint = new TextBlock
        {
            Text = "Тег передаётся клиенту. Что с ним делать — решает клиент.",
            Foreground = new SolidColorBrush(Color.Parse("#999")),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        };
        stack.Children.Add(hint);

        // Error text
        var errorText = new TextBlock
        {
            Foreground = Brushes.Red,
            FontSize = 11,
            IsVisible = false,
        };
        editor.ErrorText = errorText;
        stack.Children.Add(errorText);

        card.Child = stack;
        editor.Card = card;

        _wsActionEditors.Add(editor);
        WsHotkeyList.Children.Add(card);
    }

    private void RemoveWsActionCard(WsActionEditor editor)
    {
        _wsActionEditors.Remove(editor);
        WsHotkeyList.Children.Remove(editor.Card);
    }

    private void AddAction_Click(object? sender, RoutedEventArgs e)
    {
        AddWsActionCard(new HotkeyAction
        {
            Modifiers = 0,
            KeyCode = 0,
            Action = WsActionType.TranscribeAndSend,
        });
    }

    // --- Save / Cancel ---

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortTextBox.Text, out int port) || port < 1 || port > 65535)
        {
            Log.Warning("Validation: port must be between 1 and 65535");
            return;
        }

        if (!int.TryParse(ThreadsTextBox.Text, out int threads) || threads < 1 || threads > 32)
        {
            Log.Warning("Validation: threads must be between 1 and 32");
            return;
        }

        if (!double.TryParse(PopupDurationTextBox.Text, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double popupDuration)
            || popupDuration < 0.5 || popupDuration > 30)
        {
            Log.Warning("Validation: popup duration must be between 0.5 and 30 seconds");
            return;
        }

        if (!int.TryParse(PopupMaxLengthTextBox.Text, out int popupMaxLength) || popupMaxLength < 0)
        {
            Log.Warning("Validation: max characters must be 0 or greater");
            return;
        }

        // Validate WS hotkey actions
        bool hasErrors = false;
        var seenKeys = new HashSet<(int mod, int key)>();

        // Include insert hotkey in conflict check
        if (_selectedInsertVkCode != 0)
            seenKeys.Add((_selectedInsertModifiers, _selectedInsertVkCode));

        foreach (var editor in _wsActionEditors)
        {
            if (editor.ErrorText != null)
            {
                editor.ErrorText.IsVisible = false;
                editor.ErrorText.Text = "";
            }

            if (editor.KeyCode == 0)
            {
                SetEditorError(editor, "Горячая клавиша не задана");
                hasErrors = true;
                continue;
            }

            var keyPair = (editor.Modifiers, editor.KeyCode);
            if (!seenKeys.Add(keyPair))
            {
                SetEditorError(editor, "Эта клавиша уже занята другим действием");
                hasErrors = true;
                continue;
            }

            if (editor.ActionType == WsActionType.SendTag && string.IsNullOrWhiteSpace(editor.Tag))
            {
                SetEditorError(editor, "Для «Отправить тег» необходимо указать тег");
                hasErrors = true;
                continue;
            }
        }

        if (hasErrors) return;

        // Apply values
        int idx = MicrophoneCombo.SelectedIndex;
        _settings.MicrophoneDeviceId = idx >= 0 && idx < _devices.Count ? _devices[idx].id : null;
        _settings.InsertHotkeyModifiers = _selectedInsertModifiers;
        _settings.InsertHotkeyKey = _selectedInsertVkCode;
        _settings.WebSocketPort = port;
        _settings.ModelThreads = threads;
        _settings.ShowPopup = ShowPopupCheckBox.IsChecked == true;
        _settings.PopupDurationSeconds = popupDuration;
        _settings.PopupMaxLength = popupMaxLength;
        _settings.NormalizeAudio = NormalizeAudioCheckBox.IsChecked == true;

        _settings.WsHotkeyActions = _wsActionEditors.Select(ed => new HotkeyAction
        {
            Modifiers = ed.Modifiers,
            KeyCode = ed.KeyCode,
            Action = ed.ActionType,
            Tag = ed.Tag,
            Label = ed.ActionType switch
            {
                WsActionType.TranscribeAndSend => "Текст → WS",
                WsActionType.TranscribeWithContext => "Текст + контекст → WS",
                WsActionType.SendTag => $"Тег [{ed.Tag}]",
                _ => null,
            },
        }).ToList();

        _autoStartService.SetEnabled(AutostartCheckBox.IsChecked == true);

        SettingsChanged?.Invoke(_settings);
        Close();
    }

    private static void SetEditorError(WsActionEditor editor, string message)
    {
        if (editor.ErrorText != null)
        {
            editor.ErrorText.Text = message;
            editor.ErrorText.IsVisible = true;
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
