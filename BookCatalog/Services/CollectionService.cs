using BookCatalog.Models;
using Supabase.Postgrest;

namespace BookCatalog.Services;

public class CollectionService
{
    private readonly Supabase.Client _client;
    private readonly BookService _bookService;

    public CollectionService(SupabaseService supabaseService, BookService bookService)
    {
        _client = supabaseService.Client;
        _bookService = bookService;
    }

    public async Task<List<BookCollection>> GetAllAsync()
    {
        var result = await _client.From<BookCollection>()
            .Order("name", Constants.Ordering.Ascending)
            .Get();
        return result.Models;
    }

    public async Task<BookCollection?> GetByIdAsync(Guid id)
    {
        return await _client.From<BookCollection>()
            .Filter("id", Constants.Operator.Equals, id.ToString())
            .Single();
    }

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
