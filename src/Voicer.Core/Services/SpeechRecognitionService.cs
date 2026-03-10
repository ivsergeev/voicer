using System.IO;
using SherpaOnnx;

namespace Voicer.Core.Services;

public class SpeechRecognitionService : IDisposable
{
    private OfflineRecognizer? _recognizer;
    private bool _initialized;

    public bool IsInitialized => _initialized;

    public void Initialize(string modelPath, string tokensPath, int numThreads = 4)
    {
        if (_initialized)
            return;

        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Model file not found: {modelPath}");
        if (!File.Exists(tokensPath))
            throw new FileNotFoundException($"Tokens file not found: {tokensPath}");

        Console.WriteLine($"  [SpeechService] Model: {modelPath} ({new FileInfo(modelPath).Length / 1024 / 1024} MB)");
        Console.WriteLine($"  [SpeechService] Tokens: {tokensPath}");
        Console.WriteLine($"  [SpeechService] Threads: {numThreads}");

        var config = new OfflineRecognizerConfig();

        config.ModelConfig.NeMoCtc.Model = modelPath;
        config.ModelConfig.Tokens = tokensPath;
        config.ModelConfig.NumThreads = numThreads;
        config.ModelConfig.Debug = 0;
        config.ModelConfig.Provider = "cpu";

        config.DecodingMethod = "greedy_search";

        config.FeatConfig.SampleRate = 16000;
        config.FeatConfig.FeatureDim = 64;  // GigaAM expects 64-dim features

        Console.WriteLine("  [SpeechService] Config created, calling OfflineRecognizer constructor...");

        _recognizer = new OfflineRecognizer(config);

        Console.WriteLine("  [SpeechService] OfflineRecognizer created successfully.");
        _initialized = true;

        // Warmup: run a short silence through the model to pre-load weights.
        // Non-fatal — if warmup fails, real recognition may still work.
        try
        {
            Console.WriteLine("  [SpeechService] Running warmup (3s silence)...");
            Recognize(new float[48000]); // 3 seconds at 16kHz
            Console.WriteLine("  [SpeechService] Warmup complete.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [SpeechService] Warmup failed (non-fatal): {ex.Message}");
        }
    }

    public string Recognize(float[] samples)
    {
        if (_recognizer == null)
            throw new InvalidOperationException("SpeechRecognitionService is not initialized.");

        if (samples.Length == 0)
            return string.Empty;

        using var stream = _recognizer.CreateStream();
        stream.AcceptWaveform(16000, samples);
        _recognizer.Decode(stream);
        var result = stream.Result;

        return result.Text.Trim();
    }

    public void Dispose()
    {
        _recognizer?.Dispose();
        _recognizer = null;
        _initialized = false;
    }
}
