namespace BookCatalog.Services;

/// <summary>
/// Tracks the image currently shown in the app-wide fullscreen viewer.
/// </summary>
public sealed class ImageViewerService
{
    public string? CurrentUrl { get; private set; }

    public event Action? OnChange;

    public void Show(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        CurrentUrl = url;
        OnChange?.Invoke();
    }

    public void Close()
    {
        if (CurrentUrl == null)
        {
            return;
        }

        CurrentUrl = null;
        OnChange?.Invoke();
    }
}
