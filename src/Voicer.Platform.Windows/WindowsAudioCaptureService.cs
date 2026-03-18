using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Voicer.Core.Interfaces;

namespace Voicer.Platform.Windows;

public class WindowsAudioCaptureService : IAudioCaptureService
{
    private WasapiCapture? _capture;
    private WaveFormat? _captureFormat;
    private MemoryStream? _buffer;
    private readonly object _lock = new();
    private bool _isRecording;

    public string? DeviceId { get; set; }
    public bool NormalizeAudio { get; set; } = true;

    public bool IsRecording => _isRecording;

    public event Action? RecordingStarted;
    public event Action? RecordingStopped;

    public void StartRecording()
    {
        if (_isRecording) return;

        _buffer = new MemoryStream();

        var device = GetSelectedDevice();
        _capture = new WasapiCapture(device, false, 100);
        _captureFormat = _capture.WaveFormat;

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;

        _capture.StartRecording();
        _isRecording = true;
        RecordingStarted?.Invoke();
    }

    public void StopRecording()
    {
        if (!_isRecording) return;
        _isRecording = false;
        _capture?.StopRecording();
    }

    public float[] GetRecordedSamples()
    {
        lock (_lock)
        {
            if (_buffer == null || _buffer.Length == 0 || _captureFormat == null)
                return [];

            var bytes = _buffer.ToArray();
            var rawSamples = ConvertToFloat(bytes, _captureFormat);

            if (_captureFormat.Channels > 1)
                rawSamples = MixToMono(rawSamples, _captureFormat.Channels);

            if (_captureFormat.SampleRate != 16000)
                rawSamples = Resample(rawSamples, _captureFormat.SampleRate, 16000);

            if (NormalizeAudio)
                Normalize(rawSamples);

            return rawSamples;
        }
    }

    public List<(string id, string name)> GetMicrophoneDevices()
    {
        var devices = new List<(string, string)>();
        var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            devices.Add((device.ID, device.FriendlyName));
        }
        return devices;
    }

    private static float[] ConvertToFloat(byte[] bytes, WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            int sampleCount = bytes.Length / 4;
            var samples = new float[sampleCount];
            Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
            return samples;
        }

        if (format.BitsPerSample == 16)
        {
            int sampleCount = bytes.Length / 2;
            var samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = BitConverter.ToInt16(bytes, i * 2);
                samples[i] = sample / 32768f;
            }
            return samples;
        }

        if (format.BitsPerSample == 24)
        {
            int sampleCount = bytes.Length / 3;
            var samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                int val = bytes[i * 3] | (bytes[i * 3 + 1] << 8) | (bytes[i * 3 + 2] << 16);
                if ((val & 0x800000) != 0) val |= unchecked((int)0xFF000000);
                samples[i] = val / 8388608f;
            }
            return samples;
        }

        throw new NotSupportedException($"Unsupported audio format: {format.BitsPerSample}bit {format.Encoding}");
    }

    private static float[] MixToMono(float[] samples, int channels)
    {
        int monoLength = samples.Length / channels;
        var mono = new float[monoLength];
        for (int i = 0; i < monoLength; i++)
        {
            float sum = 0;
            for (int ch = 0; ch < channels; ch++)
                sum += samples[i * channels + ch];
            mono[i] = sum / channels;
        }
        return mono;
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

    private static float[] Resample(float[] source, int sourceRate, int targetRate)
    {
        double ratio = (double)sourceRate / targetRate;
        int targetLength = (int)(source.Length / ratio);
        var result = new float[targetLength];

        for (int i = 0; i < targetLength; i++)
        {
            double srcPos = i * ratio;
            int srcIndex = (int)srcPos;
            double frac = srcPos - srcIndex;

            if (srcIndex + 1 < source.Length)
                result[i] = (float)(source[srcIndex] * (1 - frac) + source[srcIndex + 1] * frac);
            else if (srcIndex < source.Length)
                result[i] = source[srcIndex];
        }

        return result;
    }

    private MMDevice GetSelectedDevice()
    {
        var enumerator = new MMDeviceEnumerator();

        if (!string.IsNullOrEmpty(DeviceId))
        {
            try
            {
                return enumerator.GetDevice(DeviceId);
            }
            catch
            {
                // Fall through to default
            }
        }

        try
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            throw new InvalidOperationException(
                "No microphone found. Connect a microphone and try again.");
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_lock)
        {
            _buffer?.Write(e.Buffer, 0, e.BytesRecorded);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        RecordingStopped?.Invoke();
    }

    public void Dispose()
    {
        _capture?.Dispose();
        _buffer?.Dispose();
    }
}
