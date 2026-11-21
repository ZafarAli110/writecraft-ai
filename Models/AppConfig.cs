namespace WriteCraftAI.Models;

public record AppConfig
{
    public ModelConfig Model { get; init; } = new();
    public PerformanceConfig Performance { get; init; } = new();
    public LimitsConfig Limits { get; init; } = new();
}

public record ModelConfig
{
    public string Path { get; init; } = string.Empty;
    public uint ContextSize { get; init; } = 4096;
    public int GpuLayerCount { get; init; } = 12;
    public bool AutoDetectBackend { get; init; } = true;
}

public record PerformanceConfig
{
    public int ThreadCount { get; init; } = -1;
    public int BatchSize { get; init; } = 512;
    public bool EnableContextPooling { get; init; } = true;
    public int ContextPoolSize { get; init; } = 2;
}

public record LimitsConfig
{
    public int MaxTokensPerRequest { get; init; } = 2048;
    public bool ShowTokenCounter { get; init; } = true;
    public bool ShowFirstTokenLatency { get; init; } = true;
}
