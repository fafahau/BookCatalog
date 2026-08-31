using BookCatalog.Models;
using Microsoft.JSInterop;

namespace BookCatalog.Services;

/// <summary>
/// Keeps a local copy of the whole catalogue so it stays browsable with no
/// network (typical case: standing in a bookshop with no signal). While online
/// it refreshes the snapshot from Supabase; while offline the read services fall
/// back to it, applying the same filtering / sorting in memory.
///
/// Offline mode is read-only — adding or editing books still needs a connection.
/// </summary>
public sealed class OfflineLibraryService
{
    // Opportunistic refreshes (page loads) are skipped if the snapshot is fresher
    // than this; an explicit "Synchroniser" always forces a pull.
    private static readonly TimeSpan RefreshThrottle = TimeSpan.FromMinutes(3);

    private readonly Supabase.Client _client;
    private readonly OfflineLibraryStore _store;
    private readonly IJSInProcessRuntime _js;

    private LibrarySnapshot? _snapshot;
    private DotNetObjectReference<OfflineLibraryService>? _selfRef;
    private bool _refreshing;

    public OfflineLibraryService(SupabaseService supabaseService, OfflineLibraryStore store, IJSInProcessRuntime js)
    {
        _client = supabaseService.Client;
        _store = store;
        _js = js;
        _snapshot = _store.LoadSnapshot();
    }

    /// <summary>Raised when connectivity flips or the snapshot is refreshed. May fire off the render thread.</summary>
    public event Action? Changed;

    public bool IsOnline { get; private set; } = true;

    public bool HasSnapshot => _snapshot is { Books.Count: > 0 };

    public DateTime? SyncedAt => _snapshot?.SyncedAt;

    /// <summary>Reads the current connectivity and subscribes to browser online/offline events.</summary>
    public void Initialize()
    {
        try
        {
            IsOnline = _js.Invoke<bool>("bookcatalogConnectivity.isOnline");
            _selfRef ??= DotNetObjectReference.Create(this);
            _js.InvokeVoid("bookcatalogConnectivity.register", _selfRef);
        }
        catch
        {
            IsOnline = true;
        }
    }

    [JSInvokable]
    public void OnConnectivityChanged(bool online)
    {
        if (IsOnline == online)
        {
            return;
        }

        IsOnline = online;
        Changed?.Invoke();

        if (online)
        {
            _ = RefreshAsync();
        }
    }

    /// <summary>
    /// Pulls the whole catalogue into the local snapshot. No-op when offline, when
    /// a pull is already running, or (unless <paramref name="force"/>) when the
    /// snapshot is still fresh. Returns true if a new snapshot was stored.
    /// </summary>
    public async Task<bool> RefreshAsync(bool force = false)
    {
        if (_refreshing || !IsOnline)
        {
            return false;
        }

        if (!force && _snapshot != null && DateTime.UtcNow - _snapshot.SyncedAt < RefreshThrottle)
        {
            return false;
        }

        _refreshing = true;
        try
        {
            var collections = (await _client.From<BookCollection>().Get()).Models;
            var books = (await _client.From<Book>().Get()).Models;
            var labels = (await _client.From<Label>().Get()).Models;
            var links = (await _client.From<BookLabel>().Get()).Models;

            var nameById = labels.ToDictionary(l => l.Id, l => l.Name);
            var namesByBook = links
                .GroupBy(l => l.BookId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => nameById.GetValueOrDefault(x.LabelId))
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Select(n => n!)
                        .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                        .ToList());

            var snapshot = new LibrarySnapshot
            {
                SyncedAt = DateTime.UtcNow,
                Collections = collections.Select(SnapshotCollection.From).ToList(),
                Books = books
                    .Select(b => SnapshotBook.From(b, namesByBook.GetValueOrDefault(b.Id) ?? new List<string>()))
                    .ToList(),
                Labels = labels
                    .Select(l => l.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                    .ToList(),
            };

            _snapshot = snapshot;
            _store.SaveSnapshot(snapshot);
            Changed?.Invoke();
            return true;
        }
        catch
        {
            // Offline mid-pull, or a transient failure — keep whatever snapshot we had.
            return false;
        }
        finally
        {
            _refreshing = false;
        }
    }

    // --- Offline read fallbacks (mirror the server-side queries) ------------------

    public List<BookCollection> Collections() =>
        _snapshot?.Collections
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => c.ToModel())
            .ToList()
        ?? new();

    public BookCollection? Collection(Guid id) =>
        _snapshot?.Collections.FirstOrDefault(c => c.Id == id)?.ToModel();

    public Dictionary<Guid, int> BookCounts() =>
        _snapshot?.Books
            .GroupBy(b => b.CollectionId)
            .ToDictionary(g => g.Key, g => g.Count())
        ?? new();

    public List<string> LabelNames() => _snapshot?.Labels.ToList() ?? new();

    public Book? Book(Guid id) => _snapshot?.Books.FirstOrDefault(b => b.Id == id)?.ToModel();

    public List<DateTime> CreatedAtInCollection(Guid collectionId) =>
        _snapshot?.Books.Where(b => b.CollectionId == collectionId).Select(b => b.CreatedAt).ToList()
        ?? new();

    public List<Book> BooksInCollection(Guid collectionId, BookFilter? filter)
    {
        if (_snapshot == null)
        {
            return new();
        }

        IEnumerable<SnapshotBook> query = _snapshot.Books.Where(b => b.CollectionId == collectionId);

        if (!string.IsNullOrWhiteSpace(filter?.Title))
        {
            var term = filter.Title.Trim();
            query = query.Where(b => b.Title.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter?.Author))
        {
            var term = filter.Author.Trim();
            query = query.Where(b => b.Author.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        query = (filter?.Sort ?? BookSort.Recent) switch
        {
            BookSort.TitleAsc => query.OrderBy(b => b.Title, StringComparer.CurrentCultureIgnoreCase),
            BookSort.TitleDesc => query.OrderByDescending(b => b.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => query.OrderByDescending(b => b.CreatedAt),
        };

        var books = query.Select(b => b.ToModel()).ToList();

        if (!string.IsNullOrWhiteSpace(filter?.Label))
        {
            var wanted = filter.Label.Trim();
            books = books
                .Where(b => b.LabelNames.Any(n => n.Equals(wanted, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        return books;
    }

    public List<Book> Search(string? title, string? author)
    {
        if (_snapshot == null)
        {
            return new();
        }

        var titleTerm = title?.Trim();
        var authorTerm = author?.Trim();
        if (string.IsNullOrWhiteSpace(titleTerm) && string.IsNullOrWhiteSpace(authorTerm))
        {
            return new();
        }

        return _snapshot.Books
            .Where(b => string.IsNullOrWhiteSpace(titleTerm)
                        || b.Title.Contains(titleTerm, StringComparison.OrdinalIgnoreCase))
            .Where(b => string.IsNullOrWhiteSpace(authorTerm)
                        || b.Author.Contains(authorTerm, StringComparison.OrdinalIgnoreCase))
            .OrderBy(b => b.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(b => b.ToModel())
            .ToList();
    }

    public List<Book> SearchByIsbn(string isbn)
    {
        var normalized = BookService.NormalizeIsbn(isbn);
        if (_snapshot == null || normalized.Length < 8)
        {
            return new();
        }

        return _snapshot.Books
            .Where(b => BookService.NormalizeIsbn(b.Isbn) == normalized)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => b.ToModel())
            .ToList();
    }

    public List<Book> SearchByLabel(string label)
    {
        var wanted = label?.Trim();
        if (_snapshot == null || string.IsNullOrWhiteSpace(wanted))
        {
            return new();
        }

        return _snapshot.Books
            .Where(b => b.LabelNames.Any(n => n.Equals(wanted, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(b => b.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(b => b.ToModel())
            .ToList();
    }

    public List<Book> BooksWithoutIsbn() =>
        _snapshot?.Books
            .Where(b => BookService.NormalizeIsbn(b.Isbn).Length == 0)
            .OrderBy(b => b.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(b => b.ToModel())
            .ToList()
        ?? new();
}
