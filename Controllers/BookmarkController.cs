using Microsoft.AspNetCore.Mvc;
using WriteCraftAI.Models;
using WriteCraftAI.Services;

namespace WriteCraftAI.Controllers;

[Route("api/bookmarks")]
public class BookmarkController : Controller
{
    private readonly BookmarkService _bookmarkService;
    private readonly ILogger<BookmarkController> _logger;

    public BookmarkController(BookmarkService bookmarkService, ILogger<BookmarkController> logger)
    {
        _bookmarkService = bookmarkService;
        _logger = logger;
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Sync()
    {
        try
        {
            _logger.LogInformation("Starting bookmark sync...");
            var count = await _bookmarkService.SyncBookmarksAsync();
            _logger.LogInformation("Bookmark sync completed. Total bookmarks synced: {Count}", count);

            return Ok(new { count, status = "Sync completed", lastSync = DateTime.Now });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bookmark sync failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("/bookmarks")]
    public IActionResult Index()
    {
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var bookmarks = await _bookmarkService.GetAllBookmarks();
        return Ok(new { bookmarks });
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var bookmarks = await _bookmarkService.GetAllBookmarks();
        return Ok(new
        {
            count = bookmarks.Count,
            browsers = bookmarks.GroupBy(b => b.Browser).ToDictionary(g => g.Key, g => g.Count())
        });
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] int topK = 10)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest("Query parameter 'q' is required");
        }

        try
        {
            var results = await _bookmarkService.SemanticSearchAsync(q, topK);
            return Ok(new { query = q, results });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("embeddings")]
    public async Task<IActionResult> StoreEmbeddings([FromBody] List<BookmarkEmbedding> embeddings)
    {
        if (embeddings == null)
        {
            return BadRequest("Embeddings data is required");
        }

        try
        {
            foreach (var embedding in embeddings)
            {
                if (embedding?.BookmarkId == null || embedding.Embedding == null)
                {
                    continue; // Skip invalid entries
                }
                await _bookmarkService.StoreEmbeddingAsync(embedding.BookmarkId, embedding.Embedding);
            }
            return Ok(new { stored = embeddings.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store embeddings");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}