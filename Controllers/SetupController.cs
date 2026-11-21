using Microsoft.AspNetCore.Mvc;
using WriteCraftAI.Models;
using WriteCraftAI.Services;

namespace WriteCraftAI.Controllers;

public class SetupController : Controller
{
    private readonly AppConfig _config;
    private readonly LlamaModelService _modelService;
    private readonly ILogger<SetupController> _logger;
    private readonly IWebHostEnvironment _env;

    public SetupController(AppConfig config, LlamaModelService modelService,
        ILogger<SetupController> logger, IWebHostEnvironment env)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _env = env ?? throw new ArgumentNullException(nameof(env));
    }

    public IActionResult Index()
    {
        // Skip setup if model is already valid and initialized
        var validation = ValidateModelFileInternal();
        if (validation.IsValid && _modelService.IsHealthy)
        {
            _logger.LogInformation("Model already validated and initialized, redirecting to Grammar");
            return RedirectToAction("Index", "Grammar");
        }

        return View();
    }

    [HttpPost("/api/setup/validate")]
    public JsonResult ValidateModelFile()
    {
        var validation = ValidateModelFileInternal();
        return Json(new { isValid = validation.IsValid, message = validation.Message });
    }

    private (bool IsValid, string Message) ValidateModelFileInternal()
    {
        // Validate configured path first
        if (string.IsNullOrWhiteSpace(_config?.Model?.Path))
        {
            const string error = "Model path is not configured in config.json (Model.Path)";
            _logger.LogError(error);
            return (false, $"❌ {error}");
        }

        // Resolve relative paths against the app content root
        var configured = _config.Model.Path!.Trim();
        string modelPath;

        try
        {
            if (Path.IsPathRooted(configured))
            {
                modelPath = Path.GetFullPath(configured);
                _logger.LogDebug("Using absolute model path: {Path}", modelPath);
            }
            else
            {
                modelPath = Path.GetFullPath(Path.Combine(_env.ContentRootPath, configured));
                _logger.LogDebug("Resolved relative model path: {Path}", modelPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve model path '{Path}'", configured);
            return (false, $"❌ Configured model path is invalid: {ex.Message}");
        }

        // Check if directory exists
        if (!Directory.Exists(Path.GetDirectoryName(modelPath)))
        {
            _logger.LogWarning("Model directory not found: {Path}", Path.GetDirectoryName(modelPath));
            var downloadMsg = GenerateDownloadInstructions();
            return (false, $"❌ Model directory not found.<br/>{downloadMsg}");
        }

        // Check for ANY .gguf file in the directory
        var modelDir = Path.GetDirectoryName(modelPath)!;
        var ggufFiles = Directory.GetFiles(modelDir, "*.gguf");

        if (ggufFiles.Length == 0)
        {
            _logger.LogWarning("No .gguf files found in directory: {Path}", modelDir);
            var downloadMsg = GenerateDownloadInstructions();
            return (false, $"⚠️ No model file (.gguf) found in {modelDir}.<br/>{downloadMsg}");
        }

        // Use the first .gguf file found (or the configured one if it matches)
        var selectedModel = ggufFiles.Length == 1 ? ggufFiles[0] : 
            ggufFiles.FirstOrDefault(f => f == modelPath) ?? ggufFiles[0];

        _logger.LogInformation("Found .gguf file: {File}", selectedModel);

        // Basic integrity check (file size)
        var fileInfo = new FileInfo(selectedModel);
        const long minExpectedSize = 2L * 1024 * 1024 * 1024; // 2GB

        if (fileInfo.Length < minExpectedSize)
        {
            _logger.LogWarning("Model file appears incomplete: {Size} bytes", fileInfo.Length);
            return (false, $"⚠️ Model file appears incomplete ({fileInfo.Length / 1024 / 1024}MB). Expected at least 2GB.");
        }

        // Quick metadata check without loading full model
        try
        {
            // Just validate we can read the file header without loading the entire model
            using var fs = new FileStream(selectedModel, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[4];
            fs.Read(buffer, 0, 4);

            // GGUF files start with "GGUF" magic bytes
            if (buffer[0] == 'G' && buffer[1] == 'G' && buffer[2] == 'U' && buffer[3] == 'F')
            {
                _logger.LogInformation("✅ Model file validated successfully: {Path}", selectedModel);
                return (true, "✅ Model file validated successfully");
            }

            _logger.LogWarning("Model file does not appear to be a valid GGUF file");
            return (false, "❌ Invalid GGUF file format");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Model file validation failed");
            return (false, $"❌ Cannot read model file: {ex.Message}");
        }
    }

    private string GenerateDownloadInstructions()
    {
        return @"
<div style='background: #f3f4f6; padding: 1rem; border-radius: 0.5rem; margin: 1rem 0; font-size: 0.9rem;'>
    <strong>📥 Download a Model</strong>
    <p style='margin: 0.5rem 0;'>Choose one of these recommended models from Hugging Face:</p>
    <ul style='margin: 0.5rem 0; padding-left: 1.5rem;'>
        <li>
            <strong>Qwen2.5 7B Instruct Q4_K_M</strong> (~4.8 GB, faster)
            <br/><a href='https://huggingface.co/bartowski/Qwen2.5-7B-Instruct-GGUF/resolve/main/Qwen2.5-7B-Instruct-Q4_K_M.gguf' target='_blank' style='color: #6366f1;'>Download from Hugging Face</a>
        </li>
        <li>
            <strong>Qwen2.5 14B Instruct Q4_K_M</strong> (~9.6 GB, more capable)
            <br/><a href='https://huggingface.co/bartowski/Qwen2.5-14B-Instruct-GGUF/resolve/main/Qwen2.5-14B-Instruct-Q4_K_M.gguf' target='_blank' style='color: #6366f1;'>Download from Hugging Face</a>
        </li>
    </ul>
    <p style='margin: 0.5rem 0;'><strong>Steps:</strong></p>
    <ol style='margin: 0.5rem 0; padding-left: 1.5rem;'>
        <li>Click the download link (downloads to your computer)</li>
        <li>Move the .gguf file to the <code style='background: #fff; padding: 0.25rem 0.5rem;'>AIModels</code> folder</li>
        <li><strong>Reload this page</strong> - the model will be automatically detected</li>
    </ol>
</div>";
    }

    [HttpPost("/api/setup/initialize")]
    public IActionResult InitializeModel()
    {
        try
        {
            _logger.LogInformation("Starting model initialization...");

            // This actually loads the model into memory
            _modelService.Initialize();

            _logger.LogInformation("✅ Model initialized successfully");
            return Ok(new { success = true, message = "✅ Model initialized successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Model initialization failed");
            return StatusCode(500, new { success = false, message = $"Initialization failed: {ex.Message}" });
        }
    }

    [HttpGet("/api/setup/health")]
    public IActionResult HealthCheck()
    {
        try
        {
            var isReady = _modelService.IsHealthy;
            string backend = "CPU";

            if (isReady)
            {
                // Detect actual backend being used
                if (_config.Model.AutoDetectBackend &&
                    System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                        System.Runtime.InteropServices.OSPlatform.Linux))
                {
                    backend = "Vulkan/CPU";
                }
                else if (_config.Model.GpuLayerCount > 0)
                {
                    backend = $"GPU ({_config.Model.GpuLayerCount} layers) + CPU";
                }
            }

            return Json(new
            {
                isReady,
                backend,
                modelLoaded = isReady,
                contextSize = isReady ? _config.Model.ContextSize : 0
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return Json(new { isReady = false, backend = "Unknown", error = ex.Message });
        }
    }
}