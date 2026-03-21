using System.IO;
using Serilog;
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

        Log.Information("SpeechService model: {ModelPath} ({SizeMB} MB)", modelPath, new FileInfo(modelPath).Length / 1024 / 1024);
        Log.Information("SpeechService tokens: {TokensPath}", tokensPath);
        Log.Information("SpeechService threads: {NumThreads}", numThreads);

        var config = new OfflineRecognizerConfig();

        config.ModelConfig.NeMoCtc.Model = modelPath;
        config.ModelConfig.Tokens = tokensPath;
        config.ModelConfig.NumThreads = numThreads;
        config.ModelConfig.Debug = 0;
        config.ModelConfig.Provider = "cpu";

        config.DecodingMethod = "greedy_search";

        config.FeatConfig.SampleRate = 16000;
        config.FeatConfig.FeatureDim = 64;  // GigaAM expects 64-dim features

        Log.Debug("SpeechService config created, calling OfflineRecognizer constructor");

        _recognizer = new OfflineRecognizer(config);

        Log.Debug("SpeechService OfflineRecognizer created successfully");
        _initialized = true;

        // Warmup: run a short silence through the model to pre-load weights.
        // Non-fatal — if warmup fails, real recognition may still work.
        try
        {
            Log.Information("SpeechService running warmup (3s silence)");
            Recognize(new float[48000]); // 3 seconds at 16kHz
            Log.Information("SpeechService warmup complete");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SpeechService warmup failed (non-fatal)");
        }
    }

    public string Recognize(float[] samples)
    {
        if (_recognizer == null)
            throw new InvalidOperationException("SpeechRecognitionService is not initialized.");

        if (samples.Length == 0)
            return string.Empty;

        try
        {
            using var stream = _recognizer.CreateStream();
            stream.AcceptWaveform(16000, samples);
            _recognizer.Decode(stream);
            var result = stream.Result;

            return result.Text.Trim();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Speech recognition failed");
            return string.Empty;
        }
    }

    public void Dispose()
    {
        _recognizer?.Dispose();
        _recognizer = null;
        _initialized = false;
    }
}
