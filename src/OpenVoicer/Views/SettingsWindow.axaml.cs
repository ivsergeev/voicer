using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenVoicer.Models;
using OpenVoicer.Services;

namespace OpenVoicer.Views;

public partial class SettingsWindow : Window
{
    private readonly OpenVoicerSettings _original;
    private readonly OpenCodeClient? _openCodeClient;
    private List<(string Id, string Name, List<string> Models)> _providers = new();
    private List<(string Name, string Description, string Mode)> _agents = new();

    public event Action<OpenVoicerSettings>? SettingsChanged;

    public SettingsWindow() : this(new OpenVoicerSettings(), null) { }

    public SettingsWindow(OpenVoicerSettings settings, OpenCodeClient? openCodeClient = null)
    {
        InitializeComponent();
        _original = settings;
        _openCodeClient = openCodeClient;

        // Connection
        VoicerPortTextBox.Text = settings.VoicerWsPort.ToString();
        OpenCodePortTextBox.Text = settings.OpenCodePort.ToString();
        AutoStartCheckBox.IsChecked = settings.AutoStartOpenCode;
        WorkDirTextBox.Text = settings.WorkDir;
        // WSL section — only available on Windows
        if (OperatingSystem.IsWindows())
        {
            UseWslCheckBox.IsChecked = settings.UseWsl;
            WslDistroTextBox.Text = settings.WslDistro;
            WslDistroGrid.IsVisible = settings.UseWsl;
        }
        else
        {
            WslSection.IsVisible = false;
        }

        // Notifications
        ShowPopupCheckBox.IsChecked = settings.ShowPopup;
        PopupDurationTextBox.Text = settings.PopupDurationSeconds.ToString("0.#", CultureInfo.InvariantCulture);
        PopupMaxLengthTextBox.Text = settings.PopupMaxLength.ToString();
        NewSessionTagTextBox.Text = settings.NewSessionTag;
        ContextTagTextBox.Text = settings.ContextTag;
        AutoStartCheckBox2.IsChecked = AutoStartService.IsEnabled();

        if (_openCodeClient != null)
        {
            _ = LoadProvidersAsync();
            _ = LoadAgentsAsync();
        }
        else
        {
            ModelStatusText.Text = "OpenCode не подключён";
            AgentDescText.Text = "OpenCode не подключён";
        }
    }

    private async Task LoadProvidersAsync()
    {
        try
        {
            _providers = await _openCodeClient!.GetProvidersAsync();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ProviderComboBox.Items.Clear();

                if (_providers.Count == 0)
                {
                    ModelStatusText.Text = "Не удалось загрузить провайдеров";
                    return;
                }

                foreach (var (id, name, _) in _providers)
                    ProviderComboBox.Items.Add($"{name} ({id})");

                var currentIdx = -1;
                if (!string.IsNullOrEmpty(_original.ProviderID))
                    currentIdx = _providers.FindIndex(p => p.Id == _original.ProviderID);

                if (currentIdx < 0)
                    _ = SelectCurrentFromConfigAsync();
                else
                    ProviderComboBox.SelectedIndex = currentIdx;

                ModelStatusText.Text = $"{_providers.Count} провайдеров загружено";
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                ModelStatusText.Text = $"Ошибка: {ex.Message}");
        }
    }

    private async Task LoadAgentsAsync()
    {
        try
        {
            _agents = await _openCodeClient!.GetAgentsAsync();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AgentComboBox.Items.Clear();

                if (_agents.Count == 0)
                {
                    AgentDescText.Text = "Не удалось загрузить агентов";
                    return;
                }

                foreach (var (name, _, mode) in _agents)
                {
                    var label = mode == "subagent" ? $"{name} (субагент)" : name;
                    AgentComboBox.Items.Add(label);
                }

                var currentIdx = _agents.FindIndex(a => a.Name == _original.AgentID);
                if (currentIdx < 0)
                    currentIdx = _agents.FindIndex(a => a.Name == "build");
                if (currentIdx >= 0)
                    AgentComboBox.SelectedIndex = currentIdx;

                AgentComboBox.SelectionChanged += Agent_SelectionChanged;
                UpdateAgentDescription();
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                AgentDescText.Text = $"Ошибка: {ex.Message}");
        }
    }

    private void Agent_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateAgentDescription();
    }

    private void UpdateAgentDescription()
    {
        if (AgentComboBox.SelectedIndex >= 0 && AgentComboBox.SelectedIndex < _agents.Count)
        {
            var desc = _agents[AgentComboBox.SelectedIndex].Description ?? "";
            AgentDescText.Text = desc.Length > 120 ? desc[..120] + "..." : desc;
        }
    }

    private async Task SelectCurrentFromConfigAsync()
    {
        var (pid, mid) = await _openCodeClient!.GetCurrentModelAsync();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!string.IsNullOrEmpty(pid))
            {
                var idx = _providers.FindIndex(p => p.Id == pid);
                if (idx >= 0)
                {
                    ProviderComboBox.SelectedIndex = idx;

                    if (!string.IsNullOrEmpty(mid))
                    {
                        var models = _providers[idx].Models;
                        var midx = models.IndexOf(mid);
                        if (midx >= 0)
                            ModelComboBox.SelectedIndex = midx;
                    }
                }
            }
        });
    }

    private void Provider_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ProviderComboBox.SelectedIndex < 0 || ProviderComboBox.SelectedIndex >= _providers.Count)
            return;

        var provider = _providers[ProviderComboBox.SelectedIndex];
        ModelComboBox.Items.Clear();

        foreach (var model in provider.Models)
            ModelComboBox.Items.Add(model);

        if (!string.IsNullOrEmpty(_original.ModelID))
        {
            var idx = provider.Models.IndexOf(_original.ModelID);
            if (idx >= 0)
                ModelComboBox.SelectedIndex = idx;
            else if (provider.Models.Count > 0)
                ModelComboBox.SelectedIndex = 0;
        }
        else if (provider.Models.Count > 0)
        {
            ModelComboBox.SelectedIndex = 0;
        }
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(VoicerPortTextBox.Text, out int voicerPort) || voicerPort < 1 || voicerPort > 65535)
        {
            ModelStatusText.Text = "WS порт Voicer должен быть от 1 до 65535";
            return;
        }

        if (!int.TryParse(OpenCodePortTextBox.Text, out int ocPort) || ocPort < 1 || ocPort > 65535)
        {
            ModelStatusText.Text = "Порт OpenCode должен быть от 1 до 65535";
            return;
        }

        // Popup duration
        if (!double.TryParse(PopupDurationTextBox.Text, NumberStyles.Any,
                CultureInfo.InvariantCulture, out double popupDuration)
            || popupDuration < 0.5 || popupDuration > 30)
        {
            ModelStatusText.Text = "Длительность уведомления от 0.5 до 30 сек";
            return;
        }

        if (!int.TryParse(PopupMaxLengthTextBox.Text, out int popupMaxLength) || popupMaxLength < 0)
        {
            ModelStatusText.Text = "Макс. символов должно быть 0 или больше";
            return;
        }

        // Provider/model
        string? providerID = null;
        string? modelID = null;
        if (ProviderComboBox.SelectedIndex >= 0 && ProviderComboBox.SelectedIndex < _providers.Count)
        {
            providerID = _providers[ProviderComboBox.SelectedIndex].Id;
            modelID = ModelComboBox.SelectedItem?.ToString();
        }

        // Agent
        string agentID = "build";
        if (AgentComboBox.SelectedIndex >= 0 && AgentComboBox.SelectedIndex < _agents.Count)
            agentID = _agents[AgentComboBox.SelectedIndex].Name;

        // New session tag
        var newSessionTag = NewSessionTagTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(newSessionTag))
            newSessionTag = "new-session";

        var contextTag = ContextTagTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(contextTag))
            contextTag = "context";

        // Autostart
        AutoStartService.SetEnabled(AutoStartCheckBox2.IsChecked == true);

        var newSettings = new OpenVoicerSettings
        {
            VoicerWsPort = voicerPort,
            OpenCodePort = ocPort,
            AutoStartOpenCode = AutoStartCheckBox.IsChecked == true,
            UseWsl = UseWslCheckBox.IsChecked == true,
            WorkDir = WorkDirTextBox.Text?.Trim() ?? "",
            WslDistro = WslDistroTextBox.Text?.Trim() ?? "",
            ProviderID = providerID,
            ModelID = modelID,
            AgentID = agentID,
            NewSessionTag = newSessionTag,
            ContextTag = contextTag,
            ShowPopup = ShowPopupCheckBox.IsChecked == true,
            PopupDurationSeconds = popupDuration,
            PopupMaxLength = popupMaxLength,
        };

        SettingsChanged?.Invoke(newSettings);
        Close();
    }

    private void Refresh_Click(object? sender, RoutedEventArgs e)
    {
        ModelStatusText.Text = "Загрузка...";
        AgentDescText.Text = "Загрузка...";
        ProviderComboBox.Items.Clear();
        ModelComboBox.Items.Clear();
        AgentComboBox.Items.Clear();
        _providers.Clear();
        _agents.Clear();

        if (_openCodeClient != null)
        {
            _ = LoadProvidersAsync();
            _ = LoadAgentsAsync();
        }
        else
        {
            ModelStatusText.Text = "OpenCode не подключён";
            AgentDescText.Text = "OpenCode не подключён";
        }
    }

    private void UseWsl_Changed(object? sender, RoutedEventArgs e)
    {
        var useWsl = UseWslCheckBox.IsChecked == true;
        WslDistroGrid.IsVisible = useWsl;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
