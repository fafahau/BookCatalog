using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using BookCatalog.Models;
using BookCatalog.Services;

namespace BookCatalog.Pages;

public partial class BookForm
{
    [Parameter]
    public Guid CollectionId { get; set; }

    [Parameter]
    public Guid Id { get; set; }

    [CascadingParameter(Name = "IsOnline")]
    private bool IsOnline { get; set; } = true;

    private bool _isEdit => Id != Guid.Empty;
    private string BackHref => _collection != null ? $"collection/{_collection.Id}" : "";

    private bool _loading = true;
    private bool _saving;
    private bool _isbnLookupBusy;
    private string? _error;
    private string? _isbnMessage;

    private bool _scanning;
    private bool _scanStarting;
    private string? _scanError;
    private ElementReference _scanVideo;
    private DotNetObjectReference<BookForm>? _selfRef;

    private BookCollection? _collection;
    private Book? _existingBook;

    private string _title = string.Empty;
    private string _author = string.Empty;
    private string _isbn = string.Empty;

    private List<string> _authors = new();

    private readonly List<string> _labels = new();
    private string _labelDraft = string.Empty;
    private List<string> _allLabels = new();

    private ElementReference _photo1FileRef;
    private ElementReference _photo1CamRef;
    private ElementReference _photo2FileRef;
    private ElementReference _photo2CamRef;
    private string? _cropMessage;
    private byte[]? _photo1PendingBytes;
    private byte[]? _photo2PendingBytes;
    private string? _photo1Preview;
    private string? _photo2Preview;

    protected override async Task OnInitializedAsync()
    {
        _selfRef = DotNetObjectReference.Create(this);

        if (_isEdit)
        {
            _existingBook = await BookService.GetByIdAsync(Id);
            if (_existingBook != null)
            {
                _title = _existingBook.Title;
                _author = _existingBook.Author;
                _isbn = _existingBook.Isbn ?? string.Empty;
                _photo1Preview = _existingBook.PhotoUrl1;
                _photo2Preview = _existingBook.PhotoUrl2;
                _labels.AddRange(_existingBook.LabelNames);
                _collection = await CollectionService.GetByIdAsync(_existingBook.CollectionId);
            }
        }
        else
        {
            _collection = await CollectionService.GetByIdAsync(CollectionId);
        }

        try
        {
            _authors = await BookService.GetAuthorsAsync();
        }
        catch
        {
            // Autocompletion is a convenience — a failure here shouldn't block the form.
            _authors = new();
        }

        try
        {
            _allLabels = await LabelService.GetNamesAsync();
        }
        catch
        {
            _allLabels = new();
        }

        _loading = false;
    }

    private void OnLabelKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            AddLabelDraft();
        }
    }

    private void AddLabelDraft()
    {
        var label = _labelDraft.Trim();
        _labelDraft = string.Empty;

        if (label.Length == 0 || _labels.Any(l => l.Equals(label, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _labels.Add(label);
    }

    private void RemoveLabel(string label) => _labels.Remove(label);

    private List<string> NormalizedLabels() => _labels
        .Select(l => l.Trim())
        .Where(l => l.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private async Task LookupIsbnAsync()
    {
        if (!IsOnline)
        {
            _isbnMessage = "La recherche par ISBN nécessite une connexion.";
            return;
        }

        _isbnLookupBusy = true;
        _isbnMessage = null;
        StateHasChanged();

        IsbnLookupResult? result;
        try
        {
            result = await IsbnLookupService.LookupAsync(_isbn);
        }
        finally
        {
            _isbnLookupBusy = false;
        }

        if (result == null)
        {
            _isbnMessage = "Aucune information trouvée pour cet ISBN. Saisie manuelle possible.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.Title))
        {
            _title = result.Title!;
        }
        if (!string.IsNullOrWhiteSpace(result.Author))
        {
            _author = result.Author!;
        }

        if (!string.IsNullOrWhiteSpace(result.CoverUrl) && _photo1PendingBytes == null)
        {
            var bytes = await ImageUploadService.CompressFromUrlAsync(result.CoverUrl!);
            if (bytes != null)
            {
                _photo1PendingBytes = bytes;
                _photo1Preview = ToDataUrl(bytes);
            }
            else
            {
                _isbnMessage = "Titre/auteur pré-remplis. La couverture n'a pas pu être récupérée automatiquement.";
            }
        }

        StateHasChanged();
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
        _isbn = code;
        await StopScanAsync();
        await LookupIsbnAsync();
        StateHasChanged();
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

    private async Task OnPhotoSelectedAsync(int slot, ElementReference input)
    {
        _cropMessage = null;
        var bytes = await ImageUploadService.CompressFromInputElementAsync(input);
        if (bytes == null)
        {
            return;
        }

        SetPhoto(slot, bytes);
        StateHasChanged();

        // Let the user crop the photo they just picked (they can skip with "Annuler").
        await CropAsync(slot);
    }

    private void SetPhoto(int slot, byte[] bytes)
    {
        var dataUrl = ToDataUrl(bytes);
        if (slot == 1)
        {
            _photo1PendingBytes = bytes;
            _photo1Preview = dataUrl;
        }
        else
        {
            _photo2PendingBytes = bytes;
            _photo2Preview = dataUrl;
        }
    }

    private async Task CropAsync(int slot)
    {
        _cropMessage = null;
        var bytes = slot == 1 ? _photo1PendingBytes : _photo2PendingBytes;

        // When editing an existing book the only copy may be the uploaded URL; pull it back down first.
        if (bytes == null)
        {
            var url = slot == 1 ? _photo1Preview : _photo2Preview;
            if (!string.IsNullOrEmpty(url))
            {
                bytes = await ImageUploadService.CompressFromUrlAsync(url);
            }
        }

        if (bytes == null)
        {
            _cropMessage = "Impossible de charger cette image pour la recadrer.";
            return;
        }

        var cropped = await ImageUploadService.CropAsync(bytes);
        if (cropped != null)
        {
            SetPhoto(slot, cropped);
        }
        StateHasChanged();
    }

    private static string ToDataUrl(byte[] jpegBytes) => $"data:image/jpeg;base64,{Convert.ToBase64String(jpegBytes)}";

    private async Task SaveAsync()
    {
        if (!IsOnline)
        {
            _error = "Impossible d'enregistrer hors ligne. Reconnectez-vous pour ajouter ou modifier un livre.";
            return;
        }

        _saving = true;
        _error = null;

        // Fold in a label the user typed but didn't confirm with Enter / "Ajouter".
        AddLabelDraft();
        var labels = NormalizedLabels();

        try
        {
            Guid bookId;
            Guid collectionId;

            if (_isEdit && _existingBook != null)
            {
                bookId = _existingBook.Id;
                collectionId = _existingBook.CollectionId;

                _existingBook.Title = _title.Trim();
                _existingBook.Author = _author.Trim();
                _existingBook.Isbn = string.IsNullOrWhiteSpace(_isbn) ? null : _isbn.Trim();

                if (_photo1PendingBytes != null)
                {
                    _existingBook.PhotoUrl1 = await ImageUploadService.UploadPhotoAsync(collectionId, bookId, 1, _photo1PendingBytes);
                }
                if (_photo2PendingBytes != null)
                {
                    _existingBook.PhotoUrl2 = await ImageUploadService.UploadPhotoAsync(collectionId, bookId, 2, _photo2PendingBytes);
                }

                await BookService.UpdateAsync(_existingBook);
            }
            else
            {
                collectionId = CollectionId;
                var newBook = new Book
                {
                    Title = _title.Trim(),
                    Author = _author.Trim(),
                    Isbn = string.IsNullOrWhiteSpace(_isbn) ? null : _isbn.Trim(),
                    CollectionId = collectionId,
                    CreatedBy = AuthService.CurrentUserId
                };

                var created = await BookService.CreateAsync(newBook);
                bookId = created.Id;

                if (_photo1PendingBytes != null)
                {
                    created.PhotoUrl1 = await ImageUploadService.UploadPhotoAsync(collectionId, bookId, 1, _photo1PendingBytes);
                }
                if (_photo2PendingBytes != null)
                {
                    created.PhotoUrl2 = await ImageUploadService.UploadPhotoAsync(collectionId, bookId, 2, _photo2PendingBytes);
                }

                if (_photo1PendingBytes != null || _photo2PendingBytes != null)
                {
                    await BookService.UpdateAsync(created);
                }
            }

            await LabelService.SetBookLabelsAsync(bookId, labels);

            Navigation.NavigateTo($"collection/{collectionId}");
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _saving = false;
        }
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
