using Whisper.net;
using System.Diagnostics;

namespace WriteCraftAI.Services;

public sealed class WhisperService : IDisposable
{
    private readonly ILogger<WhisperService> _logger;
    private readonly WhisperFactory _factory;
    private WhisperProcessor? _processor;
    private readonly SemaphoreSlim _processorLock = new(1, 1);
    private bool _initialized;

    public bool IsReady => _initialized && _processor != null;

    public WhisperService(ILogger<WhisperService> logger)
    {
        _logger = logger;
        
        var modelPath = Path.Combine("AIModels", "ggml-small.bin");
        if (!File.Exists(modelPath))
        {
            _logger.LogWarning("Whisper model not found at {Path}", modelPath);
            throw new FileNotFoundException($"Download from: https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin", modelPath);
        }

        _logger.LogInformation("Loading Whisper model from {Path}...", modelPath);
        var sw = Stopwatch.StartNew();
        _factory = WhisperFactory.FromPath(modelPath);
        sw.Stop();
        _logger.LogInformation("✅ Whisper factory created in {Time:F1}s", sw.Elapsed.TotalSeconds);
    }

    public void Initialize()
    {
        if (_initialized) return;

        _processorLock.Wait();
        try
        {
            if (_initialized) return;

            _logger.LogInformation("🔥 Warming up Whisper processor...");
            var sw = Stopwatch.StartNew();
            
            _processor = _factory.CreateBuilder()
                .WithLanguage("en")
                .WithThreads(Environment.ProcessorCount)
                .Build();
            
            sw.Stop();
            _initialized = true;
            _logger.LogInformation("✅ Whisper processor ready in {Time:F1}s", sw.Elapsed.TotalSeconds);
        }
        finally
        {
            _processorLock.Release();
        }
    }

    public async Task<(string Text, double TranscriptionTimeMs)> TranscribeAsync(Stream audioStream, CancellationToken cancellationToken = default)
    {
        if (!_initialized || _processor == null)
        {
            Initialize();
        }

        await _processorLock.WaitAsync(cancellationToken);
        try
        {
            var sw = Stopwatch.StartNew();
            var segments = new List<string>();

            await foreach (var segment in _processor!.ProcessAsync(audioStream, cancellationToken))
            {
                segments.Add(segment.Text);
            }

            sw.Stop();
            var text = string.Join(" ", segments).Trim();
            
            _logger.LogInformation("Transcribed {Chars} chars in {Time:F0}ms", text.Length, sw.Elapsed.TotalMilliseconds);
            
            return (text, sw.Elapsed.TotalMilliseconds);
        }
        finally
        {
            _processorLock.Release();
        }
    }

    public string GetFfmpegPath()
    {
        var localPath = Path.Combine(Directory.GetCurrentDirectory(), "External", "ffmpeg.exe");
        return File.Exists(localPath) ? localPath : "ffmpeg";
    }

    public async Task<(bool Success, string OutputPath, double ConversionTimeMs, string? Error)> ConvertToWavAsync(
        string inputPath, CancellationToken cancellationToken = default)
    {
        var outputPath = Path.GetTempFileName() + ".wav";
        var ffmpegPath = GetFfmpegPath();

        if (string.IsNullOrEmpty(ffmpegPath) || (!File.Exists(ffmpegPath) && ffmpegPath != "ffmpeg"))
        {
            return (false, outputPath, 0, "FFmpeg not found. Place ffmpeg.exe in External folder.");
        }

        var sw = Stopwatch.StartNew();

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-i \"{inputPath}\" -acodec pcm_s16le -ar 16000 -ac 1 -f wav \"{outputPath}\" -y -threads 0 -loglevel error",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        sw.Stop();

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            return (false, outputPath, sw.Elapsed.TotalMilliseconds, $"FFmpeg failed: {error}");
        }

        _logger.LogInformation("Converted audio in {Time:F0}ms", sw.Elapsed.TotalMilliseconds);
        return (true, outputPath, sw.Elapsed.TotalMilliseconds, null);
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _factory?.Dispose();
        _processorLock.Dispose();
        _logger.LogInformation("Whisper resources disposed");
    }
}
