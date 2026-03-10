namespace Voicer.Core.Interfaces;

public interface ITextInsertionService
{
    /// <summary>
    /// Gets the current clipboard text content (null if clipboard is empty or contains non-text).
    /// </summary>
    Task<string?> GetClipboardText();

    /// <summary>
    /// Sets the clipboard text content.
    /// </summary>
    Task SetClipboardText(string text);

    /// <summary>
    /// Simulates paste (Ctrl+V / Cmd+V) at the current cursor in the foreground app.
    /// Caller is responsible for setting clipboard content first.
    /// </summary>
    Task SimulatePaste();
}
