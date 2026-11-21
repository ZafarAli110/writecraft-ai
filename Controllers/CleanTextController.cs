using Microsoft.AspNetCore.Mvc;
using System.Text;
using WriteCraftAI.Models;
using WriteCraftAI.Services;

namespace WriteCraftAI.Controllers;

public class CleanTextController : Controller
{
    private readonly InferenceService _inferenceService;
    private readonly AppConfig _config;
    private readonly ILogger<CleanTextController> _logger;

    public CleanTextController(InferenceService inferenceService, AppConfig config,
        ILogger<CleanTextController> logger)
    {
        _inferenceService = inferenceService;
        _config = config;
        _logger = logger;
    }

    public IActionResult Index() => View();

    [HttpPost("/api/cleantext/clean")]
    public async Task Clean([FromBody] CleanTextRequest request)
    {
        _logger.LogInformation("=== CLEAN TEXT REQUEST ===");
        _logger.LogInformation("Text length: {Length}", request.Text?.Length);

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            await WriteEventAsync("error", "No text provided");
            return;
        }

        var words = request.Text.Split(new[] { ' ', '\n', '\r', '\t' },
            StringSplitOptions.RemoveEmptyEntries).Length;

        // Conservative token limits
        var maxTokens = Math.Min(words * 2 + 150, 800);
        var estimatedTokens = words + 100;

        _logger.LogInformation("Words: {Words}, Max tokens: {Max}, Estimated: {Est}",
            words, maxTokens, estimatedTokens);

        var prompt = BuildCleanTextPrompt(request.Text, words);

        // Stop sequences to prevent rambling
        var stopSequences = new[]
        {
            "<|im_end|>",
            "<|endoftext|>",
            "\n\n\n\n",  // Four newlines
            "\n\nNote:",
            "\n\nAdditional:",
            "\n\nP.S."
        };

        try
        {
            await WriteEventAsync("status", "✨ Cleaning text...");

            var tokenCount = 0;
            var buffer = new StringBuilder();
            var fullOutput = new StringBuilder();
            var lastFlush = DateTime.UtcNow;

            await foreach (var token in _inferenceService.StreamInferenceAsync(
                prompt, temperature: 0.3f, maxTokens: maxTokens, stopSequences))
            {
                tokenCount++;
                buffer.Append(token);
                fullOutput.Append(token);

                var timeSinceFlush = (DateTime.UtcNow - lastFlush).TotalMilliseconds;
                if (buffer.Length >= 3 || timeSinceFlush > 100)
                {
                    await WriteEventAsync("token", buffer.ToString());
                    buffer.Clear();
                    lastFlush = DateTime.UtcNow;
                }

                // Progress updates
                if (tokenCount % 20 == 0)
                {
                    var progress = Math.Min(90, (tokenCount * 100 / estimatedTokens));
                    await WriteEventAsync("progress", progress.ToString());
                }
            }

            // Flush remaining
            if (buffer.Length > 0)
            {
                await WriteEventAsync("token", buffer.ToString());
            }

            await WriteEventAsync("progress", "100");

            // Log complete output
            _logger.LogInformation("=== COMPLETE OUTPUT ===\n{Output}\n=== END ===",
                fullOutput.ToString());
            _logger.LogInformation("Completed. Tokens: {Count}", tokenCount);

            await WriteEventAsync("complete", "✅ Done!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Text cleaning failed");
            await WriteEventAsync("error", $"Failed: {ex.Message}");
        }
    }

    private string BuildCleanTextPrompt(string text, int wordCount)
    {
        var systemMessage = @"You are a concise text editor. Clean and improve text with proper formatting.

Rules:
- Use markdown formatting (headers, lists, bold)
- Fix all errors
- Improve clarity
- Keep similar length
- No unnecessary explanations";

        var userMessage = $@"Clean this text. Use markdown formatting:

{text}

Format your response:
### Clean Version
[improved text with markdown formatting]

### Key Changes
- [change 1]
- [change 2]
- [change 3]";

        return $"<|im_start|>system\n{systemMessage}<|im_end|>\n" +
               $"<|im_start|>user\n{userMessage}<|im_end|>\n" +
               $"<|im_start|>assistant\n";
    }

    private async Task WriteEventAsync(string type, string data)
    {
        // CRITICAL: Escape newlines for SSE format, but preserve them in data
        // SSE format requires newlines in data to be literal \n characters
        var escapedData = data?.Replace("\n", "\\n").Replace("\r", "") ?? "";

        await Response.WriteAsync($"event: {type}\n");
        await Response.WriteAsync($"data: {escapedData}\n\n");
        await Response.Body.FlushAsync();
    }
}

public record CleanTextRequest(string Text);