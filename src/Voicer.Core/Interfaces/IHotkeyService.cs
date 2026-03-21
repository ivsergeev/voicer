using Voicer.Core.Models;

namespace Voicer.Core.Interfaces;

public interface IHotkeyService : IDisposable
{
    /// <summary>
    /// Whether the hotkey service is functional (e.g. false on Wayland without X11).
    /// </summary>
    bool IsAvailable { get; }

    // Insert hotkey (F6) — unchanged
    event Action? InsertKeyPressed;
    event Action? InsertKeyReleased;

    // Dynamic WS hotkeys — index corresponds to position in WsHotkeyActions list
    event Action<int>? WsHotkeyPressed;
    event Action<int>? WsHotkeyReleased;

    void Start(int insertModifiers, int insertKeyCode, List<HotkeyAction> wsActions);
    void Stop();
    void UpdateInsertHotkey(int modifiers, int keyCode);
    void UpdateWsHotkeys(List<HotkeyAction> wsActions);
}
