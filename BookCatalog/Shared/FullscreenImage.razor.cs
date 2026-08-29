using BookCatalog.Services;

namespace BookCatalog.Shared;

public partial class FullscreenImage
{
    protected override void OnInitialized() => ImageViewer.OnChange += Refresh;

    private void Refresh() => InvokeAsync(StateHasChanged);

    public void Dispose() => ImageViewer.OnChange -= Refresh;
}
