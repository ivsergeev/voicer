using System.Runtime.InteropServices;
using Voicer.Core.Interfaces;

namespace Voicer.Platform.Linux;

/// <summary>
/// Linux audio capture via PulseAudio simple API (libpulse-simple.so.0).
/// Records 16kHz mono float32 PCM — the format sherpa-onnx expects.
/// </summary>
public class LinuxAudioCaptureService : IAudioCaptureService
{
    private const string PulseSimple = "libpulse-simple.so.0";

    private const int PA_STREAM_RECORD = 2;
    private const int PA_SAMPLE_FLOAT32LE = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct pa_sample_spec
    {
        public int format;
        public uint rate;
        public byte channels;
    }

    [DllImport(PulseSimple)]
    private static extern IntPtr pa_simple_new(
        string? server, string name, int dir, string? dev,
        string stream_name, ref pa_sample_spec ss,
        IntPtr channel_map, IntPtr attr, out int error);

    [DllImport(PulseSimple)]
    private static extern int pa_simple_read(IntPtr s, byte[] data, nuint bytes, out int error);

    [DllImport(PulseSimple)]
    private static extern void pa_simple_free(IntPtr s);

    [DllImport("libpulse.so.0")]
    private static extern IntPtr pa_strerror(int error);

    private IntPtr _paHandle;
    private MemoryStream? _buffer;
    private readonly object _lock = new();
    private volatile bool _isRecording;
    private Thread? _recordThread;

    public string? DeviceId { get; set; }
    public bool IsRecording => _isRecording;

    public event Action? RecordingStarted;
    public event Action? RecordingStopped;

    public void StartRecording()
    {
        if (_isRecording) return;

        _buffer = new MemoryStream();

        var spec = new pa_sample_spec
        {
            format = PA_SAMPLE_FLOAT32LE,
            rate = 16000,
            channels = 1
        };

        string? device = string.IsNullOrEmpty(DeviceId) ? null : DeviceId;

        _paHandle = pa_simple_new(null, "Voicer", PA_STREAM_RECORD, device,
            "Speech Recording", ref spec, IntPtr.Zero, IntPtr.Zero, out int error);

        if (_paHandle == IntPtr.Zero)
        {
            var errMsg = Marshal.PtrToStringAnsi(pa_strerror(error)) ?? $"error {error}";
            Console.WriteLine($"[Linux] Failed to open PulseAudio: {errMsg}");
            return;
        }

        _isRecording = true;
        RecordingStarted?.Invoke();

        _recordThread = new Thread(RecordLoop) { IsBackground = true, Name = "PulseAudioCapture" };
        _recordThread.Start();
    }

    private void RecordLoop()
    {
        // Read 100ms chunks: 16000 samples/sec * 4 bytes * 0.1 sec = 6400 bytes
        var chunk = new byte[6400];

        while (_isRecording)
        {
            int result = pa_simple_read(_paHandle, chunk, (nuint)chunk.Length, out int error);
            if (result < 0)
            {
                var errMsg = Marshal.PtrToStringAnsi(pa_strerror(error)) ?? $"error {error}";
                Console.WriteLine($"[Linux] PulseAudio read error: {errMsg}");
                break;
            }

            lock (_lock)
            {
                _buffer?.Write(chunk, 0, chunk.Length);
            }
        }
    }

    public void StopRecording()
    {
        if (!_isRecording) return;
        _isRecording = false;

        _recordThread?.Join(2000);

        if (_paHandle != IntPtr.Zero)
        {
            pa_simple_free(_paHandle);
            _paHandle = IntPtr.Zero;
        }

        RecordingStopped?.Invoke();
    }

    public float[] GetRecordedSamples()
    {
        lock (_lock)
        {
            if (_buffer == null || _buffer.Length == 0)
                return [];

            var bytes = _buffer.ToArray();
            int sampleCount = bytes.Length / 4;
            var samples = new float[sampleCount];
            Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);

            Normalize(samples);
            return samples;
        }
    }

    public List<(string id, string name)> GetMicrophoneDevices()
    {
        var devices = new List<(string, string)> { ("default", "Default Microphone") };

        // Try to list sources via pactl
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("pactl", "list short sources")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(2000);

                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && !parts[1].Contains("monitor"))
                    {
                        devices.Add((parts[1], parts[1]));
                    }
                }
            }
        }
        catch { /* pactl not available */ }

        return devices;
    }

    private static void Normalize(float[] samples, float targetPeak = 0.9f)
    {
        float max = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float abs = Math.Abs(samples[i]);
            if (abs > max) max = abs;
        }
        if (max < 1e-6f) return;
        float gain = targetPeak / max;
        for (int i = 0; i < samples.Length; i++)
            samples[i] *= gain;
    }

    public void Dispose()
    {
        StopRecording();
        _buffer?.Dispose();
    }
}
