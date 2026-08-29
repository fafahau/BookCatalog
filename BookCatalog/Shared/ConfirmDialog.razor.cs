using BookCatalog.Services;

namespace BookCatalog.Shared;

public partial class ConfirmDialog
{
    protected override void OnInitialized() => Confirm.OnChange += Refresh;

    private void Refresh() => InvokeAsync(StateHasChanged);

    public void Dispose() => Confirm.OnChange -= Refresh;
}
