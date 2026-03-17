namespace Voicer.Core.Interfaces;

public interface IHotkeyService : IDisposable
{
    /// <summary>
    /// Whether the hotkey service is functional (e.g. false on Wayland without X11).
    /// </summary>
    bool IsAvailable { get; }

    event Action? KeyPressed;
    event Action? KeyReleased;
    event Action? InsertKeyPressed;
    event Action? InsertKeyReleased;
    event Action? SelectionKeyPressed;
    event Action? SelectionKeyReleased;

    void Start(int primaryModifiers, int primaryKeyCode,
        int insertModifiers, int insertKeyCode,
        int selectionModifiers, int selectionKeyCode);
    void Stop();
    void UpdateHotkey(int modifiers, int keyCode);
    void UpdateInsertHotkey(int modifiers, int keyCode);
    void UpdateSelectionHotkey(int modifiers, int keyCode);
}
