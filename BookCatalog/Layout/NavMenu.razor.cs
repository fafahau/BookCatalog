using Microsoft.AspNetCore.Components;
using BookCatalog.Services;

namespace BookCatalog.Layout;

public partial class NavMenu : IDisposable
{
    protected override void OnInitialized() => Offline.Changed += OnConnectivityChanged;

    private void OnConnectivityChanged() => InvokeAsync(StateHasChanged);

    private async Task LogoutAsync()
    {
        await AuthService.LogoutAsync();
        Navigation.NavigateTo("login");
    }

    public void Dispose() => Offline.Changed -= OnConnectivityChanged;
}
