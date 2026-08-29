using Microsoft.AspNetCore.Components;

namespace BookCatalog.Shared;

public partial class FieldIcon
{
    [Parameter, EditorRequired]
    public string Kind { get; set; } = "";
}
