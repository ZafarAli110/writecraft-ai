using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WriteCraftAI.Models;

namespace WriteCraftAI.Services;

public class InferenceService
{
    private readonly LlamaModelService _modelService;
    private readonly AppConfig _config;
    private readonly ILogger<InferenceService> _logger;

    public InferenceService(LlamaModelService modelService, AppConfig config,
        ILogger<InferenceService> logger)
    {
        _modelService = modelService;
        _config = config;
        _logger = logger;
    }

    public async IAsyncEnumerable<string> StreamInferenceAsync(
        string prompt,
        float temperature = 0.3f,
        int maxTokens = 512,
        string[]? stopSequences = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("=== INFERENCE START ===");

        // ✅ VERIFY THE RIGHT BACKEND IS LOADED
        var backendInfoPtr = NativeApi.llama_print_system_info();
        var backendInfo = Marshal.PtrToStringAnsi(backendInfoPtr) ?? "Unknown";

        _logger.LogWarning("Backend: {Backend}", backendInfo);

        // ✅ Now Contains() works on the string
        if (!backendInfo.Contains("AVX2 = 1"))
        {
            _logger.LogError("❌ PERFORMANCE WARNING: AVX2 NOT DETECTED - SLOW MODE");
        }

        if (!backendInfo.Contains("GGML_CPU = 1"))
        {
            _logger.LogError("❌ PERFORMANCE WARNING: CPU Backend NOT DETECTED");
        }
        _logger.LogInformation("Prompt: {Length} chars, MaxTokens: {Max}", prompt.Length, maxTokens);

        _modelService.Initialize();

        var stopWatch = Stopwatch.StartNew();
        int tokenCount = 0;
        bool firstToken = true;

        _logger.LogInformation("Creating StatelessExecutor...");
        var executorCreateStart = Stopwatch.StartNew();

        // StatelessExecutor - creates fresh context each time
        var executor = new StatelessExecutor(
            _modelService.GetModel(),
            _modelService.GetModelParams()
        );

        executorCreateStart.Stop();
        _logger.LogInformation("Executor created in {Ms:F0}ms", executorCreateStart.Elapsed.TotalMilliseconds);

        // Optimized sampling for speed
        var inferenceParams = new InferenceParams
        {
            MaxTokens = maxTokens,

            // Simple, fast sampling pipeline
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = temperature,
                TopK = 40,
                TopP = 0.9f,
                RepeatPenalty = 1.1f
                // Removed MinP for speed
            },

            AntiPrompts = stopSequences ?? GetSafeStopSequences()
        };

        _logger.LogDebug("Sampling: Temp={Temp}, TopK={K}, TopP={P}",
            temperature, 40, 0.9f);

        // Stream tokens
        await foreach (var token in executor.InferAsync(prompt, inferenceParams)
            .WithCancellation(cancellationToken))
        {
            tokenCount++;

            if (firstToken)
            {
                firstToken = false;
                var ttft = stopWatch.Elapsed.TotalMilliseconds;
                _logger.LogInformation("✅ First token: {Ms:F0}ms", ttft);
            }

            // Periodic progress logging
            if (tokenCount == 10 || tokenCount == 25 || tokenCount % 50 == 0)
            {
                var rate = tokenCount / stopWatch.Elapsed.TotalSeconds;
                _logger.LogDebug("{Count} tokens @ {Rate:F1} tok/s", tokenCount, rate);
            }

            yield return token;
        }

        var finalRate = tokenCount / stopWatch.Elapsed.TotalSeconds;
        _logger.LogInformation("✅ Done: {Count} tokens, {Time:F1}s, {Rate:F1} tok/s",
            tokenCount, stopWatch.Elapsed.TotalSeconds, finalRate);
    }

    private string[] GetSafeStopSequences() => new[]
    {
        "<|im_end|>",
        "<|endoftext|>",
        "\n\n\n"
    };
}