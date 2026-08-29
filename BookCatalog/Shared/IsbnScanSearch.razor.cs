using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BookCatalog.Shared;

public partial class IsbnScanSearch
{
    /// <summary>Placeholder shown in the ISBN input.</summary>
    [Parameter]
    public string Placeholder { get; set; } = "Rechercher par ISBN";

    /// <summary>Set by the parent while a search is running; disables the button.</summary>
    [Parameter]
    public bool Searching { get; set; }

    /// <summary>Raised with the trimmed ISBN when the user searches or a barcode is scanned.</summary>
    [Parameter]
    public EventCallback<string> OnSearch { get; set; }

    private string _query = string.Empty;

    private bool _scanning;
    private bool _scanStarting;
    private string? _scanError;
    private ElementReference _scanVideo;
    private DotNetObjectReference<IsbnScanSearch>? _selfRef;

    protected override void OnInitialized()
        => _selfRef = DotNetObjectReference.Create(this);

    private async Task OnKeyUp(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await RaiseSearchAsync();
        }
    }

    private async Task RaiseSearchAsync()
    {
        var query = _query?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        await OnSearch.InvokeAsync(query);
    }

    private async Task ToggleScanAsync()
    {
        if (_scanning)
        {
            await StopScanAsync();
            return;
        }

        _scanError = null;
        _scanning = true;
        _scanStarting = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_scanning || !_scanStarting)
        {
            return;
        }

        _scanStarting = false;
        try
        {
            await JS.InvokeAsync<string>("barcodeScanner.start", _scanVideo, _selfRef);
        }
        catch (Exception ex)
        {
            _scanning = false;
            _scanError = "Impossible d'accéder à la caméra : " + ex.Message;
            StateHasChanged();
        }
    }

    [JSInvokable]
    public async Task OnBarcodeDetected(string code)
    {
        _query = code;
        await StopScanAsync();
        await RaiseSearchAsync();
    }

    private async Task StopScanAsync()
    {
        _scanning = false;
        _scanStarting = false;
        try
        {
            await JS.InvokeVoidAsync("barcodeScanner.stop");
        }
        catch
        {
            // circuit already gone / script not loaded
        }
        StateHasChanged();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("barcodeScanner.stop");
        }
        catch
        {
            // circuit already gone
        }
        _selfRef?.Dispose();
    }
}
