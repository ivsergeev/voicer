namespace Voicer.Core.Interfaces;

public interface ITrayIconGenerator
{
    /// <summary>
    /// Creates a PNG icon stream for the given state.
    /// </summary>
    /// <param name="iconType">One of: idle, recording_ws, recording_insert, processing_ws, processing_insert, no_model</param>
    Stream CreateIconStream(string iconType);
}
