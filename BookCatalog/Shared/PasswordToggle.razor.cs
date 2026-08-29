using Microsoft.AspNetCore.Components;

namespace BookCatalog.Shared;

public partial class PasswordToggle
{
    /// <summary>Whether the associated password is currently shown as plain text.</summary>
    [Parameter]
    public bool Visible { get; set; }

    /// <summary>Raised when the user clicks the toggle.</summary>
    [Parameter]
    public EventCallback OnToggle { get; set; }
}
