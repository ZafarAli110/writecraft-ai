using LLama;
using LLama.Common;
using LLama.Native;
using WriteCraftAI.Models;

namespace WriteCraftAI.Services;

public sealed class LlamaModelService : IDisposable
{
    private readonly AppConfig _config;
    private readonly ILogger<LlamaModelService> _logger;
    private LLamaWeights? _model;
    private ModelParams? _modelParams;
    private readonly object _initLock = new();
    private bool _initialized;

    public bool IsHealthy => _initialized && _model != null;

    public LlamaModelService(AppConfig config, ILogger<LlamaModelService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public void Initialize()
    {
        if (_initialized) return;

        lock (_initLock)
        {
            if (_initialized) return;

            ConfigureNativeLibraries();
            LoadModel();

            _initialized = true;
            _logger.LogInformation("✅ Model initialized successfully");
        }
    }

    private void ConfigureNativeLibraries()
    {
        // Native library configuration is handled in Program.cs startup
        // This is just a placeholder to maintain the initialization flow
        _logger.LogInformation("Using native library configuration from startup");
    }

    private void LoadModel()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var modelPath = Path.GetFullPath(_config.Model.Path);

        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"Model not found at {modelPath}");
        }

        _logger.LogInformation("Loading model: {Model}", Path.GetFileName(modelPath));

        // Calculate optimal thread count
        var totalCores = Environment.ProcessorCount;
        var physicalCores = totalCores / 2;  // Account for hyperthreading

        // Use physical cores for main work, leave 1-2 for system
        var threads = _config.Performance.ThreadCount > 0
            ? _config.Performance.ThreadCount
            : Math.Max(4, physicalCores - 1);

        _logger.LogInformation("Using {Threads} threads (Physical cores: {Physical}, Total: {Total})",
            threads, physicalCores, totalCores);

        var gpuLayerCount = _config.Model.GpuLayerCount;

        var paramCreateTime = System.Diagnostics.Stopwatch.StartNew();
        _modelParams = new ModelParams(modelPath)
        {
            ContextSize = _config.Model.ContextSize,
            GpuLayerCount = gpuLayerCount,

            // ✅ Keep these
            UseMemorymap = true,  // Critical for fast loading
            BatchSize = (uint)_config.Performance.BatchSize,
            UBatchSize = (uint)Math.Min(512, _config.Performance.BatchSize),
            Threads = threads,

            // ✅ CHANGE these
            UseMemoryLock = false,        // Requires admin rights, causes slowdown if fails
            NoKqvOffload = false,         // Let LLamaSharp auto-manage this
            DefragThreshold = -1f,        // Disable defrag (was 0.1f)
            Embeddings = false,           // Keep false unless you need them

            // ✅ These are fine
            FlashAttention = false,
            MainGpu = gpuLayerCount > 0 ? 0 : -1,
            SplitMode = GPUSplitMode.None,
        };
        paramCreateTime.Stop();
        _logger.LogInformation("ModelParams created in {Time:F1}s", paramCreateTime.Elapsed.TotalSeconds);

        var loadTime = System.Diagnostics.Stopwatch.StartNew();
        _model = LLamaWeights.LoadFromFile(_modelParams);
        loadTime.Stop();

        sw.Stop();
        _logger.LogInformation(
            "✅ Model loaded in {Time:F1}s (params: {ParamTime:F1}s, load: {LoadTime:F1}s) - Context: {Context}, Threads: {Threads}, Batch: {Batch}/{UBatch}",
            sw.Elapsed.TotalSeconds, paramCreateTime.Elapsed.TotalSeconds, loadTime.Elapsed.TotalSeconds,
            _config.Model.ContextSize, _modelParams.Threads, _modelParams.BatchSize, _modelParams.UBatchSize);
    }

    public LLamaWeights GetModel()
    {
        if (_model == null)
            throw new InvalidOperationException("Model not initialized");
        return _model;
    }

    public ModelParams GetModelParams()
    {
        if (_modelParams == null)
            throw new InvalidOperationException("Model parameters not initialized");
        return _modelParams;
    }

    public void Dispose()
    {
        _model?.Dispose();
        _logger.LogInformation("Model resources disposed");
    }
}