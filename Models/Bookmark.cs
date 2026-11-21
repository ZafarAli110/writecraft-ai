namespace WriteCraftAI.Models;

public class Bookmark
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public DateTime DateAdded { get; set; }
    public long VisitCount { get; set; }
    public string Browser { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Tags { get; set; }
}

public class BookmarkEmbedding
{
    public string BookmarkId { get; set; } = string.Empty;
    public byte[] Embedding { get; set; } = Array.Empty<byte>();
    public DateTime LastModified { get; set; }
}

public class BookmarkSyncResult
{
    public int TotalBookmarks { get; set; }
    public int NewBookmarks { get; set; }
    public int UpdatedBookmarks { get; set; }
    public string Browser { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
}