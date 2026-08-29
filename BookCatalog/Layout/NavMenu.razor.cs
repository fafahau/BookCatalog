using Microsoft.AspNetCore.Components;
using BookCatalog.Services;

namespace BookCatalog.Layout;

public partial class NavMenu
{
    private async Task LogoutAsync()
    {
        await AuthService.LogoutAsync();
        Navigation.NavigateTo("login");
    }
}
