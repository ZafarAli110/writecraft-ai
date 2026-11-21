using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using WriteCraftAI.Services;
using Xunit;

namespace WriteCraftAI.Tests;

internal sealed class StubWebHostEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "WriteCraftAI.Tests";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = "Testing";
    public string ContentRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

    public StubWebHostEnvironment(string contentRootPath)
    {
        ContentRootPath = contentRootPath;
    }
}

public class BookmarkEmbeddingServiceTests
{
    [Fact]
    public void ConvertToBytes_And_ConvertFromBytes_RoundTrips384DimVector()
    {
        var original = new float[BookmarkEmbeddingService.EmbeddingDimensions];
        for (int i = 0; i < original.Length; i++)
        {
            original[i] = (i - 192) * 0.005f;
        }

        var bytes = BookmarkEmbeddingService.ConvertToBytes(original);
        var restored = BookmarkEmbeddingService.ConvertFromBytes(bytes);

        Assert.Equal(original.Length * 4, bytes.Length);
        Assert.Equal(original.Length, restored.Length);
        for (int i = 0; i < original.Length; i++)
        {
            Assert.Equal(original[i], restored[i], precision: 5);
        }
    }

    [Fact]
    public void CosineSimilarity_ComputesIdenticalOrthogonalOppositeAndZeroVectors()
    {
        Assert.Equal(1.0, BookmarkEmbeddingService.CosineSimilarity([1f, 2f, 3f], [2f, 4f, 6f]), precision: 5);
        Assert.Equal(0.0, BookmarkEmbeddingService.CosineSimilarity([1f, 0f], [0f, 1f]), precision: 5);
        Assert.Equal(-1.0, BookmarkEmbeddingService.CosineSimilarity([1f, -2f], [-1f, 2f]), precision: 5);
        Assert.Equal(0.0, BookmarkEmbeddingService.CosineSimilarity([0f, 0f], [1f, 2f]), precision: 5);
        Assert.Equal(0.0, BookmarkEmbeddingService.CosineSimilarity([1f], [1f, 2f]), precision: 5);
    }
}

public class RomanUrduVocabularyServiceTests
{
    [Fact]
    public void LoadsUrduVocabularyJson_FromContentRoot_AndBuildsRelevantHints()
    {
        // Locate repo root containing urdu-vocabulary.json
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "urdu-vocabulary.json")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var env = new StubWebHostEnvironment(dir!.FullName);
        var service = new RomanUrduVocabularyService(NullLogger<RomanUrduVocabularyService>.Instance, env);

        var hints = service.BuildVocabularyHints("aaj kaam bohat zyada hai");
        var examples = service.BuildFewShotExamples("aaj kaam bohat zyada hai", maxExamples: 3);

        Assert.Contains("Key vocabulary:", hints);
        Assert.NotEmpty(examples);
    }

    [Fact]
    public void FallsBackToDefaultVocabulary_WhenJsonFileMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"writecraft-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var env = new StubWebHostEnvironment(tempDir);
            var service = new RomanUrduVocabularyService(NullLogger<RomanUrduVocabularyService>.Instance, env);

            var hints = service.BuildVocabularyHints("kal meeting hai");
            Assert.Contains("Key vocabulary:", hints);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
