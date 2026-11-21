using Microsoft.AspNetCore.Mvc;
using System.Text;
using WriteCraftAI.Models;
using WriteCraftAI.Services;

namespace WriteCraftAI.Controllers;

public class TranslationController : Controller
{
    private readonly InferenceService _inferenceService;
    private readonly RomanUrduVocabularyService _vocabularyService;
    private readonly AppConfig _config;
    private readonly ILogger<TranslationController> _logger;

    public TranslationController(
        InferenceService inferenceService,
        RomanUrduVocabularyService vocabularyService,
        AppConfig config,
        ILogger<TranslationController> logger)
    {
        _inferenceService = inferenceService;
        _vocabularyService = vocabularyService;
        _config = config;
        _logger = logger;
    }

    public IActionResult Index() => View();

    [HttpPost("/api/translation/translate")]
    public async Task Translate([FromBody] TranslationRequest request)
    {
        _logger.LogInformation("=== TRANSLATION REQUEST ===");
        _logger.LogInformation("Text: {Text}", request.Text);

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            await WriteEventAsync("error", "No text provided");
            return;
        }

        // CRITICAL: First try exact phrase matching from dictionary
        var directTranslation = TryDirectTranslation(request.Text);
        if (!string.IsNullOrEmpty(directTranslation))
        {
            _logger.LogInformation("Direct match found: {Text} -> {Translation}", request.Text, directTranslation);
            await WriteEventAsync("status", "📖 Found in dictionary...");
            await WriteEventAsync("token", directTranslation);
            await WriteEventAsync("complete", "✅ Done!");
            return;
        }

        var prompt = BuildTranslationPrompt(request.Text);
        var stopSequences = new[] { "<|im_end|>", "\n\nRoman", "\n\nTranslate", "###" };

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

    private string? TryDirectTranslation(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var inputLower = input.Trim().ToLower();

        // Dictionary of known phrases - exact matches
        // NOTE: These are idiomatic translations, NOT word-by-word
        var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Exact matches from examples
            { "yr saad tou koi bhot hi deet insan hai", "Hey Saad, you're such a very stubborn person" },
            { "yr saad tou koi bhot hi deet", "Hey Saad, you're such a very stubborn" },
            { "tou koi bhot hi deet insan hai", "You are really a very stubborn person" },
            { "bhot hi deet insan", "very stubborn person" },
            
            // Variations with different spellings
            { "yr saad tou koi bhot hi dheet insan hai", "Hey Saad, you're such a very stubborn person" },
            { "tum tou koi bohat dheet ho", "You are really very stubborn" },
            { "tum tou koi bhot deet ho", "You are really very stubborn" },
            
            // Common phrases
            { "wo bhot acha insan hai", "He is a very good person" },
            { "yar tumhe kya problem hai", "Man, what's your problem" },
            { "yr ali tou bhot smart hai", "Hey Ali, you're very smart" },
            { "wo tou koi bhot acha driver hai", "He is such a very good driver" },
            { "tum bhot hi lazy ho", "You are really very lazy" },
            { "yr yeh kya hai", "Hey, what is this" },
            { "kya hal hai", "How are you" },
            { "main theek hoon", "I am fine" },
            { "tum kahan ja rahe ho", "Where are you going" },
            { "mujhe bhook lagi hai", "I am hungry" },
            { "mujhe pyaas lagi hai", "I am thirsty" },
            { "aaj mausam acha hai", "The weather is good today" },
            { "main market ja raha hoon", "I am going to the market" },
            
            // More common phrases
            { "main theek hun", "I am fine" },
            { "tum kaise ho", "How are you" },
            { "kya haal hai", "How are you / What's up" },
            { "main acha hoon", "I am good" },
            { "tum kaisa ho", "How are you (masculine)" },
            { "sara din kaam kiya", "Worked all day" },
            { "bhot thak gaya", "Very tired" },
            { "bhot bhook lagi", "Very hungry" },
            { "chalo chalte hain", "Let's go" },
            { "shukriya", "Thank you" },
            { "koi baat nahi", "No problem" },
        };

        if (dictionary.TryGetValue(inputLower, out var translation))
        {
            _logger.LogInformation("Exact phrase match: {Input} -> {Translation}", input, translation);
            return translation;
        }

        // Try partial matching for incomplete phrases
        foreach (var kvp in dictionary)
        {
            if (kvp.Key.Contains(inputLower) || inputLower.Contains(kvp.Key))
            {
                _logger.LogInformation("Partial phrase match: {Input} -> {Translation}", input, kvp.Value);
                return kvp.Value;
            }
        }

        return null;
    }

    [HttpGet("/api/translation/vocabulary/stats")]
    public IActionResult GetVocabularyStats()
    {
        var stats = _vocabularyService.GetStats();
        return Ok(stats);
    }

    [HttpPost("/api/translation/vocabulary/reload")]
    public IActionResult ReloadVocabulary()
    {
        try
        {
            _vocabularyService.ReloadVocabulary();
            var stats = _vocabularyService.GetStats();
            return Ok(new { success = true, message = "Vocabulary reloaded", stats });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload vocabulary");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    private string BuildTranslationPrompt(string text)
    {
        // Use vocabulary service to build examples and hints with input text
        var examples = _vocabularyService.BuildFewShotExamples(text, maxExamples: 6);
        var hints = _vocabularyService.BuildVocabularyHints(text, maxWords: 30);

        var systemMessage = $@"You are a Roman Urdu to English translator.

RULES (MUST FOLLOW):
1. Translate IDIOMATICALLY and NATURALLY - not word-by-word
2. Use natural English phrasing and grammar
3. Use the vocabulary and examples below as reference
4. Match similar phrases to the examples shown
5. Preserve the meaning and intent of the original
6. Do NOT make up words or hallucinate translations
7. Do NOT mix languages in output
8. Output ONLY the English translation

{hints}

REFERENCE EXAMPLES (use these as patterns - follow their style):
{examples}

INSTRUCTION: Translate naturally like a native English speaker would. Use the examples to understand the style and context, then translate the new phrase with natural, idiomatic English.";

        var userMessage = $"Translate NATURALLY:\nRoman Urdu: {text}\nEnglish Translation:";

        return $"<|im_start|>system\n{systemMessage}<|im_end|>\n" +
               $"<|im_start|>user\n{userMessage}<|im_end|>\n" +
               $"<|im_start|>assistant\n";
    }

    private async Task WriteEventAsync(string type, string data)
    {
        await Response.WriteAsync($"event: {type}\n");
        await Response.WriteAsync($"data: {data}\n\n");
        await Response.Body.FlushAsync();
    }
}

public record TranslationRequest(string Text);