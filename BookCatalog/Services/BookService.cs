using BookCatalog.Models;
using Supabase.Postgrest;

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
