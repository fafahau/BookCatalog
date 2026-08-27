using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BookCatalog.Services;

public class IsbnLookupResult
{
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? CoverUrl { get; set; }

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Title)
        && !string.IsNullOrWhiteSpace(Author)
        && !string.IsNullOrWhiteSpace(CoverUrl);
}

/// <summary>
/// Looks up book metadata by ISBN, querying several free sources in order of
/// usefulness for a French catalogue: the BnF (Bibliothèque nationale de France)
/// SRU API first, then Open Library, then Google Books. Values from earlier
/// sources win; later sources only fill the gaps.
/// </summary>
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

        var sources = new Func<string, Task<IsbnLookupResult?>>[]
        {
            LookupBnfAsync,
            LookupOpenLibraryAsync,
            LookupGoogleBooksAsync,
        };

        IsbnLookupResult? merged = null;

        foreach (var source in sources)
        {
            IsbnLookupResult? part;
            try
            {
                part = await source(isbn);
            }
            catch
            {
                part = null;
            }

            if (part == null)
            {
                continue;
            }

            if (merged == null)
            {
                merged = part;
            }
            else
            {
                merged.Title ??= part.Title;
                if (string.IsNullOrWhiteSpace(merged.Author))
                {
                    merged.Author = part.Author;
                }
                merged.CoverUrl ??= part.CoverUrl;
            }

            if (merged.IsComplete)
            {
                break;
            }
        }

        // BnF has no cover thumbnails: try the Open Library cover-by-ISBN
        // endpoint as a last resort (404s when unknown, handled downstream).
        if (merged != null && !string.IsNullOrWhiteSpace(merged.Title)
            && string.IsNullOrWhiteSpace(merged.CoverUrl))
        {
            merged.CoverUrl = $"https://covers.openlibrary.org/b/isbn/{Uri.EscapeDataString(isbn)}-L.jpg?default=false";
        }

        return merged;
    }

    private async Task<IsbnLookupResult?> LookupBnfAsync(string isbn)
    {
        var query = Uri.EscapeDataString($"bib.isbn all \"{isbn}\"");
        var url = "https://catalogue.bnf.fr/api/SRU?version=1.2&operation=searchRetrieve"
                  + $"&recordSchema=dublincore&maximumRecords=1&query={query}";

        string xml;
        try
        {
            using var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            xml = await response.Content.ReadAsStringAsync();
        }
        catch
        {
            return null;
        }

        try
        {
            var doc = XDocument.Parse(xml);

            var titles = doc.Descendants()
                .Where(e => e.Name.LocalName == "title")
                .Select(e => e.Value.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            var creators = doc.Descendants()
                .Where(e => e.Name.LocalName == "creator" || e.Name.LocalName == "contributor")
                .Select(e => CleanBnfCreator(e.Value))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct()
                .ToList();

            if (titles.Count == 0 && creators.Count == 0)
            {
                return null;
            }

            // The dublincore record carries the notice's ark; the BnF cover
            // service can serve a jacket image from it (often sourced from Electre).
            var ark = doc.Descendants()
                .Where(e => e.Name.LocalName == "identifier")
                .Select(e => e.Value)
                .Select(v => Regex.Match(v, @"ark:/12148/[a-z0-9]+"))
                .FirstOrDefault(m => m.Success)?.Value;

            return new IsbnLookupResult
            {
                Title = titles.FirstOrDefault(),
                Author = creators.Count > 0 ? string.Join(", ", creators) : null,
                CoverUrl = ark != null
                    ? $"https://catalogue.bnf.fr/couverture?&appName=NE&idArk={ark}&couverture=1"
                    : null,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Normalises a BnF <c>dc:creator</c> such as
    /// "Beaumont, Émilie (1948-....). Auteur du texte" into "Émilie Beaumont".
    /// </summary>
    private static string CleanBnfCreator(string raw)
    {
        var s = Regex.Replace(raw, @"\s*\([^)]*\)", "").Trim();

        var role = s.IndexOf(". ", StringComparison.Ordinal);
        if (role > 0)
        {
            s = s[..role];
        }

        s = s.Trim().Trim('.', ',').Trim();

        var comma = s.IndexOf(", ", StringComparison.Ordinal);
        if (comma > 0)
        {
            s = (s[(comma + 2)..] + " " + s[..comma]).Trim();
        }

        return s;
    }

    private async Task<IsbnLookupResult?> LookupOpenLibraryAsync(string isbn)
    {
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

    private async Task<IsbnLookupResult?> LookupGoogleBooksAsync(string isbn)
    {
        var url = $"https://www.googleapis.com/books/v1/volumes?q=isbn:{Uri.EscapeDataString(isbn)}";

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
            if (!doc.RootElement.TryGetProperty("items", out var itemsEl)
                || itemsEl.ValueKind != JsonValueKind.Array
                || itemsEl.GetArrayLength() == 0)
            {
                return null;
            }

            if (!itemsEl[0].TryGetProperty("volumeInfo", out var info))
            {
                return null;
            }

            var result = new IsbnLookupResult();

            if (info.TryGetProperty("title", out var titleEl))
            {
                result.Title = titleEl.GetString();
            }

            if (info.TryGetProperty("authors", out var authorsEl) && authorsEl.ValueKind == JsonValueKind.Array)
            {
                var names = authorsEl.EnumerateArray()
                    .Select(a => a.GetString())
                    .Where(n => !string.IsNullOrWhiteSpace(n));
                result.Author = string.Join(", ", names);
            }

            if (info.TryGetProperty("imageLinks", out var imagesEl))
            {
                string? cover = null;
                if (imagesEl.TryGetProperty("thumbnail", out var thumb))
                {
                    cover = thumb.GetString();
                }
                else if (imagesEl.TryGetProperty("smallThumbnail", out var smallThumb))
                {
                    cover = smallThumb.GetString();
                }

                // Google serves these over http and with a curl page fold; normalise.
                if (!string.IsNullOrWhiteSpace(cover))
                {
                    result.CoverUrl = cover!
                        .Replace("http://", "https://")
                        .Replace("&edge=curl", "");
                }
            }

            return string.IsNullOrWhiteSpace(result.Title)
                && string.IsNullOrWhiteSpace(result.Author)
                && string.IsNullOrWhiteSpace(result.CoverUrl)
                ? null
                : result;
        }
    }
}
