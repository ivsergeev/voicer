using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    private int _selectedVkCode;
    private int _selectedModifiers;
    private int _selectedInsertVkCode;
    private int _selectedInsertModifiers;
    private List<(string id, string name)> _devices = [];

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
            HotkeyModifiers = settings.HotkeyModifiers,
            HotkeyKey = settings.HotkeyKey,
            InsertHotkeyModifiers = settings.InsertHotkeyModifiers,
            InsertHotkeyKey = settings.InsertHotkeyKey,
            ModelThreads = settings.ModelThreads,
            MicrophoneDeviceId = settings.MicrophoneDeviceId,
            ShowPopup = settings.ShowPopup,
            IncludeSelectedText = settings.IncludeSelectedText
        };

        _selectedVkCode = settings.HotkeyKey;
        _selectedModifiers = settings.HotkeyModifiers;
        _selectedInsertVkCode = settings.InsertHotkeyKey;
        _selectedInsertModifiers = settings.InsertHotkeyModifiers;

        LoadMicrophones();

        HotkeyTextBox.Text = _platformInfo.GetHotkeyDisplayName(_selectedModifiers, _selectedVkCode);
        InsertHotkeyTextBox.Text = _platformInfo.GetHotkeyDisplayName(_selectedInsertModifiers, _selectedInsertVkCode);
        PortTextBox.Text = _settings.WebSocketPort.ToString();
        ThreadsTextBox.Text = _settings.ModelThreads.ToString();
        ShowPopupCheckBox.IsChecked = _settings.ShowPopup;
        IncludeSelectedTextCheckBox.IsChecked = _settings.IncludeSelectedText;
        AutostartCheckBox.IsChecked = _autoStartService.IsEnabled();
    }

    private void LoadMicrophones()
    {
        _devices = _audioCaptureService.GetMicrophoneDevices();
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

    private void HotkeyTextBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        HotkeyTextBox.Text = "Press a key...";
    }

    private void HotkeyTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;

        int mod = AvaloniaModifiersToMod(e.KeyModifiers);

        if (IsModifierKey(e.Key))
        {
            // Show intermediate state while holding modifiers
            HotkeyTextBox.Text = mod > 0
                ? _platformInfo.GetHotkeyDisplayName(mod, 0) + "..."
                : "Press a key...";
            return;
        }

        _selectedModifiers = mod;
        _selectedVkCode = _platformInfo.KeyToVkCode(e.Key);
        HotkeyTextBox.Text = _platformInfo.GetHotkeyDisplayName(_selectedModifiers, _selectedVkCode);
    }

    private void InsertHotkeyTextBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        InsertHotkeyTextBox.Text = "Press a key...";
    }

    private void InsertHotkeyTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;

        int mod = AvaloniaModifiersToMod(e.KeyModifiers);

        if (IsModifierKey(e.Key))
        {
            InsertHotkeyTextBox.Text = mod > 0
                ? _platformInfo.GetHotkeyDisplayName(mod, 0) + "..."
                : "Press a key...";
            return;
        }

        _selectedInsertModifiers = mod;
        _selectedInsertVkCode = _platformInfo.KeyToVkCode(e.Key);
        InsertHotkeyTextBox.Text = _platformInfo.GetHotkeyDisplayName(_selectedInsertModifiers, _selectedInsertVkCode);
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortTextBox.Text, out int port) || port < 1 || port > 65535)
        {
            Console.WriteLine("Validation: Port must be between 1 and 65535.");
            return;
        }

        if (!int.TryParse(ThreadsTextBox.Text, out int threads) || threads < 1 || threads > 32)
        {
            Console.WriteLine("Validation: Threads must be between 1 and 32.");
            return;
        }

        int idx = MicrophoneCombo.SelectedIndex;
        _settings.MicrophoneDeviceId = idx >= 0 && idx < _devices.Count ? _devices[idx].id : null;
        _settings.HotkeyModifiers = _selectedModifiers;
        _settings.HotkeyKey = _selectedVkCode;
        _settings.InsertHotkeyModifiers = _selectedInsertModifiers;
        _settings.InsertHotkeyKey = _selectedInsertVkCode;
        _settings.WebSocketPort = port;
        _settings.ModelThreads = threads;
        _settings.ShowPopup = ShowPopupCheckBox.IsChecked == true;
        _settings.IncludeSelectedText = IncludeSelectedTextCheckBox.IsChecked == true;

        _autoStartService.SetEnabled(AutostartCheckBox.IsChecked == true);

        SettingsChanged?.Invoke(_settings);
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
