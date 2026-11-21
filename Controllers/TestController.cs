using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using WriteCraftAI.Services;

namespace WriteCraftAI.Controllers;

[ApiController]
[Route("api/perf")]
public class PerformanceTestController : ControllerBase
{
    private readonly InferenceService _inferenceService;
    private readonly ILogger<PerformanceTestController> _logger;

    public PerformanceTestController(InferenceService inferenceService,
        ILogger<PerformanceTestController> logger)
    {
        _inferenceService = inferenceService;
        _logger = logger;
    }

    [HttpGet("quick")]
    public async Task<IActionResult> QuickTest()
    {
        _logger.LogInformation("=== QUICK PERFORMANCE TEST ===");

        var testPrompt = "<|im_start|>system\nYou are a helpful assistant.<|im_end|>\n" +
                        "<|im_start|>user\nCount from 1 to 5<|im_end|>\n" +
                        "<|im_start|>assistant\n";

        var sw = Stopwatch.StartNew();
        var tokens = new List<string>();
        double? firstTokenMs = null;

        try
        {
            await foreach (var token in _inferenceService.StreamInferenceAsync(
                testPrompt,
                temperature: 0.3f,
                maxTokens: 30,
                stopSequences: new[] { "<|im_end|>" }))
            {
                if (tokens.Count == 0)
                    firstTokenMs = sw.Elapsed.TotalMilliseconds;

                tokens.Add(token);
            }

            sw.Stop();
            var result = string.Join("", tokens);
            var tokensPerSec = tokens.Count / sw.Elapsed.TotalSeconds;

            var grade = tokensPerSec switch
            {
                > 5.0 => "🚀 Excellent",
                > 3.0 => "✅ Good",
                > 2.0 => "⚠️ Acceptable",
                _ => "❌ Poor - needs optimization"
            };

            return Ok(new
            {
                tokenCount = tokens.Count,
                firstTokenMs = firstTokenMs,
                totalSeconds = sw.Elapsed.TotalSeconds,
                tokensPerSecond = Math.Round(tokensPerSec, 2),
                grade = grade,
                output = result.Substring(0, Math.Min(50, result.Length)) + "..."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("roman-urdu")]
    public async Task<IActionResult> RomanUrduTest()
    {
        _logger.LogInformation("=== ROMAN URDU QUALITY TEST ===");

        var testCases = new[]
        {
            "mujhe yeh kaam abhi karna hai kyunki kal time nahi milega",
            "aaj mausam bohat acha hai",
            "main apne dost ke saath market gaya tha"
        };

        var results = new List<object>();

        foreach (var testCase in testCases)
        {
            var prompt = $"<|im_start|>system\nTranslate Roman Urdu to English.<|im_end|>\n" +
                        $"<|im_start|>user\n{testCase}<|im_end|>\n" +
                        $"<|im_start|>assistant\n";

            var sw = Stopwatch.StartNew();
            var tokens = new List<string>();

            try
            {
                await foreach (var token in _inferenceService.StreamInferenceAsync(
                    prompt,
                    temperature: 0.3f,
                    maxTokens: 100,
                    stopSequences: new[] { "<|im_end|>" }))
                {
                    tokens.Add(token);
                }

                sw.Stop();
                var translation = string.Join("", tokens).Trim();

                results.Add(new
                {
                    input = testCase,
                    translation = translation,
                    tokensPerSec = Math.Round(tokens.Count / sw.Elapsed.TotalSeconds, 2),
                    timeSeconds = Math.Round(sw.Elapsed.TotalSeconds, 1)
                });
            }
            catch (Exception ex)
            {
                results.Add(new { input = testCase, error = ex.Message });
            }
        }

        return Ok(new { testResults = results });
    }

    [HttpGet("stress")]
    public async Task<IActionResult> StressTest([FromQuery] int iterations = 3)
    {
        _logger.LogInformation("=== STRESS TEST ({Count} iterations) ===", iterations);

        var prompt = "<|im_start|>system\nYou are helpful.<|im_end|>\n" +
                    "<|im_start|>user\nSay hello<|im_end|>\n" +
                    "<|im_start|>assistant\n";

        var results = new List<object>();

        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            var tokens = new List<string>();
            double? firstTokenMs = null;

            try
            {
                await foreach (var token in _inferenceService.StreamInferenceAsync(
                    prompt,
                    temperature: 0.3f,
                    maxTokens: 20,
                    stopSequences: new[] { "<|im_end|>" }))
                {
                    if (tokens.Count == 0)
                        firstTokenMs = sw.Elapsed.TotalMilliseconds;
                    tokens.Add(token);
                }

                sw.Stop();

                results.Add(new
                {
                    iteration = i + 1,
                    tokenCount = tokens.Count,
                    firstTokenMs = firstTokenMs,
                    totalSeconds = Math.Round(sw.Elapsed.TotalSeconds, 2),
                    tokensPerSecond = Math.Round(tokens.Count / sw.Elapsed.TotalSeconds, 2)
                });
            }
            catch (Exception ex)
            {
                results.Add(new { iteration = i + 1, error = ex.Message });
            }

            // Small delay between iterations
            await Task.Delay(500);
        }

        var avgTokPerSec = results
            .Where(r => r.GetType().GetProperty("tokensPerSecond") != null)
            .Select(r => (double)r.GetType().GetProperty("tokensPerSecond")!.GetValue(r)!)
            .Average();

        return Ok(new
        {
            iterations = results,
            averageTokensPerSecond = Math.Round(avgTokPerSec, 2),
            recommendation = avgTokPerSec switch
            {
                > 5.0 => "Performance is excellent! 🚀",
                > 3.0 => "Good performance for your hardware ✅",
                > 2.0 => "Acceptable but could be better ⚠️",
                _ => "Consider trying different batch sizes or thread counts ❌"
            }
        });
    }
}