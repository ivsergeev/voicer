namespace Voicer.Core.Interfaces;

public interface IAudioCaptureService : IDisposable
{
    string? DeviceId { get; set; }
    bool IsRecording { get; }

    event Action? RecordingStarted;
    event Action? RecordingStopped;

    void StartRecording();
    void StopRecording();

    /// <summary>
    /// Returns recorded audio as mono float32 PCM at 16kHz.
    /// </summary>
    float[] GetRecordedSamples();

    /// <summary>
    /// Returns list of (id, displayName) for available capture devices.
    /// </summary>
    List<(string id, string name)> GetMicrophoneDevices();
}
