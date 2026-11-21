using Microsoft.AspNetCore.Mvc;
using System.Text;
using WriteCraftAI.Models;
using WriteCraftAI.Services;

namespace WriteCraftAI.Controllers;

public class GrammarController : Controller
{
    private readonly InferenceService _inferenceService;
    private readonly AppConfig _config;
    private readonly ILogger<GrammarController> _logger;

    public GrammarController(InferenceService inferenceService, AppConfig config,
        ILogger<GrammarController> logger)
    {
        _inferenceService = inferenceService;
        _config = config;
        _logger = logger;
    }

    public IActionResult Index() => View();

    [HttpPost("/api/grammar/analyze")]
    public async Task Analyze([FromBody] GrammarRequest request)
    {
        _logger.LogInformation("=== NEW ANALYSIS REQUEST ===");
        _logger.LogInformation("Mode: {Mode}, Text length: {Length}", request.Mode, request.Text?.Length);
        _logger.LogInformation("Client: {IP}", HttpContext.Connection.RemoteIpAddress);

        // CRITICAL: Disable response buffering for SSE
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            _logger.LogWarning("Empty text received");
            await WriteEventAsync("error", "No text provided");
            return;
        }

        // Token estimation
        var words = request.Text.Split(new[] { ' ', '\n', '\r', '\t' },
            StringSplitOptions.RemoveEmptyEntries).Length;
        var estimatedTokens = (int)(words / 0.75);

        _logger.LogInformation("Estimated tokens: {Tokens}", estimatedTokens);

        if (estimatedTokens > _config.Limits.MaxTokensPerRequest)
        {
            _logger.LogWarning("Text too long: {Tokens} tokens", estimatedTokens);
            await WriteEventAsync("error",
                $"Text too long. Estimated {estimatedTokens} tokens, limit is {_config.Limits.MaxTokensPerRequest}.");
            return;
        }

        var prompt = BuildQwenPrompt(request.Text, request.Mode, words);

        // Use Qwen-specific stop sequences
        var stopSequences = new[] {
            "<|im_end|>",      // Qwen chat template end
            "<|endoftext|>",   // Alternative end token
            "\n\nUser:",       // Prevent continuation
            "\n\nHuman:"       // Prevent continuation
        };

        _logger.LogInformation("Starting inference stream...");
        _logger.LogDebug("Using Qwen-formatted prompt:\n{Prompt}", prompt);

        try
        {
            await WriteEventAsync("status", "🔄 AI is thinking...");

            var tokenCount = 0;
            var buffer = new StringBuilder();
            var fullOutput = new StringBuilder();
            var lastFlush = DateTime.UtcNow;

            await foreach (var token in _inferenceService.StreamInferenceAsync(
                prompt, temperature: 0.3f, maxTokens: 512, stopSequences))
            {
                tokenCount++;
                buffer.Append(token);
                fullOutput.Append(token);

                // Log first few tokens for debugging
                if (tokenCount <= 3)
                {
                    _logger.LogDebug("Token #{Count}: {Token}", tokenCount, token.Replace("\n", "\\n"));
                }

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

            // Log complete output
            _logger.LogInformation("=== COMPLETE OUTPUT ===\n{Output}\n=== END ===",
                fullOutput.ToString());
            _logger.LogInformation("Stream completed. Total tokens: {Count}", tokenCount);
            await WriteEventAsync("complete", "✅ Analysis complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Streaming inference failed");
            await WriteEventAsync("error", $"Processing failed: {ex.Message}");
        }
    }

    private string BuildQwenPrompt(string text, string mode, int wordCount)
    {
        var systemMessage = "You are a concise writing assistant. Be direct. No preambles or unnecessary explanations.";

        var userMessage = mode switch
        {
            "grammar" =>
                $"Fix grammar, spelling, punctuation, and word choice issues. Check for redundant words, incorrect word order, and awkward phrasing. Explain key errors briefly in 1-2 sentences:\n\n{text}\n\nCorrected text:",

            "rephrase" =>
                $"Fix errors and provide 3 short alternative versions:\n\n{text}\n\nCorrected:\nAlternative 1:\nAlternative 2:\nAlternative 3:",

            _ =>
                $"Improve this text briefly:\n\n{text}\n\nImproved:"
        };

        return $"<|im_start|>system\n{systemMessage}<|im_end|>\n" +
               $"<|im_start|>user\n{userMessage}<|im_end|>\n" +
               $"<|im_start|>assistant\n";
    }

    private async Task WriteEventAsync(string type, string data)
    {
        // CRITICAL: Escape newlines for SSE format, but preserve them in data
        var escapedData = data?.Replace("\n", "\\n").Replace("\r", "") ?? "";
        
        // Log each event for debugging
        _logger.LogDebug("SSE Event: {Type} = {Data}", type, escapedData);

        await Response.WriteAsync($"event: {type}\n");
        await Response.WriteAsync($"data: {escapedData}\n\n");
        await Response.Body.FlushAsync();
    }
}

public record GrammarRequest(string Text, string Mode = "grammar");