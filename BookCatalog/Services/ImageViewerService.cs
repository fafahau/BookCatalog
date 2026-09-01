using Microsoft.JSInterop;

namespace BookCatalog.Services;

/// <summary>
/// Tracks the image currently shown in the app-wide fullscreen viewer.
/// Opening the viewer pushes a browser history entry so the Back button
/// (and the Android system back gesture) closes it instead of leaving the page.
/// </summary>
public sealed class ImageViewerService : IDisposable
{
    private readonly IJSInProcessRuntime _js;
    private DotNetObjectReference<ImageViewerService>? _selfRef;
    private bool _registered;

    public ImageViewerService(IJSInProcessRuntime js) => _js = js;

    public string? CurrentUrl { get; private set; }

    public event Action? OnChange;

    public void Show(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var wasOpen = CurrentUrl != null;
        CurrentUrl = url;

        if (!wasOpen)
        {
            EnsureRegistered();
            TryJs("bookcatalogImageViewer.pushState");
        }

        OnChange?.Invoke();
    }

    /// <summary>Closed from the UI (✕ button or backdrop tap).</summary>
    public void Close() => CloseInternal(fromHistory: false);

    /// <summary>Closed by the browser Back button; the history entry is already gone.</summary>
    [JSInvokable]
    public void CloseFromHistory() => CloseInternal(fromHistory: true);

    private void CloseInternal(bool fromHistory)
    {
        if (CurrentUrl == null)
        {
            return;
        }

        CurrentUrl = null;

        if (!fromHistory)
        {
            TryJs("bookcatalogImageViewer.popState");
        }

        OnChange?.Invoke();
    }

    private void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        try
        {
            _selfRef ??= DotNetObjectReference.Create(this);
            _js.InvokeVoid("bookcatalogImageViewer.register", _selfRef);
            _registered = true;
        }
        catch
        {
            // JS not ready yet; retry on the next open.
        }
    }

    private void TryJs(string identifier)
    {
        try
        {
            _js.InvokeVoid(identifier);
        }
        catch
        {
            // Ignore: viewer state still updates without history integration.
        }
    }

    public void Dispose() => _selfRef?.Dispose();
}
