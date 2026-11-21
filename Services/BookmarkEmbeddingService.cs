using System.Buffers;
using System.Text;
using LLama.Common;
using WriteCraftAI.Models;

namespace WriteCraftAI.Services;

public class BookmarkEmbeddingService
{
    private readonly LlamaModelService _modelService;
    private readonly ILogger<BookmarkEmbeddingService> _logger;
    private readonly InferenceService _inferenceService;
    public const int EmbeddingDimensions = 384; // all-MiniLM-L6-v2 dimensions

    public BookmarkEmbeddingService(
        LlamaModelService modelService, 
        ILogger<BookmarkEmbeddingService> logger,
        InferenceService inferenceService)
    {
        _modelService = modelService;
        _logger = logger;
        _inferenceService = inferenceService;
    }

    // Note: Embeddings are now generated client-side using all-MiniLM-L6-v2
    // This method is kept for potential server-side query embedding, but currently not used
    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        // Placeholder - client-side handles embedding generation
        _logger.LogWarning("Server-side embedding generation is deprecated. Using client-side all-MiniLM-L6-v2");
        return new float[384]; // all-MiniLM-L6-v2 has 384 dimensions
    }

    public async Task<BookmarkEmbedding> GenerateBookmarkEmbeddingAsync(Bookmark bookmark)
    {
        var combinedText = $"{bookmark.Title} {bookmark.Url} {bookmark.FolderPath}";
        var embedding = await GenerateEmbeddingAsync(combinedText);
        
        return new BookmarkEmbedding
        {
            BookmarkId = bookmark.Id,
            Embedding = ConvertToBytes(embedding),
            LastModified = DateTime.UtcNow
        };
    }

    public static byte[] ConvertToBytes(float[] floats)
    {
        var bytes = new byte[floats.Length * 4];
        for (int i = 0; i < floats.Length; i++)
        {
            BitConverter.GetBytes(floats[i]).CopyTo(bytes, i * 4);
        }
        return bytes;
    }

    public static float[] ConvertFromBytes(byte[] bytes)
    {
        var floats = new float[bytes.Length / 4];
        for (int i = 0; i < floats.Length; i++)
        {
            floats[i] = BitConverter.ToSingle(bytes, i * 4);
        }
        return floats;
    }

    public static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0.0;

        double dot = 0.0;
        double normA = 0.0;
        double normB = 0.0;

        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0.0 || normB == 0.0)
            return 0.0;

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}