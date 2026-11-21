using System.Diagnostics;
using System.Runtime.InteropServices;
using LLama.Native;
using Microsoft.Extensions.Options;
using Whisper.net;
using WriteCraftAI.Data;
using WriteCraftAI.Models;
using WriteCraftAI.Services;

// ✅ Configure LlamaSharp logging
NativeLibraryConfig.All.WithLogCallback((level, message) => {
    if (level is LLamaLogLevel.Error or LLamaLogLevel.Warning or LLamaLogLevel.Info)
        Console.WriteLine($"[LLamaSharp] {level}: {message}");
});

// ✅ Optional: Verify backend loaded (for debugging)
try {
    var infoPtr = NativeApi.llama_print_system_info();
    var info = Marshal.PtrToStringAnsi(infoPtr) ?? "Unknown";
    Console.WriteLine("[LLamaSharp] System info: " + info);
} catch (Exception ex) {
    Console.WriteLine($"[LLamaSharp] Warning: {ex.Message}");
}

var builder = WebApplication.CreateBuilder(args);

// If launched with --service, configure for Windows Service usage
var isService = args != null && args.Contains("--service", StringComparer.OrdinalIgnoreCase);
var restrictKestrelToLoopback = isService;

builder.Configuration.AddJsonFile("config.json", optional: true, reloadOnChange: true);

// Bind configuration
builder.Services.Configure<AppConfig>(builder.Configuration);
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<AppConfig>>().Value);

// Add services to the container.
builder.Services.AddSingleton<LlamaModelService>();
builder.Services.AddScoped<InferenceService>();
builder.Services.AddSingleton<RomanUrduVocabularyService>();
builder.Services.AddSingleton<WhisperService>();
builder.Services.AddSingleton<BookmarkVectorDb>();
builder.Services.AddScoped<BookmarkParserService>();
builder.Services.AddScoped<BookmarkEmbeddingService>();
builder.Services.AddScoped<BookmarkService>();

builder.Services.AddControllersWithViews();

builder.WebHost.ConfigureKestrel(options => {
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(30);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(5);
    options.AddServerHeader = false;
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name.EndsWith(".wasm") || 
            ctx.File.Name.Contains("ort-wasm"))
        {
            ctx.Context.Response.Headers.Append("Cross-Origin-Opener-Policy", "same-origin");
            ctx.Context.Response.Headers.Append("Cross-Origin-Embedder-Policy", "require-corp");
        }
    }
});
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Setup}/{action=Index}/{id?}");

try
{
    var config = app.Services.GetRequiredService<AppConfig>();
    var modelService = app.Services.GetRequiredService<LlamaModelService>();

    var modelPath = Path.GetFullPath(
        Path.IsPathRooted(config.Model.Path)
            ? config.Model.Path
            : Path.Combine(app.Environment.ContentRootPath, config.Model.Path)
    );

    if (File.Exists(modelPath))
    {
        var fileInfo = new FileInfo(modelPath);
        if (fileInfo.Length > 2L * 1024 * 1024 * 1024)
        {
            app.Logger.LogInformation("Pre-initializing model on startup...");
            modelService.Initialize();
            app.Logger.LogInformation("✅ Model ready for immediate use");
        }
    }
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Could not pre-initialize model on startup. User will need to run setup.");
}

// Pre-initialize Whisper for faster ASR on first use
try
{
    var whisperService = app.Services.GetRequiredService<WhisperService>();
    app.Logger.LogInformation("Pre-initializing Whisper model...");
    whisperService.Initialize();
    app.Logger.LogInformation("✅ Whisper ready for immediate use");
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Could not pre-initialize Whisper. Will initialize on first use.");
}

if (!isService && Environment.UserInteractive)
{
    var url = "http://localhost:5555";
    app.Logger.LogInformation("Opening browser to {Url}", url);

    try
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Could not auto-open browser. Please navigate to {Url} manually.", url);
    }
}

app.Run();
