using System.Net.Http.Json;
using System.Text.Json;

namespace BookCatalog.Services;

public class IsbnLookupResult
{
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? CoverUrl { get; set; }
}

/// <summary>Looks up book metadata by ISBN via the free Open Library API (brief 3.3).</summary>
public class IsbnLookupService
{
    private readonly HttpClient _http;

    public IsbnLookupService(HttpClient http)
    {
        _http = http;
    }

    public async Task<IsbnLookupResult?> LookupAsync(string isbn)
    {
        isbn = isbn.Trim().Replace("-", "").Replace(" ", "");
        if (string.IsNullOrWhiteSpace(isbn))
        {
            return null;
        }

        var url = $"https://openlibrary.org/api/books?bibkeys=ISBN:{Uri.EscapeDataString(isbn)}&format=json&jscmd=data";

        JsonDocument doc;
        try
        {
            using var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            doc = await response.Content.ReadFromJsonAsync<JsonDocument>() ?? JsonDocument.Parse("{}");
        }
        catch
        {
            return null;
        }

        using (doc)
        {
            var key = $"ISBN:{isbn}";
            if (!doc.RootElement.TryGetProperty(key, out var entry))
            {
                return null;
            }

            var result = new IsbnLookupResult();

            if (entry.TryGetProperty("title", out var titleEl))
            {
                result.Title = titleEl.GetString();
            }

            if (entry.TryGetProperty("authors", out var authorsEl) && authorsEl.ValueKind == JsonValueKind.Array)
            {
                var names = authorsEl.EnumerateArray()
                    .Select(a => a.TryGetProperty("name", out var n) ? n.GetString() : null)
                    .Where(n => !string.IsNullOrWhiteSpace(n));
                result.Author = string.Join(", ", names);
            }

            if (entry.TryGetProperty("cover", out var coverEl))
            {
                if (coverEl.TryGetProperty("large", out var large))
                {
                    result.CoverUrl = large.GetString();
                }
                else if (coverEl.TryGetProperty("medium", out var medium))
                {
                    result.CoverUrl = medium.GetString();
                }
            }

            return result;
        }
    }
}
