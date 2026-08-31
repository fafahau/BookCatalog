using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using BookCatalog.Models;
using BookCatalog.Services;

namespace BookCatalog.Pages;

public partial class CollectionDetail
{
    private enum ViewMode { List, Tile }
    private enum GroupMode { None, Label, Author }

    [Parameter]
    public Guid Id { get; set; }

    [CascadingParameter(Name = "IsOnline")]
    private bool IsOnline { get; set; } = true;

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
        public bool GroupsExpanded { get; set; } = true;
    }

    // Initial expand state only: true when restored filters would otherwise be
    // hidden. Never reassigned, so Blazor's diff leaves the attribute alone
    // after the first render and the user's own open/close clicks stick.
    private bool _filtersOpen;

    // Drives the "open" attribute of every group's <details>. Reassigned only by
    // the expand/collapse switch, so between clicks Blazor leaves each group
    // alone and the user's own per-group toggles stick.
    private bool _groupsExpanded = true;

    private bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(_filter.Title)
        || !string.IsNullOrWhiteSpace(_filter.Author)
        || !string.IsNullOrWhiteSpace(_filter.Label)
        || _filter.Sort != BookSort.Recent;

    private bool _isbnSearching;
    private bool _isbnSearched;
    private List<Book> _isbnHere = new();
    private List<string> _isbnElsewhere = new();

    // Bulk selection (admin): pick several books, then add one or more labels to
    // all of them at once. Ephemeral — never persisted to localStorage.
    private bool _selecting;
    private readonly HashSet<Guid> _selectedIds = new();

    private bool _labelDialogOpen;
    private readonly List<string> _dialogLabels = new();
    private string _dialogLabelDraft = string.Empty;
    private bool _applyingLabels;

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
        _groupsExpanded = s.GroupsExpanded;

        _filtersOpen = HasActiveFilters;
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
        GroupsExpanded = _groupsExpanded,
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

    private void SetGroupsExpanded(bool expanded)
    {
        _groupsExpanded = expanded;
        PersistState();
    }

    private async Task ClearFiltersAsync()
    {
        _filter.Title = null;
        _filter.Author = null;
        _filter.Label = null;
        _filter.Sort = BookSort.Recent;
        await ReloadBooksAsync();
    }

    private void ToggleSelecting()
    {
        if (!IsOnline)
        {
            return;
        }

        _selecting = !_selecting;
        if (!_selecting)
        {
            _selectedIds.Clear();
            _labelDialogOpen = false;
        }
    }

    private void ToggleBook(Book book)
    {
        if (!_selectedIds.Remove(book.Id))
        {
            _selectedIds.Add(book.Id);
        }
    }

    // "Select all" spans every filtered book, not just the current page.
    private void SelectAllFiltered()
    {
        foreach (var book in _books)
        {
            _selectedIds.Add(book.Id);
        }
    }

    private void ClearSelection() => _selectedIds.Clear();

    private void OpenLabelDialog()
    {
        _dialogLabels.Clear();
        _dialogLabelDraft = string.Empty;
        _labelDialogOpen = true;
    }

    private void CloseLabelDialog() => _labelDialogOpen = false;

    private void OnDialogLabelKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            AddDialogLabel();
        }
    }

    private void AddDialogLabel()
    {
        var label = _dialogLabelDraft.Trim();
        _dialogLabelDraft = string.Empty;

        if (label.Length == 0 || _dialogLabels.Any(l => l.Equals(label, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _dialogLabels.Add(label);
    }

    private void RemoveDialogLabel(string label) => _dialogLabels.Remove(label);

    private async Task ApplyLabelsAsync()
    {
        AddDialogLabel(); // fold in a label typed but not confirmed with Enter / "Ajouter"

        var labels = _dialogLabels
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!IsOnline || labels.Count == 0 || _selectedIds.Count == 0)
        {
            return;
        }

        _applyingLabels = true;
        try
        {
            await LabelService.AddLabelsToBooksAsync(_selectedIds.ToList(), labels);

            try
            {
                _allLabels = await LabelService.GetNamesAsync();
            }
            catch
            {
                // Suggestions are a convenience — a refresh failure shouldn't block the flow.
            }

            await ReloadBooksAsync();

            _labelDialogOpen = false;
            _selecting = false;
            _selectedIds.Clear();
        }
        finally
        {
            _applyingLabels = false;
        }
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

    private void AddBook()
    {
        if (IsOnline)
        {
            Navigation.NavigateTo($"collection/{Id}/book/new");
        }
    }

    private void OpenBook(Book book)
    {
        if (AuthService.IsAdmin)
        {
            Navigation.NavigateTo($"book/{book.Id}/edit");
        }
    }

    private async Task DeleteBookAsync(Book book)
    {
        if (!IsOnline)
        {
            return;
        }

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
