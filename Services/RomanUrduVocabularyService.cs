using System.Text.Json;
using System.Text.Json.Serialization;

namespace WriteCraftAI.Services;

public class RomanUrduVocabularyService
{
    private readonly ILogger<RomanUrduVocabularyService> _logger;
    private readonly IWebHostEnvironment _env;
    private VocabularyData? _vocabulary;
    private DateTime _lastLoadTime;
    private readonly string _vocabularyPath;

    public RomanUrduVocabularyService(ILogger<RomanUrduVocabularyService> logger,
        IWebHostEnvironment env)
    {
        _logger = logger;
        _env = env;
        var primaryPath = Path.Combine(_env.ContentRootPath, "urdu-vocabulary.json");
        var legacyPath = Path.Combine(_env.ContentRootPath, "vocabulary.json");
        _vocabularyPath = File.Exists(primaryPath) ? primaryPath : legacyPath;
        LoadVocabulary();
    }

    public void LoadVocabulary()
    {
        try
        {
            if (!File.Exists(_vocabularyPath))
            {
                _logger.LogWarning("vocabulary.json not found at {Path}, using defaults", _vocabularyPath);
                _vocabulary = GetDefaultVocabulary();
                return;
            }

            var json = File.ReadAllText(_vocabularyPath);
            _vocabulary = JsonSerializer.Deserialize<VocabularyData>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            _lastLoadTime = DateTime.UtcNow;
            _logger.LogInformation("✅ Loaded vocabulary: {Words} words, {Phrases} phrases, {Examples} examples",
                _vocabulary?.CommonWords?.Count ?? 0,
                _vocabulary?.Phrases?.Count ?? 0,
                _vocabulary?.CustomExamples?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load vocabulary.json, using defaults");
            _vocabulary = GetDefaultVocabulary();
        }
    }

    public void ReloadVocabulary()
    {
        _logger.LogInformation("Reloading vocabulary...");
        LoadVocabulary();
    }

    public string BuildVocabularyHints(string? inputText = null, int maxWords = 15)
    {
        if (_vocabulary?.CommonWords == null || _vocabulary.CommonWords.Count == 0)
            return string.Empty;

        var hints = new List<string> { "Key vocabulary:" };

        // Smart selection: if we have input text, only include relevant words
        IEnumerable<KeyValuePair<string, string>> selectedWords;

        if (!string.IsNullOrWhiteSpace(inputText))
        {
            var inputLower = inputText.ToLower();

            // Only include words that appear in the input text
            var relevantWords = _vocabulary.CommonWords
                .Where(kvp => inputLower.Contains(kvp.Key.ToLower()))
                .Take(maxWords);

            // If we found relevant words, use them
            if (relevantWords.Any())
            {
                selectedWords = relevantWords;
                _logger.LogDebug("Using {Count} relevant vocabulary words", relevantWords.Count());
            }
            else
            {
                // Fallback: use most common words
                selectedWords = _vocabulary.CommonWords.Take(maxWords);
                _logger.LogDebug("No matching words, using top {Count} general words", maxWords);
            }
        }
        else
        {
            // No input text, use general high-frequency words
            selectedWords = _vocabulary.CommonWords.Take(maxWords);
        }

        var wordsList = selectedWords.Select(kvp => $"- {kvp.Key} = {kvp.Value}");
        hints.AddRange(wordsList);

        return string.Join("\n", hints);
    }

    public string BuildFewShotExamples(string? inputText = null, int maxExamples = 4)
    {
        var examples = new List<string>();

        // Smart selection: prioritize examples similar to input
        List<PhraseExample> selectedExamples = new();

        if (!string.IsNullOrWhiteSpace(inputText))
        {
            var inputLower = inputText.ToLower();
            var inputWords = inputLower.Split(new[] { ' ', '\n', '\r', '\t' },
                StringSplitOptions.RemoveEmptyEntries);

            // Score examples by word overlap
            var scoredExamples = new List<(PhraseExample example, int score)>();

            // Check custom examples first (highest priority)
            if (_vocabulary?.CustomExamples != null)
            {
                foreach (var example in _vocabulary.CustomExamples)
                {
                    var exampleWords = example.RomanUrdu.ToLower().Split(
                        new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    var score = inputWords.Count(w => exampleWords.Contains(w));
                    scoredExamples.Add((example, score + 10)); // Bonus for custom
                }
            }

            // Then check phrases
            if (_vocabulary?.Phrases != null)
            {
                foreach (var phrase in _vocabulary.Phrases)
                {
                    var phraseWords = phrase.RomanUrdu.ToLower().Split(
                        new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    var score = inputWords.Count(w => phraseWords.Contains(w));
                    scoredExamples.Add((phrase, score));
                }
            }

            // Take top matches
            selectedExamples = scoredExamples
                .OrderByDescending(x => x.score)
                .Take(maxExamples)
                .Select(x => x.example)
                .ToList();

            _logger.LogDebug("Selected {Count} relevant examples based on input", selectedExamples.Count);
        }
        else
        {
            // No input - use general examples
            if (_vocabulary?.CustomExamples != null)
                selectedExamples.AddRange(_vocabulary.CustomExamples.Take(2));

            if (_vocabulary?.Phrases != null)
                selectedExamples.AddRange(_vocabulary.Phrases.Take(maxExamples - selectedExamples.Count));
        }

        // Build example strings
        foreach (var example in selectedExamples.Take(maxExamples))
        {
            examples.Add($"Roman Urdu: {example.RomanUrdu}\nEnglish: {example.English}");
        }

        return examples.Count > 0
            ? string.Join("\n\n", examples)
            : GetDefaultExamples();
    }

    public Dictionary<string, string> GetCommonWords()
    {
        return _vocabulary?.CommonWords ?? new Dictionary<string, string>();
    }

    public VocabularyStats GetStats()
    {
        return new VocabularyStats
        {
            TotalWords = _vocabulary?.CommonWords?.Count ?? 0,
            TotalPhrases = _vocabulary?.Phrases?.Count ?? 0,
            TotalExamples = _vocabulary?.CustomExamples?.Count ?? 0,
            LastLoaded = _lastLoadTime,
            FilePath = _vocabularyPath,
            FileExists = File.Exists(_vocabularyPath)
        };
    }

    private VocabularyData GetDefaultVocabulary()
    {
        return new VocabularyData
        {
            CommonWords = new Dictionary<string, string>
            {
                { "dukhan", "shop" },
                { "dahi", "yogurt" },
                { "ja", "go" },
                { "lao", "bring" }
            },
            Phrases = new List<PhraseExample>
            {
                new() { RomanUrdu = "Main ja raha hoon", English = "I am going" }
            },
            CustomExamples = new List<PhraseExample>()
        };
    }

    private string GetDefaultExamples()
    {
        return @"Roman Urdu: Main market ja raha hoon
English: I am going to the market

Roman Urdu: Dukhan se doodh le aao
English: Bring milk from the shop";
    }
}

public class VocabularyData
{
    [JsonPropertyName("commonWords")]
    public Dictionary<string, string> CommonWords { get; set; } = new();

    [JsonPropertyName("highFrequencyWords")]
    public HighFrequencyWords? HighFrequencyWords { get; set; }

    [JsonPropertyName("phrases")]
    public List<PhraseExample> Phrases { get; set; } = new();

    [JsonPropertyName("customExamples")]
    public List<PhraseExample> CustomExamples { get; set; } = new();

    [JsonPropertyName("notes")]
    public List<string>? Notes { get; set; }
}

public class HighFrequencyWords
{
    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("words")]
    public List<string> Words { get; set; } = new();
}

public class PhraseExample
{
    [JsonPropertyName("romanUrdu")]
    public string RomanUrdu { get; set; } = "";

    [JsonPropertyName("english")]
    public string English { get; set; } = "";
}

public record VocabularyStats
{
    public int TotalWords { get; init; }
    public int TotalPhrases { get; init; }
    public int TotalExamples { get; init; }
    public DateTime LastLoaded { get; init; }
    public string FilePath { get; init; } = "";
    public bool FileExists { get; init; }
}