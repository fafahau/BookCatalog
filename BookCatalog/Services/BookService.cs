using BookCatalog.Models;
using Supabase.Postgrest;
using Supabase.Postgrest.Interfaces;

namespace BookCatalog.Services;

public enum BookSort
{
    Recent,
    TitleAsc,
    TitleDesc
}

public class BookFilter
{
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Label { get; set; }
    public BookSort Sort { get; set; } = BookSort.Recent;
}

public class BookService
{
    private readonly Supabase.Client _client;
    private readonly ImageUploadService _imageUploadService;
    private readonly LabelService _labelService;

    public BookService(SupabaseService supabaseService, ImageUploadService imageUploadService, LabelService labelService)
    {
        _client = supabaseService.Client;
        _imageUploadService = imageUploadService;
        _labelService = labelService;
    }

    public async Task<List<Book>> GetByCollectionAsync(Guid collectionId, BookFilter? filter = null)
    {
        var query = _client.From<Book>()
            .Filter("collection_id", Constants.Operator.Equals, collectionId.ToString());

        if (!string.IsNullOrWhiteSpace(filter?.Title))
        {
            query = query.Filter("title", Constants.Operator.ILike, $"%{filter.Title}%");
        }

        if (!string.IsNullOrWhiteSpace(filter?.Author))
        {
            query = query.Filter("author", Constants.Operator.ILike, $"%{filter.Author}%");
        }

        var result = (filter?.Sort ?? BookSort.Recent) switch
        {
            BookSort.TitleAsc => await query.Order("title", Constants.Ordering.Ascending).Get(),
            BookSort.TitleDesc => await query.Order("title", Constants.Ordering.Descending).Get(),
            _ => await query.Order("created_at", Constants.Ordering.Descending).Get()
        };

        var books = result.Models;
        if (books.Count > 0)
        {
            var namesByBook = await _labelService.GetNamesByBookAsync();
            foreach (var book in books)
            {
                book.LabelNames = namesByBook.GetValueOrDefault(book.Id) ?? new();
            }
        }

        if (!string.IsNullOrWhiteSpace(filter?.Label))
        {
            var wanted = filter.Label.Trim();
            books = books
                .Where(b => b.LabelNames.Any(n => n.Equals(wanted, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        return books;
    }

    /// <summary>
    /// Creation timestamps of every book in the collection. Bucketing by day is left
    /// to the caller. Used by the admin-only "calendrier des ajouts".
    /// </summary>
    public async Task<List<DateTime>> GetCreatedAtByCollectionAsync(Guid collectionId)
    {
        var result = await _client.From<Book>()
            .Select("created_at")
            .Filter("collection_id", Constants.Operator.Equals, collectionId.ToString())
            .Get();

        return result.Models.Select(b => b.CreatedAt).ToList();
    }

    /// <summary>
    /// Finds every book whose ISBN matches <paramref name="isbn"/>, across all collections,
    /// regardless of the caller's role (books are readable by any signed-in user).
    /// Comparison is done on digits only so hyphenated / scanned forms all match.
    /// </summary>
    public async Task<List<Book>> SearchByIsbnAsync(string isbn)
    {
        var normalized = NormalizeIsbn(isbn);
        if (normalized.Length < 8)
        {
            return new();
        }

        var result = await _client.From<Book>()
            .Order("created_at", Constants.Ordering.Descending)
            .Get();

        return result.Models
            .Where(b => NormalizeIsbn(b.Isbn) == normalized)
            .ToList();
    }

    /// <summary>
    /// Finds every book whose title and/or author matches, across all collections.
    /// Both terms are optional; an empty query returns nothing.
    /// </summary>
    public async Task<List<Book>> SearchAsync(string? title, string? author)
    {
        var titleTerm = title?.Trim();
        var authorTerm = author?.Trim();
        if (string.IsNullOrWhiteSpace(titleTerm) && string.IsNullOrWhiteSpace(authorTerm))
        {
            return new();
        }

        IPostgrestTable<Book> query = _client.From<Book>();

        if (!string.IsNullOrWhiteSpace(titleTerm))
        {
            query = query.Filter("title", Constants.Operator.ILike, $"%{titleTerm}%");
        }

        if (!string.IsNullOrWhiteSpace(authorTerm))
        {
            query = query.Filter("author", Constants.Operator.ILike, $"%{authorTerm}%");
        }

        var result = await query.Order("title", Constants.Ordering.Ascending).Get();
        return result.Models;
    }

    /// <summary>
    /// Finds every book carrying a label whose name matches <paramref name="label"/>
    /// (case-insensitive), across all collections. Returned books have their
    /// <see cref="Book.LabelNames"/> populated. An empty query returns nothing.
    /// </summary>
    public async Task<List<Book>> SearchByLabelAsync(string label)
    {
        var wanted = label?.Trim();
        if (string.IsNullOrWhiteSpace(wanted))
        {
            return new();
        }

        var result = await _client.From<Book>()
            .Order("title", Constants.Ordering.Ascending)
            .Get();

        var namesByBook = await _labelService.GetNamesByBookAsync();
        var books = new List<Book>();
        foreach (var book in result.Models)
        {
            var names = namesByBook.GetValueOrDefault(book.Id) ?? new();
            if (names.Any(n => n.Equals(wanted, StringComparison.OrdinalIgnoreCase)))
            {
                book.LabelNames = names;
                books.Add(book);
            }
        }

        return books;
    }

    /// <summary>Every book with no ISBN recorded, across all collections, ordered by title.</summary>
    public async Task<List<Book>> GetBooksWithoutIsbnAsync()
    {
        var result = await _client.From<Book>()
            .Order("title", Constants.Ordering.Ascending)
            .Get();

        return result.Models
            .Where(b => NormalizeIsbn(b.Isbn).Length == 0)
            .ToList();
    }

    /// <summary>Keeps digits (and a trailing ISBN-10 check "X"); drops hyphens, spaces, prefixes.</summary>
    public static string NormalizeIsbn(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return new string(raw.Where(c => char.IsDigit(c) || c is 'X' or 'x').ToArray()).ToUpperInvariant();
    }

    public async Task<List<string>> GetAuthorsAsync()
    {
        var result = await _client.From<Book>()
            .Select("author")
            .Get();

        return result.Models
            .Select(b => b.Author?.Trim() ?? string.Empty)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<Book?> GetByIdAsync(Guid id)
    {
        var book = await _client.From<Book>()
            .Filter("id", Constants.Operator.Equals, id.ToString())
            .Single();

        if (book != null)
        {
            book.LabelNames = await _labelService.GetNamesForBookAsync(book.Id);
        }

        return book;
    }

    public async Task<Book> CreateAsync(Book book)
    {
        var result = await _client.From<Book>().Insert(book);
        return result.Models.First();
    }

    public async Task UpdateAsync(Book book)
    {
        await _client.From<Book>()
            .Filter("id", Constants.Operator.Equals, book.Id.ToString())
            .Update(book);
    }

    public async Task DeleteAsync(Book book)
    {
        await _imageUploadService.DeleteBookPhotosAsync(book.CollectionId, book.Id);
        await _client.From<Book>()
            .Filter("id", Constants.Operator.Equals, book.Id.ToString())
            .Delete();
    }
}
