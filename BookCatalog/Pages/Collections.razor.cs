using BookCatalog.Models;
using BookCatalog.Services;

namespace BookCatalog.Pages;

public partial class Collections
{
    private List<BookCollection> _collections = new();
    private Dictionary<Guid, int> _bookCounts = new();
    private bool _loading = true;
    private string _newCollectionName = string.Empty;
    private Guid? _editingId;
    private string _editingName = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _loading = true;
        _collections = await CollectionService.GetAllAsync();
        _bookCounts = await CollectionService.GetBookCountsAsync();
        _loading = false;
    }

    private int GetBookCount(Guid collectionId) => _bookCounts.GetValueOrDefault(collectionId);

    private double GetBookPercent(Guid collectionId)
    {
        var total = _bookCounts.Values.Sum();
        return total == 0 ? 0 : GetBookCount(collectionId) * 100d / total;
    }

    private async Task CreateAsync()
    {
        var name = _newCollectionName.Trim();
        if (string.IsNullOrWhiteSpace(name) || AuthService.CurrentUserId == null)
        {
            return;
        }

        await CollectionService.CreateAsync(name, AuthService.CurrentUserId.Value);
        _newCollectionName = string.Empty;
        await ReloadAsync();
    }

    private void StartRename(BookCollection collection)
    {
        _editingId = collection.Id;
        _editingName = collection.Name;
    }

    private void CancelRename()
    {
        _editingId = null;
        _editingName = string.Empty;
    }

    private async Task SaveRenameAsync(Guid id)
    {
        var name = _editingName.Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            await CollectionService.RenameAsync(id, name);
        }

        CancelRename();
        await ReloadAsync();
    }

    private async Task DeleteAsync(BookCollection collection)
    {
        var confirmed = await Confirm.ConfirmAsync(
            $"Supprimer la collection « {collection.Name} » et tous ses livres ?",
            title: "Supprimer la collection");
        if (!confirmed)
        {
            return;
        }

        await CollectionService.DeleteAsync(collection.Id);
        await ReloadAsync();
    }
}
