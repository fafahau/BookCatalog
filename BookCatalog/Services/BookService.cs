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
    public BookSort Sort { get; set; } = BookSort.Recent;
}

public class BookService
{
    private readonly Supabase.Client _client;
    private readonly ImageUploadService _imageUploadService;

    public BookService(SupabaseService supabaseService, ImageUploadService imageUploadService)
    {
        _client = supabaseService.Client;
        _imageUploadService = imageUploadService;
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
        return result.Models;
    }

    public async Task<Book?> GetByIdAsync(Guid id)
    {
        return await _client.From<Book>()
            .Filter("id", Constants.Operator.Equals, id.ToString())
            .Single();
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
