using Microsoft.AspNetCore.Mvc;
using System.Text;
using WriteCraftAI.Models;
using WriteCraftAI.Services;

namespace WriteCraftAI.Controllers;

public class GitAssistantController : Controller
{
    private readonly InferenceService _inferenceService;
    private readonly AppConfig _config;
    private readonly ILogger<GitAssistantController> _logger;

    public GitAssistantController(InferenceService inferenceService, AppConfig config,
        ILogger<GitAssistantController> logger)
    {
        _inferenceService = inferenceService;
        _config = config;
        _logger = logger;
    }


    public IActionResult Index() => View();

    [HttpPost("/api/git/assist")]
    public async Task Assist([FromBody] GitAssistantRequest request)
    {
        _logger.LogInformation("=== GIT GURU REQUEST ===");
        _logger.LogInformation("Query: {Query}", request.Query?.Substring(0, Math.Min(100, request.Query?.Length ?? 0)));

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            await WriteEventAsync("error", "No query provided");
            return;
        }

        var prompt = BuildGitGuruPrompt(request.Query);

        try
        {
            var tokenCount = 0;
            var startTime = DateTime.UtcNow;
            var firstTokenTime = (DateTime?)null;
            var outputBuilder = new StringBuilder();

          var stopSequences = new[] { 
            "<|im_end|>", 
            "---",           // Section break
            "Let me know",   // Prevents conversational endings
            "Feel free",     // Prevents filler
            "Hope this"      // Prevents "Hope this helps"
        };

            await foreach (var token in _inferenceService.StreamInferenceAsync(
                prompt, temperature: 0.1f, maxTokens: 1024, stopSequences: stopSequences))
            {
                if (firstTokenTime == null)
                {
                    firstTokenTime = DateTime.UtcNow;
                    var ttft = (firstTokenTime.Value - startTime).TotalMilliseconds;
                    await WriteEventAsync("metrics", $"{{\"ttft\": {ttft:F0}}}");
                }

                tokenCount++;
                outputBuilder.Append(token);
                await WriteEventAsync("token", token);
            }

            var totalTime = (DateTime.UtcNow - startTime).TotalSeconds;
            var tokensPerSec = tokenCount / totalTime;

            await WriteEventAsync("complete", $"{{\"tokens\": {tokenCount}, \"time\": {totalTime:F1}, \"tps\": {tokensPerSec:F1}}}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Git Guru streaming failed");
            await WriteEventAsync("error", ex.Message);
        }
    }

 private static string BuildGitGuruPrompt(string query)
{
    return $@"<|im_start|>system
You are Git Guru, an expert Git assistant.

RULES:
1. If user asks ""is this correct?"" - analyze their command, explain what it actually does, then provide the CORRECT command if theirs is wrong
2. Provide git commands in a ```bash code block
3. Use placeholders like <commit-hash>, <branch-name>, <your-username> for values user hasn't provided
4. Keep explanation to 2-3 sentences max
5. Add ⚠️ warning only for destructive commands (reset --hard, force push, etc.)
6. Never ask questions - provide the complete answer<|im_end|>
<|im_start|>user
{query}<|im_end|>
<|im_start|>assistant
";
}

    private async Task WriteEventAsync(string eventType, string data)
    {
        var escapedData = data.Replace("\n", "\\n").Replace("\r", "");
        await Response.WriteAsync($"event: {eventType}\ndata: {escapedData}\n\n");
        await Response.Body.FlushAsync();
    }
}

public class GitAssistantRequest
{
    public string Query { get; set; } = "";
}
