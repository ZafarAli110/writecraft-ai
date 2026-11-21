using Microsoft.AspNetCore.Mvc;
using System.Text;
using WriteCraftAI.Models;
using WriteCraftAI.Services;

namespace WriteCraftAI.Controllers;

public class WritingAssistantController : Controller
{
    private readonly InferenceService _inferenceService;
    private readonly AppConfig _config;
    private readonly ILogger<WritingAssistantController> _logger;

    public WritingAssistantController(InferenceService inferenceService, AppConfig config,
        ILogger<WritingAssistantController> logger)
    {
        _inferenceService = inferenceService;
        _config = config;
        _logger = logger;
    }

    public IActionResult Index() => View();

    [HttpPost("/api/writing/assist")]
    public async Task Assist([FromBody] WritingAssistRequest request)
    {
        _logger.LogInformation("=== WRITING ASSISTANCE REQUEST ===");
        _logger.LogInformation("Task: {Task}, Text length: {Length}", request.Task, request.Text?.Length);

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

        // Realistic token limits based on task
        var (maxTokens, estimatedTokens) = GetTaskLimits(request.Task, words);

        _logger.LogInformation("Task: {Task}, Max tokens: {Max}, Estimated: {Est}",
            request.Task, maxTokens, estimatedTokens);

        var prompt = BuildWritingPrompt(request.Text, request.Task, words);
        var stopSequences = GetTaskStopSequences(request.Task);
        
        // Use higher temperature for creative tasks
        var temperature = request.Task.ToLower() switch
        {
            "creative" => 0.7f,
            "general" => 0.5f,
            "expand" => 0.5f,
            _ => 0.4f
        };

        try
        {
            await WriteEventAsync("status", $"✍️ {GetTaskDescription(request.Task)}...");

            var tokenCount = 0;
            var buffer = new StringBuilder();
            var fullOutput = new StringBuilder();
            var lastFlush = DateTime.UtcNow;

            await foreach (var token in _inferenceService.StreamInferenceAsync(
                prompt, temperature: temperature, maxTokens: maxTokens, stopSequences))
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
            _logger.LogError(ex, "Writing assistance failed");
            await WriteEventAsync("error", $"Failed: {ex.Message}");
        }
    }

    private (int maxTokens, int estimatedTokens) GetTaskLimits(string task, int inputWords)
    {
        // For short prompts (like "write a story about X"), we need generous token limits
        // to allow the model to generate complete content without cutting off mid-sentence
        var isShortPrompt = inputWords < 50;
        
        return task.ToLower() switch
        {
            // General and creative tasks need more room for open-ended generation
            "general" => isShortPrompt ? (1024, 600) : (768, 450),
            "creative" => isShortPrompt ? (1024, 600) : (Math.Max(inputWords * 2, 512), Math.Max(inputWords, 300)),
            
            // Summary should be concise
            "summarize" => (Math.Min(inputWords / 2 + 50, 200), Math.Min(inputWords / 2, 150)),
            
            // Expansion needs more tokens
            "expand" => (Math.Max(inputWords * 3, 512), Math.Max(inputWords * 2, 300)),
            
            // These maintain similar length to input
            "simplify" => (inputWords + 150, inputWords + 75),
            "improve" => (inputWords + 200, inputWords + 100),
            "formal" => (inputWords + 150, inputWords + 75),
            
            _ => (inputWords + 150, inputWords + 75)
        };
    }

    private string[] GetTaskStopSequences(string task)
    {
        var common = new[] { "<|im_end|>", "<|endoftext|>", "\n\n\n" };

        return task.ToLower() switch
        {
            "summarize" => common.Concat(new[] { "\n\nIn summary", "\n\nOriginal" }).ToArray(),
            "expand" => common.Concat(new[] { "\n\nNote:", "\n\nOriginal" }).ToArray(),
            _ => common
        };
    }

    private string BuildWritingPrompt(string text, string task, int wordCount)
    {
        // For short prompts, use a more generative system message
        var isShortPrompt = wordCount < 50;
        
        var systemMessage = (task.ToLower(), isShortPrompt) switch
        {
            ("creative", true) => "You are a creative writer. Write engaging, complete stories with a beginning, middle, and end. Include vivid descriptions and dialogue. Always finish your story properly - never stop mid-sentence.",
            ("general", true) => "You are a helpful writing assistant. When asked to write something, create complete, well-structured content. Always finish your response properly - never stop mid-sentence.",
            _ => "You are a concise writing assistant. Be direct and focused. No unnecessary explanations or preambles."
        };

        var userMessage = task.ToLower() switch
        {
            "general" when isShortPrompt =>
                $"{text}\n\nWrite a complete, well-structured response:",
            
            "general" =>
                $"Help me with the following writing request or question. Be helpful and provide a clear response:\n\n{text}\n\nResponse:",

            "summarize" =>
                $"Summarize this in 2-3 sentences. Keep only essential information:\n\n{text}\n\nSummary:",

            "expand" =>
                $"Expand this text significantly. Add relevant details, examples, and depth. Aim for about {Math.Max(wordCount * 2, 200)} words:\n\n{text}\n\nExpanded version:",

            "simplify" =>
                $"Rewrite this in simple, clear language. Same length or shorter:\n\n{text}\n\nSimplified:",

            "improve" =>
                $"Improve this text. Fix issues, enhance clarity. Keep similar length:\n\n{text}\n\nImproved:",

            "formal" =>
                $"Rewrite this in formal, professional language. Same length:\n\n{text}\n\nFormal version:",

            "creative" when isShortPrompt =>
                $"{text}\n\nWrite a complete, engaging story with a proper beginning, middle, and ending:",
            
            "creative" =>
                $"Rewrite this more creatively and engagingly with vivid language:\n\n{text}\n\nCreative version:",

            _ =>
                $"Improve this text:\n\n{text}\n\nImproved version:"
        };

        return $"<|im_start|>system\n{systemMessage}<|im_end|>\n" +
               $"<|im_start|>user\n{userMessage}<|im_end|>\n" +
               $"<|im_start|>assistant\n";
    }

    private string GetTaskDescription(string task)
    {
        return task.ToLower() switch
        {
            "general" => "Generating response",
            "summarize" => "Summarizing",
            "expand" => "Expanding",
            "simplify" => "Simplifying",
            "improve" => "Improving",
            "formal" => "Formalizing",
            "creative" => "Enhancing",
            _ => "Processing"
        };
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

public record WritingAssistRequest(string Text, string Task = "improve");