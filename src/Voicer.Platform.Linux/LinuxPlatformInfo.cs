using Avalonia.Input;
using Voicer.Core.Interfaces;

namespace Voicer.Platform.Linux;

public class LinuxPlatformInfo : IPlatformInfo
{
    // Avalonia Key enum to Windows VK code (canonical format stored in settings)
    private static readonly Dictionary<Key, int> KeyToVkMap = new()
    {
        { Key.F1, 0x70 }, { Key.F2, 0x71 }, { Key.F3, 0x72 }, { Key.F4, 0x73 },
        { Key.F5, 0x74 }, { Key.F6, 0x75 }, { Key.F7, 0x76 }, { Key.F8, 0x77 },
        { Key.F9, 0x78 }, { Key.F10, 0x79 }, { Key.F11, 0x7A }, { Key.F12, 0x7B },
        { Key.A, 0x41 }, { Key.B, 0x42 }, { Key.C, 0x43 }, { Key.D, 0x44 },
        { Key.E, 0x45 }, { Key.F, 0x46 }, { Key.G, 0x47 }, { Key.H, 0x48 },
        { Key.I, 0x49 }, { Key.J, 0x4A }, { Key.K, 0x4B }, { Key.L, 0x4C },
        { Key.M, 0x4D }, { Key.N, 0x4E }, { Key.O, 0x4F }, { Key.P, 0x50 },
        { Key.Q, 0x51 }, { Key.R, 0x52 }, { Key.S, 0x53 }, { Key.T, 0x54 },
        { Key.U, 0x55 }, { Key.V, 0x56 }, { Key.W, 0x57 }, { Key.X, 0x58 },
        { Key.Y, 0x59 }, { Key.Z, 0x5A },
        { Key.D0, 0x30 }, { Key.D1, 0x31 }, { Key.D2, 0x32 }, { Key.D3, 0x33 },
        { Key.D4, 0x34 }, { Key.D5, 0x35 }, { Key.D6, 0x36 }, { Key.D7, 0x37 },
        { Key.D8, 0x38 }, { Key.D9, 0x39 },
        { Key.Space, 0x20 }, { Key.Enter, 0x0D }, { Key.Escape, 0x1B },
        { Key.Tab, 0x09 }, { Key.Back, 0x08 }, { Key.Delete, 0x2E },
        { Key.Insert, 0x2D }, { Key.Home, 0x24 }, { Key.End, 0x23 },
        { Key.PageUp, 0x21 }, { Key.PageDown, 0x22 },
        { Key.Left, 0x25 }, { Key.Up, 0x26 }, { Key.Right, 0x27 }, { Key.Down, 0x28 },
        { Key.Pause, 0x13 }, { Key.CapsLock, 0x14 }, { Key.Scroll, 0x91 },
        { Key.PrintScreen, 0x2C }, { Key.NumLock, 0x90 },
    };

    private static readonly Dictionary<int, string> VkToNameMap = new()
    {
        { 0x70, "F1" }, { 0x71, "F2" }, { 0x72, "F3" }, { 0x73, "F4" },
        { 0x74, "F5" }, { 0x75, "F6" }, { 0x76, "F7" }, { 0x77, "F8" },
        { 0x78, "F9" }, { 0x79, "F10" }, { 0x7A, "F11" }, { 0x7B, "F12" },
        { 0x41, "A" }, { 0x42, "B" }, { 0x43, "C" }, { 0x44, "D" },
        { 0x45, "E" }, { 0x46, "F" }, { 0x47, "G" }, { 0x48, "H" },
        { 0x49, "I" }, { 0x4A, "J" }, { 0x4B, "K" }, { 0x4C, "L" },
        { 0x4D, "M" }, { 0x4E, "N" }, { 0x4F, "O" }, { 0x50, "P" },
        { 0x51, "Q" }, { 0x52, "R" }, { 0x53, "S" }, { 0x54, "T" },
        { 0x55, "U" }, { 0x56, "V" }, { 0x57, "W" }, { 0x58, "X" },
        { 0x59, "Y" }, { 0x5A, "Z" },
        { 0x30, "0" }, { 0x31, "1" }, { 0x32, "2" }, { 0x33, "3" },
        { 0x34, "4" }, { 0x35, "5" }, { 0x36, "6" }, { 0x37, "7" },
        { 0x38, "8" }, { 0x39, "9" },
        { 0x20, "Space" }, { 0x0D, "Return" }, { 0x1B, "Escape" },
        { 0x09, "Tab" }, { 0x08, "BackSpace" }, { 0x2E, "Delete" },
        { 0x2D, "Insert" }, { 0x24, "Home" }, { 0x23, "End" },
        { 0x21, "PageUp" }, { 0x22, "PageDown" },
        { 0x25, "Left" }, { 0x26, "Up" }, { 0x27, "Right" }, { 0x28, "Down" },
        { 0x13, "Pause" }, { 0x14, "CapsLock" }, { 0x91, "ScrollLock" },
        { 0x2C, "PrintScreen" }, { 0x90, "NumLock" },
    };

    /// <summary>
    /// Maps VK code to X11 keysym for XGrabKey.
    /// </summary>
    internal static readonly Dictionary<int, uint> VkToX11KeySym = new()
    {
        { 0x70, 0xFFBE }, { 0x71, 0xFFBF }, { 0x72, 0xFFC0 }, { 0x73, 0xFFC1 }, // F1-F4
        { 0x74, 0xFFC2 }, { 0x75, 0xFFC3 }, { 0x76, 0xFFC4 }, { 0x77, 0xFFC5 }, // F5-F8
        { 0x78, 0xFFC6 }, { 0x79, 0xFFC7 }, { 0x7A, 0xFFC8 }, { 0x7B, 0xFFC9 }, // F9-F12
        { 0x41, 0x61 }, { 0x42, 0x62 }, { 0x43, 0x63 }, { 0x44, 0x64 }, // a-d
        { 0x45, 0x65 }, { 0x46, 0x66 }, { 0x47, 0x67 }, { 0x48, 0x68 }, // e-h
        { 0x49, 0x69 }, { 0x4A, 0x6A }, { 0x4B, 0x6B }, { 0x4C, 0x6C }, // i-l
        { 0x4D, 0x6D }, { 0x4E, 0x6E }, { 0x4F, 0x6F }, { 0x50, 0x70 }, // m-p
        { 0x51, 0x71 }, { 0x52, 0x72 }, { 0x53, 0x73 }, { 0x54, 0x74 }, // q-t
        { 0x55, 0x75 }, { 0x56, 0x76 }, { 0x57, 0x77 }, { 0x58, 0x78 }, // u-x
        { 0x59, 0x79 }, { 0x5A, 0x7A }, // y-z
        { 0x20, 0x20 },     // Space
        { 0x0D, 0xFF0D },   // Return
        { 0x1B, 0xFF1B },   // Escape
        { 0x09, 0xFF09 },   // Tab
        { 0x08, 0xFF08 },   // BackSpace
        { 0x2E, 0xFFFF },   // Delete
        { 0x2D, 0xFF63 },   // Insert
        { 0x24, 0xFF50 },   // Home
        { 0x23, 0xFF57 },   // End
        { 0x21, 0xFF55 },   // Page_Up
        { 0x22, 0xFF56 },   // Page_Down
        { 0x25, 0xFF51 },   // Left
        { 0x26, 0xFF52 },   // Up
        { 0x27, 0xFF53 },   // Right
        { 0x28, 0xFF54 },   // Down
        { 0x13, 0xFF13 },   // Pause
        { 0x14, 0xFFE5 },   // Caps_Lock
        { 0x91, 0xFF14 },   // Scroll_Lock
        { 0x90, 0xFF7F },   // Num_Lock
    };

    public string GetKeyName(int vkCode)
    {
        return VkToNameMap.TryGetValue(vkCode, out var name) ? name : $"Key 0x{vkCode:X2}";
    }

    public int KeyToVkCode(object key)
    {
        if (key is Key avaloniaKey && KeyToVkMap.TryGetValue(avaloniaKey, out var vk))
            return vk;
        return 0;
    }

    public string GetHotkeyDisplayName(int modifiers, int vkCode)
    {
        var parts = new List<string>();
        if ((modifiers & 0x0002) != 0) parts.Add("Ctrl");
        if ((modifiers & 0x0001) != 0) parts.Add("Alt");
        if ((modifiers & 0x0004) != 0) parts.Add("Shift");
        if ((modifiers & 0x0008) != 0) parts.Add("Super");
        if (vkCode != 0) parts.Add(GetKeyName(vkCode));
        return parts.Count > 0 ? string.Join("+", parts) : "";
    }
}
