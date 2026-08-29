using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using BookCatalog.Models;

namespace BookCatalog.Pages;

public partial class Search
{
    private enum SearchMode { Isbn, Title, Author, Label }

    private SearchMode _mode = SearchMode.Isbn;
    private string _query = string.Empty;
    private bool _searching;
    private bool _searched;
    private List<Hit> _results = new();
    private List<Hit> _booksWithoutIsbn = new();

    private Dictionary<Guid, string> _collectionNames = new();
    private List<string> _allLabels = new();

    private bool _scanning;
    private bool _scanStarting;
    private string? _scanError;
    private ElementReference _scanVideo;
    private DotNetObjectReference<Search>? _selfRef;

    private sealed record Hit(Book Book, string CollectionName);

    private string Placeholder => _mode switch
    {
        SearchMode.Title => "Rechercher par titre",
        SearchMode.Author => "Rechercher par auteur",
        SearchMode.Label => "Rechercher par label",
        _ => "Rechercher par ISBN"
    };

    protected override async Task OnInitializedAsync()
    {
        _selfRef = DotNetObjectReference.Create(this);
        var collections = await CollectionService.GetAllAsync();
        _collectionNames = collections.ToDictionary(c => c.Id, c => c.Name);

        try
        {
            _allLabels = await LabelService.GetNamesAsync();
        }
        catch
        {
            _allLabels = new();
        }
    }

    private async Task SetMode(SearchMode mode)
    {
        if (_mode == mode)
        {
            return;
        }

        if (_scanning)
        {
            await StopScanAsync();
        }

        _mode = mode;
        _searched = false;
        _results = new();
        _booksWithoutIsbn = new();
    }

    private async Task OnKeyUp(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await SearchAsync();
        }
    }

    private Hit ToHit(Book b) =>
        new(b, _collectionNames.TryGetValue(b.CollectionId, out var name) ? name : "Collection inconnue");

    private async Task SearchAsync()
    {
        var query = _query?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        _searching = true;
        StateHasChanged();
        try
        {
            List<Book> books = _mode switch
            {
                SearchMode.Title => await BookService.SearchAsync(query, null),
                SearchMode.Author => await BookService.SearchAsync(null, query),
                SearchMode.Label => await BookService.SearchByLabelAsync(query),
                _ => await BookService.SearchByIsbnAsync(query)
            };

            _results = books
                .Select(ToHit)
                .OrderBy(h => h.CollectionName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(h => h.Book.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            _booksWithoutIsbn = new();
            if (_mode == SearchMode.Isbn && _results.Count == 0)
            {
                var withoutIsbn = await BookService.GetBooksWithoutIsbnAsync();
                _booksWithoutIsbn = withoutIsbn
                    .Select(ToHit)
                    .OrderBy(h => h.CollectionName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(h => h.Book.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }

            _searched = true;
        }
        finally
        {
            _searching = false;
            StateHasChanged();
        }
    }

    private async Task ToggleScanAsync()
    {
        if (_scanning)
        {
            await StopScanAsync();
            return;
        }

        _scanError = null;
        _scanning = true;
        _scanStarting = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_scanning || !_scanStarting)
        {
            return;
        }

        _scanStarting = false;
        try
        {
            await JS.InvokeAsync<string>("barcodeScanner.start", _scanVideo, _selfRef);
        }
        catch (Exception ex)
        {
            _scanning = false;
            _scanError = "Impossible d'accéder à la caméra : " + ex.Message;
            StateHasChanged();
        }
    }

    [JSInvokable]
    public async Task OnBarcodeDetected(string code)
    {
        _query = code;
        await StopScanAsync();
        await SearchAsync();
    }

    private async Task StopScanAsync()
    {
        _scanning = false;
        _scanStarting = false;
        try
        {
            await JS.InvokeVoidAsync("barcodeScanner.stop");
        }
        catch
        {
            // circuit already gone / script not loaded
        }
        StateHasChanged();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("barcodeScanner.stop");
        }
        catch
        {
            // circuit already gone
        }
        _selfRef?.Dispose();
    }
}
