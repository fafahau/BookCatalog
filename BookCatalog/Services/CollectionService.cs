using BookCatalog.Models;
using Supabase.Postgrest;

namespace BookCatalog.Services;

public class CollectionService
{
    private readonly Supabase.Client _client;
    private readonly BookService _bookService;
    private readonly OfflineLibraryService _offline;

    public CollectionService(SupabaseService supabaseService, BookService bookService, OfflineLibraryService offline)
    {
        _client = supabaseService.Client;
        _bookService = bookService;
        _offline = offline;
    }

    private async Task<T> WithOfflineFallback<T>(Func<Task<T>> online, Func<T> offline)
    {
        if (!_offline.IsOnline && _offline.HasSnapshot)
        {
            return offline();
        }

        try
        {
            return await online();
        }
        catch when (_offline.HasSnapshot)
        {
            return offline();
        }
    }

    public Task<List<BookCollection>> GetAllAsync() =>
        WithOfflineFallback(
            async () =>
            {
                var result = await _client.From<BookCollection>()
                    .Order("name", Constants.Ordering.Ascending)
                    .Get();
                return result.Models;
            },
            () => _offline.Collections());

    /// <summary>Number of books in each collection, keyed by collection id.</summary>
    public Task<Dictionary<Guid, int>> GetBookCountsAsync() =>
        WithOfflineFallback(
            async () =>
            {
                var result = await _client.From<Book>()
                    .Select("collection_id")
                    .Get();

                return result.Models
                    .GroupBy(b => b.CollectionId)
                    .ToDictionary(g => g.Key, g => g.Count());
            },
            () => _offline.BookCounts());

    public Task<BookCollection?> GetByIdAsync(Guid id) =>
        WithOfflineFallback(
            async () => await _client.From<BookCollection>()
                .Filter("id", Constants.Operator.Equals, id.ToString())
                .Single(),
            () => _offline.Collection(id));

    public async Task<BookCollection> CreateAsync(string name, Guid createdBy)
    {
        var collection = new BookCollection { Name = name, CreatedBy = createdBy };
        var result = await _client.From<BookCollection>().Insert(collection);
        return result.Models.First();
    }

    public async Task RenameAsync(Guid id, string newName)
    {
        await _client.From<BookCollection>()
            .Filter("id", Constants.Operator.Equals, id.ToString())
            .Set(c => c.Name, newName)
            .Update();
    }

    /// <summary>Deletes every book (and its Storage photos) in the collection, then the collection itself.</summary>
    public async Task DeleteAsync(Guid id)
    {
        var books = await _bookService.GetByCollectionAsync(id);
        foreach (var book in books)
        {
            await _bookService.DeleteAsync(book);
        }

        await _client.From<BookCollection>()
            .Filter("id", Constants.Operator.Equals, id.ToString())
            .Delete();
    }
}
