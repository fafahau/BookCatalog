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
}
