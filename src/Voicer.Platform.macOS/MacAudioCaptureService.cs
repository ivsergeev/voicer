using System.Runtime.InteropServices;
using Voicer.Core.Interfaces;

namespace Voicer.Platform.macOS;

/// <summary>
/// macOS audio capture via CoreAudio AudioQueue API.
/// Records 16kHz mono float32 PCM — the format sherpa-onnx expects.
/// Requires microphone permission: System Settings → Privacy → Microphone.
/// </summary>
public class MacAudioCaptureService : IAudioCaptureService
{
    private const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const uint kAudioFormatLinearPCM = 0x6C70636D; // 'lpcm'
    private const uint kAudioFormatFlagIsFloat = 1;
    private const uint kAudioFormatFlagIsPacked = 8;
    private const int kNumberBuffers = 3;
    private const int kSampleRate = 16000;
    private const int kBufferSize = kSampleRate * 4 / 10; // 6400 bytes = 100ms

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioStreamBasicDescription
    {
        public double mSampleRate;
        public uint mFormatID;
        public uint mFormatFlags;
        public uint mBytesPerPacket;
        public uint mFramesPerPacket;
        public uint mBytesPerFrame;
        public uint mChannelsPerFrame;
        public uint mBitsPerChannel;
        public uint mReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioQueueBuffer
    {
        public uint mAudioDataBytesCapacity;
        public IntPtr mAudioData;
        public uint mAudioDataByteSize;
        public IntPtr mUserData;
        public uint mPacketDescriptionCapacity;
        public IntPtr mPacketDescriptions;
        public uint mPacketDescriptionCount;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AudioQueueInputCallback(
        IntPtr inUserData, IntPtr inAQ, IntPtr inBuffer,
        IntPtr inStartTime, uint inNumberPacketDescriptions, IntPtr inPacketDescs);

    [DllImport(AudioToolbox)]
    private static extern int AudioQueueNewInput(
        ref AudioStreamBasicDescription inFormat,
        AudioQueueInputCallback inCallbackProc,
        IntPtr inUserData, IntPtr inCallbackRunLoop,
        IntPtr inCallbackRunLoopMode, uint inFlags,
        out IntPtr outAQ);

    [DllImport(AudioToolbox)]
    private static extern int AudioQueueAllocateBuffer(IntPtr inAQ, uint inBufferByteSize, out IntPtr outBuffer);

    [DllImport(AudioToolbox)]
    private static extern int AudioQueueEnqueueBuffer(IntPtr inAQ, IntPtr inBuffer, uint inNumPacketDescs, IntPtr inPacketDescs);

    [DllImport(AudioToolbox)]
    private static extern int AudioQueueStart(IntPtr inAQ, IntPtr inStartTime);

    [DllImport(AudioToolbox)]
    private static extern int AudioQueueStop(IntPtr inAQ, int inImmediate);

    [DllImport(AudioToolbox)]
    private static extern int AudioQueueDispose(IntPtr inAQ, int inImmediate);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFRunLoopGetCurrent();

    [DllImport(CoreFoundation)]
    private static extern void CFRunLoopRunInMode(IntPtr mode, double seconds, int returnAfterSourceHandled);

    [DllImport(CoreFoundation)]
    private static extern void CFRunLoopStop(IntPtr rl);

    private IntPtr _audioQueue;
    private readonly IntPtr[] _buffers = new IntPtr[kNumberBuffers];
    private MemoryStream? _buffer;
    private readonly object _lock = new();
    private volatile bool _isRecording;
    private Thread? _recordThread;
    private AudioQueueInputCallback? _callbackDelegate;
    private IntPtr _runLoop;

    public string? DeviceId { get; set; }
    public bool IsRecording => _isRecording;

    public event Action? RecordingStarted;
    public event Action? RecordingStopped;

    public void StartRecording()
    {
        if (_isRecording) return;
        _buffer = new MemoryStream();
        _recordThread = new Thread(AudioQueueThread) { IsBackground = true, Name = "CoreAudioCapture" };
        _recordThread.Start();
    }

    private void AudioQueueThread()
    {
        var format = new AudioStreamBasicDescription
        {
            mSampleRate = kSampleRate,
            mFormatID = kAudioFormatLinearPCM,
            mFormatFlags = kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked,
            mBytesPerPacket = 4,
            mFramesPerPacket = 1,
            mBytesPerFrame = 4,
            mChannelsPerFrame = 1,
            mBitsPerChannel = 32,
        };

        _callbackDelegate = AudioInputCallback;

        int status = AudioQueueNewInput(ref format, _callbackDelegate, IntPtr.Zero,
            IntPtr.Zero, IntPtr.Zero, 0, out _audioQueue);

        if (status != 0)
        {
            Console.WriteLine($"[macOS] AudioQueueNewInput failed: {status}");
            Console.WriteLine("  Ensure microphone access is granted in System Settings → Privacy → Microphone.");
            return;
        }

        for (int i = 0; i < kNumberBuffers; i++)
        {
            AudioQueueAllocateBuffer(_audioQueue, kBufferSize, out _buffers[i]);
            AudioQueueEnqueueBuffer(_audioQueue, _buffers[i], 0, IntPtr.Zero);
        }

        status = AudioQueueStart(_audioQueue, IntPtr.Zero);
        if (status != 0)
        {
            Console.WriteLine($"[macOS] AudioQueueStart failed: {status}");
            AudioQueueDispose(_audioQueue, 1);
            return;
        }

        _isRecording = true;
        RecordingStarted?.Invoke();

        _runLoop = CFRunLoopGetCurrent();
        while (_isRecording)
        {
            CFRunLoopRunInMode(CoreFoundationInterop.DefaultMode, 0.1, 0);
        }
    }

    private void AudioInputCallback(IntPtr inUserData, IntPtr inAQ, IntPtr inBuffer,
        IntPtr inStartTime, uint inNumberPacketDescriptions, IntPtr inPacketDescs)
    {
        var aqBuffer = Marshal.PtrToStructure<AudioQueueBuffer>(inBuffer);

        if (aqBuffer.mAudioDataByteSize > 0)
        {
            var data = new byte[aqBuffer.mAudioDataByteSize];
            Marshal.Copy(aqBuffer.mAudioData, data, 0, (int)aqBuffer.mAudioDataByteSize);

            lock (_lock)
            {
                _buffer?.Write(data, 0, data.Length);
            }
        }

        if (_isRecording)
            AudioQueueEnqueueBuffer(inAQ, inBuffer, 0, IntPtr.Zero);
    }

    public void StopRecording()
    {
        if (!_isRecording) return;
        _isRecording = false;

        if (_audioQueue != IntPtr.Zero)
        {
            AudioQueueStop(_audioQueue, 1);
            AudioQueueDispose(_audioQueue, 1);
            _audioQueue = IntPtr.Zero;
        }

        if (_runLoop != IntPtr.Zero)
        {
            CFRunLoopStop(_runLoop);
            _runLoop = IntPtr.Zero;
        }

        _recordThread?.Join(2000);
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
        return [("default", "Default Microphone")];
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

/// <summary>
/// Helper to access kCFRunLoopDefaultMode string constant.
/// </summary>
internal static class CoreFoundationInterop
{
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, int encoding);

    private static IntPtr? _defaultMode;

    internal static IntPtr DefaultMode
    {
        get
        {
            _defaultMode ??= CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopDefaultMode", 0x08000100);
            return _defaultMode.Value;
        }
    }
}
