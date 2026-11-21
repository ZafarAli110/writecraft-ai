using WriteCraftAI.Data;
using WriteCraftAI.Models;
using WriteCraftAI.Services;

namespace WriteCraftAI.Services;

public class BookmarkService
{
    private readonly BookmarkParserService _parserService;
    private readonly BookmarkVectorDb _vectorDb;
    private readonly BookmarkEmbeddingService _embeddingService;
    private readonly ILogger<BookmarkService> _logger;

    public BookmarkService(
        BookmarkParserService parserService,
        BookmarkVectorDb vectorDb,
        BookmarkEmbeddingService embeddingService,
        ILogger<BookmarkService> logger)
    {
        _parserService = parserService;
        _vectorDb = vectorDb;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    public async Task<int> SyncBookmarksAsync()
    {
        var bookmarks = await _parserService.ParseAllBrowsersAsync();
        await _vectorDb.SyncBookmarksAsync(bookmarks);

        // Generate embeddings for all bookmarks
        _logger.LogInformation("Generating embeddings for {Count} bookmarks", bookmarks.Count);
        foreach (var bookmark in bookmarks)
        {
            try
            {
                var embedding = await _embeddingService.GenerateBookmarkEmbeddingAsync(bookmark);
                await _vectorDb.StoreEmbeddingAsync(bookmark.Id, embedding.Embedding);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate embedding for bookmark {Id}", bookmark.Id);
            }
        }

        return bookmarks.Count;
    }

    public async Task<List<Bookmark>> GetAllBookmarks()
    {
        return await _vectorDb.GetAllBookmarksAsync();
    }

    public async Task<List<Bookmark>> SemanticSearchAsync(string query, int topK = 10)
    {
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
        return await _vectorDb.SemanticSearchAsync(queryEmbedding, topK);
    }

    public async Task StoreEmbeddingAsync(string bookmarkId, byte[] embedding)
    {
        await _vectorDb.StoreEmbeddingAsync(bookmarkId, embedding);
    }
}