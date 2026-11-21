using System;
using System.Text.Json;
using System.Data.SQLite;
using WriteCraftAI.Models;

namespace WriteCraftAI.Services;

public class BookmarkParserService
{
    private readonly ILogger<BookmarkParserService> _logger;

    public BookmarkParserService(ILogger<BookmarkParserService> logger)
    {
        _logger = logger;
    }

    public async Task<List<Bookmark>> ParseAllBrowsersAsync()
    {
        var bookmarks = new List<Bookmark>();

        _logger.LogInformation("Starting bookmark parsing for all browsers");

        // Chrome/Edge (JSON-based)
        try
        {
            _logger.LogInformation("Parsing Chrome/Edge bookmarks...");
            var chromeBookmarks = await ParseChromeBookmarksAsync();
            _logger.LogInformation("Found {Count} Chrome/Edge bookmarks", chromeBookmarks.Count);
            bookmarks.AddRange(chromeBookmarks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not parse Chrome/Edge bookmarks");
        }

        // Firefox (SQLite-based)
        try
        {
            _logger.LogInformation("Parsing Firefox bookmarks...");
            var firefoxBookmarks = await ParseFirefoxBookmarksAsync();
            _logger.LogInformation("Found {Count} Firefox bookmarks", firefoxBookmarks.Count);
            bookmarks.AddRange(firefoxBookmarks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not parse Firefox bookmarks");
        }

        // Safari (plist-based)
        try
        {
            _logger.LogInformation("Parsing Safari bookmarks...");
            var safariBookmarks = await ParseSafariBookmarksAsync();
            _logger.LogInformation("Found {Count} Safari bookmarks", safariBookmarks.Count);
            bookmarks.AddRange(safariBookmarks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not parse Safari bookmarks");
        }

        var browserCounts = bookmarks.GroupBy(b => b.Browser).ToDictionary(g => g.Key, g => g.Count());
        _logger.LogInformation("Total bookmarks parsed: {Total} from {Browsers} browsers. Breakdown: {Breakdown}",
            bookmarks.Count, browserCounts.Count, string.Join(", ", browserCounts.Select(kv => $"{kv.Key}: {kv.Value}")));

        return bookmarks;
    }

    private async Task<List<Bookmark>> ParseChromeBookmarksAsync()
    {
        var bookmarks = new List<Bookmark>();
        var profiles = new[] { "Default", "Profile 1", "Profile 2" };

        foreach (var browser in new[] { "Chrome", "Edge" })
        {
            var basePath = browser == "Chrome"
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Google", "Chrome", "User Data")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Edge", "User Data");

            _logger.LogInformation("Checking {Browser} base path: {Path}", browser, basePath);
            if (!Directory.Exists(basePath))
            {
                _logger.LogWarning("{Browser} base directory not found: {Path}", browser, basePath);
                continue;
            }

            foreach (var profile in profiles)
            {
                var bookmarkPath = Path.Combine(basePath, profile, "Bookmarks");
                _logger.LogInformation("Checking {Browser} bookmark file for profile '{Profile}': {Path}", browser, profile, bookmarkPath);
                if (!File.Exists(bookmarkPath))
                {
                    _logger.LogWarning("{Browser} bookmark file not found for profile '{Profile}': {Path}", browser, profile, bookmarkPath);
                    continue;
                }

                var json = await File.ReadAllTextAsync(bookmarkPath);
                var root = JsonDocument.Parse(json).RootElement;
                
                if (root.TryGetProperty("roots", out var roots))
                {
                    foreach (var folder in new[] { "bookmark_bar", "other" })
                    {
                        if (roots.TryGetProperty(folder, out var folderNode))
                        {
                            ParseChromeBookmarkNode(folderNode, $"Root/{folder}", browser, bookmarks);
                        }
                    }
                }
            }
        }

        return bookmarks;
    }

    private void ParseChromeBookmarkNode(JsonElement node, string path, string browser, List<Bookmark> bookmarks)
    {
        if (node.TryGetProperty("children", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                var name = child.GetProperty("name").GetString() ?? "";
                
                if (child.TryGetProperty("url", out var url))
                {
                    // This is a bookmark
                    var bookmark = new Bookmark
                    {
                        Url = url.GetString() ?? "",
                        Title = name,
                        FolderPath = path,
                        Browser = browser,
                        DateAdded = DateTime.FromFileTimeUtc(ParseLongValue(child.GetProperty("date_added"))),
                        VisitCount = child.TryGetProperty("meta_info", out var meta) &&
                                   meta.TryGetProperty("visits", out var visits)
                                   ? ParseLongValue(visits) : 0
                    };
                    
                    if (!string.IsNullOrWhiteSpace(bookmark.Url) && 
                        !string.IsNullOrWhiteSpace(bookmark.Title))
                    {
                        bookmarks.Add(bookmark);
                    }
                }
                else
                {
                    // This is a folder
                    ParseChromeBookmarkNode(child, $"{path}/{name}", browser, bookmarks);
                }
            }
        }
    }

    private async Task<List<Bookmark>> ParseFirefoxBookmarksAsync()
    {
        var bookmarks = new List<Bookmark>();
        var profilesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Mozilla", "Firefox", "Profiles");

        _logger.LogInformation("Checking Firefox profiles path: {Path}", profilesPath);
        if (!Directory.Exists(profilesPath))
        {
            _logger.LogWarning("Firefox profiles directory not found: {Path}", profilesPath);
            return bookmarks;
        }

        foreach (var profileDir in Directory.GetDirectories(profilesPath))
        {
            _logger.LogInformation("Found Firefox profile directory: {Path}", profileDir);
            var dbPath = Path.Combine(profileDir, "places.sqlite");
            _logger.LogInformation("Checking Firefox places.sqlite: {Path}", dbPath);
            if (!File.Exists(dbPath))
            {
                _logger.LogWarning("places.sqlite not found in Firefox profile: {Path}", profileDir);
                continue;
            }

            try
            {
                // Copy to avoid SQLite locking issues
                var tempDbPath = Path.GetTempFileName();
                await using (var source = File.OpenRead(dbPath))
                await using (var dest = File.OpenWrite(tempDbPath))
                {
                    await source.CopyToAsync(dest);
                }

                using var connection = new SQLiteConnection($"Data Source={tempDbPath}");
                await connection.OpenAsync();

                var command = new SQLiteCommand(@"
                    SELECT b.title, p.url, datetime(b.dateAdded/1000000, 'unixepoch') as dateAdded, p.visit_count
                    FROM moz_bookmarks b
                    JOIN moz_places p ON b.fk = p.id
                    WHERE b.type = 1 AND p.url IS NOT NULL
                    ORDER BY b.dateAdded DESC
                    LIMIT 5000", connection);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var url = reader["url"]?.ToString() ?? string.Empty;
                    var title = reader["title"]?.ToString() ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(title))
                    {
                        bookmarks.Add(new Bookmark
                        {
                            Url = url,
                            Title = title,
                            FolderPath = "Firefox Bookmarks",
                            Browser = "Firefox",
                            DateAdded = DateTime.TryParse(reader["dateAdded"]?.ToString() ?? string.Empty, out var dt) ? dt : DateTime.MinValue,
                            VisitCount = reader["visit_count"] != DBNull.Value ? Convert.ToInt64(reader["visit_count"]) : 0
                        });
                    }
                }

                File.Delete(tempDbPath);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, $"Could not parse Firefox profile: {profileDir}");
            }
        }

        return bookmarks;
    }

    private async Task<List<Bookmark>> ParseSafariBookmarksAsync()
    {
        var bookmarks = new List<Bookmark>();
        var safariPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Safari", "Bookmarks.plist");

        if (!File.Exists(safariPath)) return bookmarks;

        try
        {
            // Note: Proper plist parsing requires additional libraries like PlistCS or similar
            // For now, this is a placeholder - Safari bookmarks are in binary plist format
            // which is complex to parse without external dependencies

            _logger.LogInformation("Safari bookmarks found but parsing not implemented (requires plist library)");
            // TODO: Implement plist parsing for Safari bookmarks
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not parse Safari bookmarks");
        }

        return bookmarks;
    }

    private long ParseLongValue(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.GetInt64();
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            var str = element.GetString();
            return long.TryParse(str, out var parsed) ? parsed : 0;
        }
        else
        {
            return 0;
        }
    }
}