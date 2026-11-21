using System;
using System.Data.SQLite;
using WriteCraftAI.Models;
using WriteCraftAI.Services;

namespace WriteCraftAI.Data;

public class BookmarkVectorDb : IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<BookmarkVectorDb> _logger;

    public BookmarkVectorDb(IWebHostEnvironment env, ILogger<BookmarkVectorDb> logger)
    {
        var dbPath = Path.Combine(env.ContentRootPath, "bookmarks.db");
        _connectionString = $"Data Source={dbPath};Version=3;Foreign Keys=True;";
        _logger = logger;
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Bookmarks (
                Id TEXT PRIMARY KEY,
                Url TEXT NOT NULL,
                Title TEXT NOT NULL,
                FolderPath TEXT NOT NULL,
                DateAdded TEXT NOT NULL,
                VisitCount INTEGER NOT NULL,
                Browser TEXT NOT NULL,
                Category TEXT,
                Tags TEXT
            );

            CREATE TABLE IF NOT EXISTS BookmarkEmbeddings (
                BookmarkId TEXT PRIMARY KEY,
                Embedding BLOB NOT NULL,
                LastModified TEXT NOT NULL,
                FOREIGN KEY (BookmarkId) REFERENCES Bookmarks(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_bookmarks_url ON Bookmarks(Url);
            CREATE INDEX IF NOT EXISTS idx_bookmarks_title ON Bookmarks(Title);
            CREATE INDEX IF NOT EXISTS idx_embeddings_bookmark_id ON BookmarkEmbeddings(BookmarkId);
        ";
        command.ExecuteNonQuery();
    }

    public async Task SyncBookmarksAsync(List<Bookmark> bookmarks)
    {
        using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();

        using var transaction = connection.BeginTransaction();
        try
        {
            // Clear existing bookmarks
            var deleteCmd = connection.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM Bookmarks";
            deleteCmd.Transaction = transaction;
            await deleteCmd.ExecuteNonQueryAsync();

            // Insert new bookmarks
            foreach (var bookmark in bookmarks)
            {
                var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO Bookmarks (Id, Url, Title, FolderPath, DateAdded, VisitCount, Browser, Category, Tags)
                    VALUES (@id, @url, @title, @folderPath, @dateAdded, @visitCount, @browser, @category, @tags)
                ";
                insertCmd.Parameters.Add(new SQLiteParameter("@id", bookmark.Id));
                insertCmd.Parameters.Add(new SQLiteParameter("@url", bookmark.Url));
                insertCmd.Parameters.Add(new SQLiteParameter("@title", bookmark.Title));
                insertCmd.Parameters.Add(new SQLiteParameter("@folderPath", bookmark.FolderPath));
                insertCmd.Parameters.Add(new SQLiteParameter("@dateAdded", bookmark.DateAdded.ToString("O")));
                insertCmd.Parameters.Add(new SQLiteParameter("@visitCount", bookmark.VisitCount));
                insertCmd.Parameters.Add(new SQLiteParameter("@browser", bookmark.Browser));
                insertCmd.Parameters.Add(new SQLiteParameter("@category", bookmark.Category ?? string.Empty));
                insertCmd.Parameters.Add(new SQLiteParameter("@tags", bookmark.Tags ?? string.Empty));
                insertCmd.Transaction = transaction;
                await insertCmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            _logger.LogInformation("Synced {Count} bookmarks to database", bookmarks.Count);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to sync bookmarks");
            throw;
        }
    }

    public async Task<List<Bookmark>> GetAllBookmarksAsync()
    {
        using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, Url, Title, FolderPath, DateAdded, VisitCount, Browser, Category, Tags
            FROM Bookmarks
        ";

        var bookmarks = new List<Bookmark>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            bookmarks.Add(new Bookmark
            {
                Id = reader["Id"]?.ToString() ?? string.Empty,
                Url = reader["Url"]?.ToString() ?? string.Empty,
                Title = reader["Title"]?.ToString() ?? string.Empty,
                FolderPath = reader["FolderPath"]?.ToString() ?? string.Empty,
                DateAdded = DateTime.TryParse(reader["DateAdded"]?.ToString() ?? string.Empty, out var dt) ? dt : DateTime.MinValue,
                VisitCount = reader["VisitCount"] != DBNull.Value ? Convert.ToInt64(reader["VisitCount"]) : 0,
                Browser = reader["Browser"]?.ToString() ?? string.Empty,
                Category = reader["Category"]?.ToString(),
                Tags = reader["Tags"]?.ToString()
            });
        }

        return bookmarks;
    }



    public async Task StoreEmbeddingAsync(string bookmarkId, byte[] embedding)
    {
        using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT OR REPLACE INTO BookmarkEmbeddings (BookmarkId, Embedding, LastModified)
            VALUES (@bookmarkId, @embedding, @lastModified)
        ";
        command.Parameters.Add(new SQLiteParameter("@bookmarkId", bookmarkId));
        command.Parameters.Add(new SQLiteParameter("@embedding", embedding));
        command.Parameters.Add(new SQLiteParameter("@lastModified", DateTime.UtcNow.ToString("O")));

        await command.ExecuteNonQueryAsync();
    }

    public async Task<List<(string BookmarkId, float[] Embedding)>> GetAllEmbeddingsAsync()
    {
        using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT BookmarkId, Embedding FROM BookmarkEmbeddings";

        var embeddings = new List<(string BookmarkId, float[] Embedding)>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var bookmarkId = reader["BookmarkId"]?.ToString() ?? string.Empty;
            var embeddingBytes = (byte[])reader["Embedding"];
            var embedding = BookmarkEmbeddingService.ConvertFromBytes(embeddingBytes);
            embeddings.Add((bookmarkId, embedding));
        }

        return embeddings;
    }

    public async Task<List<Bookmark>> SemanticSearchAsync(float[] queryEmbedding, int topK = 10)
    {
        var allEmbeddings = await GetAllEmbeddingsAsync();
        var similarities = new List<(string BookmarkId, float Similarity)>();

        foreach (var (bookmarkId, embedding) in allEmbeddings)
        {
            var similarity = CosineSimilarity(queryEmbedding, embedding);
            similarities.Add((bookmarkId, similarity));
        }

        var topBookmarks = similarities
            .OrderByDescending(s => s.Similarity)
            .Take(topK)
            .Select(s => s.BookmarkId)
            .ToList();

        var allBookmarks = await GetAllBookmarksAsync();
        return allBookmarks.Where(b => topBookmarks.Contains(b.Id)).ToList();
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;

        float dotProduct = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return dotProduct / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }

    public void Dispose() => _logger.LogDebug("Bookmark database disposed");
}