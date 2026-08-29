using Microsoft.AspNetCore.Components;
using BookCatalog.Models;
using BookCatalog.Services;

namespace BookCatalog.Pages;

public partial class CollectionDetail
{
    private enum ViewMode { List, Tile }
    private enum GroupMode { None, Label, Author }

    [Parameter]
    public Guid Id { get; set; }

    private BookCollection? _collection;
    private List<Book> _books = new();
    private bool _loading = true;
    private bool _booksLoading = true;
    private ViewMode _viewMode = ViewMode.List;
    private GroupMode _groupMode = GroupMode.None;

    private int _pageSize = 20;
    private int _currentPage = 1;

    private int _totalPages =>
        _books.Count > 0
            ? (int)Math.Ceiling(_books.Count / (double)_pageSize)
            : 1;

    private IReadOnlyList<Book> PagedBooks =>
        _books.Skip((_currentPage - 1) * _pageSize).Take(_pageSize).ToList();
    private readonly BookFilter _filter = new();
    private List<string> _allLabels = new();

    private const string NoLabel = "Sans label";
    private const string NoAuthor = "Sans auteur";

    private sealed record BookGroup(string Name, List<Book> Books);
    private List<BookGroup> _groupedBooks = new();

    // Filters + view toggles are remembered across navigation and reloads
    // (localStorage) until the user changes them. Keyed per collection, so each
    // collection keeps its own filters.
    private string StateKey => $"collection-view.{Id}";

    private sealed class ViewState
    {
        public string? Title { get; set; }
        public string? Author { get; set; }
        public string? Label { get; set; }
        public BookSort Sort { get; set; }
        public ViewMode View { get; set; }
        public GroupMode Group { get; set; }
        public int PageSize { get; set; } = 20;
    }

    // Initial expand state only: true when restored filters would otherwise be
    // hidden. Never reassigned, so Blazor's diff leaves the attribute alone
    // after the first render and the user's own open/close clicks stick.
    private bool _filtersOpen;

    private bool _isbnSearching;
    private bool _isbnSearched;
    private List<Book> _isbnHere = new();
    private List<string> _isbnElsewhere = new();

    protected override async Task OnInitializedAsync()
    {
        RestoreState();

        _collection = await CollectionService.GetByIdAsync(Id);
        _loading = false;

        try
        {
            _allLabels = await LabelService.GetNamesAsync();
        }
        catch
        {
            _allLabels = new();
        }

        await ReloadBooksAsync();
    }

    private void RestoreState()
    {
        var s = UiState.Get<ViewState>(StateKey);
        if (s == null)
        {
            return;
        }

        _filter.Title = s.Title;
        _filter.Author = s.Author;
        _filter.Label = s.Label;
        _filter.Sort = s.Sort;
        _viewMode = s.View;
        _groupMode = s.Group;
        _pageSize = s.PageSize is 20 or 40 or 60 ? s.PageSize : 20;

        _filtersOpen = !string.IsNullOrWhiteSpace(_filter.Title)
            || !string.IsNullOrWhiteSpace(_filter.Author)
            || !string.IsNullOrWhiteSpace(_filter.Label)
            || _filter.Sort != BookSort.Recent;
    }

    private void PersistState() => UiState.Set(StateKey, new ViewState
    {
        Title = _filter.Title,
        Author = _filter.Author,
        Label = _filter.Label,
        Sort = _filter.Sort,
        View = _viewMode,
        Group = _groupMode,
        PageSize = _pageSize,
    });

    private void SetViewMode(ViewMode mode)
    {
        _viewMode = mode;
        PersistState();
    }

    private void OnPageSizeChanged()
    {
        _currentPage = 1;
        PersistState();
    }

    private void PrevPage()
    {
        if (_currentPage > 1)
        {
            _currentPage--;
        }
    }

    private void NextPage()
    {
        if (_currentPage < _totalPages)
        {
            _currentPage++;
        }
    }

    private void SetGroupMode(GroupMode mode)
    {
        _groupMode = mode;
        RebuildGroups();
        PersistState();
    }

    private async Task ReloadBooksAsync()
    {
        PersistState();
        _booksLoading = true;
        _books = await BookService.GetByCollectionAsync(Id, _filter);
        _booksLoading = false;
        _currentPage = Math.Clamp(_currentPage, 1, _totalPages);
        RebuildGroups();
        StateHasChanged();
    }

    /// <summary>
    /// Buckets <see cref="_books"/> by label or author. A book with several labels
    /// lands in each of its groups; books missing the key go in a "Sans …" group
    /// that sorts last. Books keep the order set by the current sort.
    /// </summary>
    private void RebuildGroups()
    {
        if (_groupMode == GroupMode.None)
        {
            _groupedBooks = new();
            return;
        }

        var placeholder = _groupMode == GroupMode.Label ? NoLabel : NoAuthor;
        var groups = new Dictionary<string, List<Book>>(StringComparer.CurrentCultureIgnoreCase);

        foreach (var book in _books)
        {
            IEnumerable<string> keys = _groupMode switch
            {
                GroupMode.Label => book.LabelNames is { Count: > 0 }
                    ? book.LabelNames
                    : new List<string> { placeholder },
                _ => new List<string> { string.IsNullOrWhiteSpace(book.Author) ? placeholder : book.Author.Trim() }
            };

            foreach (var key in keys)
            {
                if (!groups.TryGetValue(key, out var bucket))
                {
                    bucket = new();
                    groups[key] = bucket;
                }
                bucket.Add(book);
            }
        }

        _groupedBooks = groups
            .OrderBy(g => g.Key == placeholder ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new BookGroup(g.Key, g.Value))
            .ToList();
    }

    private async Task IsbnSearchAsync(string isbn)
    {
        _isbnSearching = true;
        _isbnSearched = false;
        StateHasChanged();
        try
        {
            var matches = await BookService.SearchByIsbnAsync(isbn);
            _isbnHere = matches.Where(b => b.CollectionId == Id).ToList();

            var otherIds = matches
                .Where(b => b.CollectionId != Id)
                .Select(b => b.CollectionId)
                .Distinct()
                .ToList();

            if (otherIds.Any())
            {
                var all = await CollectionService.GetAllAsync();
                _isbnElsewhere = all
                    .Where(c => otherIds.Contains(c.Id))
                    .Select(c => c.Name)
                    .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            else
            {
                _isbnElsewhere = new();
            }

            _isbnSearched = true;
        }
        finally
        {
            _isbnSearching = false;
            StateHasChanged();
        }
    }

    private void AddBook() => Navigation.NavigateTo($"collection/{Id}/book/new");

    private void OpenBook(Book book)
    {
        if (AuthService.IsAdmin)
        {
            Navigation.NavigateTo($"book/{book.Id}/edit");
        }
    }

    private async Task DeleteBookAsync(Book book)
    {
        var confirmed = await Confirm.ConfirmAsync(
            $"Supprimer le livre « {book.Title} » ?",
            title: "Supprimer le livre");
        if (!confirmed)
        {
            return;
        }

        await BookService.DeleteAsync(book);
        await ReloadBooksAsync();
    }
}
