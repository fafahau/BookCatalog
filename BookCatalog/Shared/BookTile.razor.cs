using Microsoft.AspNetCore.Components;
using BookCatalog.Models;

namespace BookCatalog.Shared;

public partial class BookTile
{
    [Parameter, EditorRequired]
    public Book Book { get; set; } = null!;

    [Parameter]
    public EventCallback OnClick { get; set; }
}
