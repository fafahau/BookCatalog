using Microsoft.AspNetCore.Components;
using BookCatalog.Models;

namespace BookCatalog.Shared;

public partial class BookCollectionList
{
    [Parameter, EditorRequired]
    public IReadOnlyList<Book> Books { get; set; } = null!;

    [Parameter]
    public bool TileView { get; set; }

    [Parameter]
    public EventCallback<Book> OnOpen { get; set; }

    [Parameter]
    public EventCallback<Book> OnDelete { get; set; }

    /// <summary>When true, clicking an item toggles its selection instead of opening it.</summary>
    [Parameter]
    public bool SelectionMode { get; set; }

    [Parameter]
    public HashSet<Guid> SelectedIds { get; set; } = new();

    [Parameter]
    public EventCallback<Book> OnToggleSelect { get; set; }

    [CascadingParameter(Name = "IsOnline")]
    private bool IsOnline { get; set; } = true;

    private Task ItemClick(Book book) =>
        SelectionMode ? OnToggleSelect.InvokeAsync(book) : OnOpen.InvokeAsync(book);
}
