using System.Text;
using Microsoft.AspNetCore.Mvc;
using WriteCraftAI.Services;

namespace WriteCraftAI.Controllers;

public class EnglishTranslationController: Controller
{
  private readonly InferenceService _inferenceService;
  private readonly ILogger<EnglishTranslationController> _logger;

  public EnglishTranslationController(InferenceService inferenceService,
      ILogger<EnglishTranslationController> logger)
  {
      _inferenceService = inferenceService;
      _logger = logger;
  }

  public IActionResult Index() => View();

  [HttpPost("/api/english-translation/translate")]
  public async Task Translate([FromBody] EnglishTranslationRequest translation)
  {
    _logger.LogInformation("=== ENGLISH TRANSLATION REQUEST ===");
    _logger.LogInformation("Content length: {Length}", translation.Content?.Length);

    Response.Headers.ContentType = "text/event-stream";
    Response.Headers.CacheControl = "no-cache";
    Response.Headers["X-Accel-Buffering"] = "no";

    if (string.IsNullOrWhiteSpace(translation.Content))
    {
        _logger.LogWarning("Empty content received");
       await WriteEventAsync("error", "No content provided");
            return;
    }

   var systemPrompt = "You are a professional translator. Output only the direct English translation. Do not add explanations, notes, or additional text.";
var userPrompt = $"Translate to English: \"{translation.Content}\"";
var prompt = $"<|im_start|>system\n{systemPrompt}<|im_end|>\n<|im_start|>user\n{userPrompt}<|im_end|>\n<|im_start|>assistant\n";
   var stopSequences = new[] { "<|im_end|>", "\n\n", "###", "Note:", "Explanation:" };

        try
        {
            await WriteEventAsync("status", "🌐 Translating...");

            var tokenCount = 0;
            var buffer = new StringBuilder();
            var lastFlush = DateTime.UtcNow;

            await foreach (var token in _inferenceService.StreamInferenceAsync(
                prompt, temperature: 0.05f, maxTokens: 100, stopSequences))
            {
                tokenCount++;
                buffer.Append(token);

                var timeSinceFlush = (DateTime.UtcNow - lastFlush).TotalMilliseconds;
                if (buffer.Length >= 3 || timeSinceFlush > 100)
                {
                    await WriteEventAsync("token", buffer.ToString());
                    buffer.Clear();
                    lastFlush = DateTime.UtcNow;
                }
            }

            if (buffer.Length > 0)
            {
                await WriteEventAsync("token", buffer.ToString());
            }

            _logger.LogInformation("Translation completed. Tokens: {Count}", tokenCount);
            await WriteEventAsync("complete", "✅ Done!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Translation failed");
            await WriteEventAsync("error", $"Failed: {ex.Message}");
        }
  }

  private async Task WriteEventAsync(string type, string data)
    {
        // CRITICAL: Escape newlines for SSE format
        var escapedData = data?.Replace("\n", "\\n").Replace("\r", "") ?? "";

        await Response.WriteAsync($"event: {type}\n");
        await Response.WriteAsync($"data: {escapedData}\n\n");
        await Response.Body.FlushAsync();
    }
}

public record EnglishTranslationRequest(
    string Content
);