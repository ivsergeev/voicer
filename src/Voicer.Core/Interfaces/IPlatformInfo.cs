namespace Voicer.Core.Interfaces;

public interface IPlatformInfo
{
    /// <summary>
    /// Converts a stored VK code to a human-readable key name for display.
    /// </summary>
    string GetKeyName(int vkCode);

    /// <summary>
    /// Converts a platform key event to a VK code for storage.
    /// </summary>
    int KeyToVkCode(object key);

    /// <summary>
    /// Converts a hotkey combination (modifiers + key) to a display string like "Ctrl+Shift+F6".
    /// Modifier flags: MOD_ALT=0x0001, MOD_CONTROL=0x0002, MOD_SHIFT=0x0004, MOD_WIN=0x0008.
    /// </summary>
    string GetHotkeyDisplayName(int modifiers, int vkCode);
}
