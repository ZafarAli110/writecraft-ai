using Microsoft.AspNetCore.Mvc;
using System.Text;
using WriteCraftAI.Models;
using WriteCraftAI.Services;

namespace WriteCraftAI.Controllers;

public class EmailWriterController : Controller
{
    private readonly InferenceService _inferenceService;
    private readonly AppConfig _config;
    private readonly ILogger<EmailWriterController> _logger;

    public EmailWriterController(InferenceService inferenceService, AppConfig config,
        ILogger<EmailWriterController> logger)
    {
        _inferenceService = inferenceService;
        _config = config;
        _logger = logger;
    }

    public IActionResult Index() => View();

    [HttpPost("/api/email/write")]
    public async Task WriteEmail([FromBody] EmailWriterRequest request)
    {
        _logger.LogInformation("=== EMAIL WRITING REQUEST ===");
        _logger.LogInformation("Tone: {Tone}, Length: {Length}, Content: {Content}",
            request.Tone, request.Length, request.Content?.Substring(0, Math.Min(50, request.Content.Length)));

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            await WriteEventAsync("error", "No content provided");
            return;
        }

        var words = request.Content.Split(new[] { ' ', '\n', '\r', '\t' },
            StringSplitOptions.RemoveEmptyEntries).Length;

        // Strict limits based on requested length
        var (maxTokens, estimatedTokens, wordLimit) = GetEmailLimits(request.Length, words);

        _logger.LogInformation("Email limits - Max tokens: {Max}, Word limit: {Words}",
            maxTokens, wordLimit);

        var prompt = BuildEmailPrompt(request.Content, request.Tone, request.Subject,
            request.Length, wordLimit);

        // Aggressive stop sequences to prevent rambling
        var stopSequences = new[]
        {
            "<|im_end|>",
            "<|endoftext|>",
            "\n\n\n",
            "\n\nBest regards,\n",
            "\n\nSincerely,\n",
            "\n\nThank you,\n",
            "\n\nRegards,\n",
            "\n\nNote:",
            "\n\nP.S.:",
            "\n\n---"
        };

        try
        {
            await WriteEventAsync("status", $"✉️ Writing {request.Length} {request.Tone} email...");

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

                // Emergency brake: stop if output is getting too long
                var currentOutput = fullOutput.ToString();
                var currentWords = currentOutput.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

                if (currentWords > wordLimit * 1.5)
                {
                    _logger.LogWarning("Email exceeded word limit, stopping generation");
                    break;
                }

                // Stop if we detect conclusion markers
                if (currentOutput.Contains("Best regards") ||
                    currentOutput.Contains("Sincerely") ||
                    currentOutput.Contains("Thank you for") && currentOutput.Length > 200)
                {
                    _logger.LogDebug("Detected email closing, completing generation");
                    break;
                }
            }

            if (buffer.Length > 0)
            {
                await WriteEventAsync("token", buffer.ToString());
            }

            await WriteEventAsync("progress", "100");
            
            // Log complete output
            _logger.LogInformation("=== COMPLETE OUTPUT ===\n{Output}\n=== END ===",
                fullOutput.ToString());
            _logger.LogInformation("Email completed. Tokens: {Count}", tokenCount);
            await WriteEventAsync("complete", "✅ Email ready!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email writing failed");
            await WriteEventAsync("error", $"Failed: {ex.Message}");
        }
    }

    private (int maxTokens, int estimatedTokens, int wordLimit) GetEmailLimits(
        string length, int inputWords)
    {
        return length.ToLower() switch
        {
            "short" => (150, 120, 100),      // ~100 words max
            "medium" => (300, 250, 200),     // ~200 words max
            "long" => (500, 400, 350),       // ~350 words max
            _ => (300, 250, 200)             // Default to medium
        };
    }

    private string BuildEmailPrompt(string content, string tone, string? subject,
        string length, int wordLimit)
    {
        var toneGuidance = tone.ToLower() switch
        {
            "professional" => "professional and polished",
            "casual" => "friendly and conversational",
            "business" => "formal and business-appropriate",
            _ => "appropriate"
        };

        var lengthGuidance = length.ToLower() switch
        {
            "short" => $"Keep it brief and concise (under {wordLimit} words). Be direct.",
            "medium" => $"Write a complete but concise email (around {wordLimit} words).",
            "long" => $"Write a detailed email (up to {wordLimit} words).",
            _ => $"Keep it concise (around {wordLimit} words)."
        };

        var systemMessage = $@"You are an email writing assistant. Write {toneGuidance} emails.

Rules:
- {lengthGuidance}
- Include greeting, body, and closing
- No explanations or notes
- Stop after the closing signature
- Be direct and clear";

        var subjectLine = !string.IsNullOrWhiteSpace(subject)
            ? $"\n\nSubject: {subject}"
            : "";

        var userMessage = $@"Write a {tone} email based on these key points:{subjectLine}

{content}

Write the complete email now:";

        return $"<|im_start|>system\n{systemMessage}<|im_end|>\n" +
               $"<|im_start|>user\n{userMessage}<|im_end|>\n" +
               $"<|im_start|>assistant\n";
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

public record EmailWriterRequest(
    string Content,
    string Tone = "professional",
    string Length = "medium",
    string? Subject = null);