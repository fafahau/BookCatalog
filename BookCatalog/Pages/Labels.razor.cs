using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using BookCatalog.Models;
using BookCatalog.Services;

namespace BookCatalog.Pages;

public partial class Labels
{
    [CascadingParameter(Name = "IsOnline")]
    private bool IsOnline { get; set; } = true;

    private List<Label> _labels = new();
    private Dictionary<Guid, string> _drafts = new();
    private string _newLabelName = string.Empty;
    private string? _message;
    private bool _loading = true;
    private bool _busy;

    protected override async Task OnInitializedAsync() => await ReloadAsync();

    private async Task OnNewLabelKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await CreateAsync();
        }
    }

    private async Task CreateAsync()
    {
        var name = _newLabelName.Trim();
        if (name.Length == 0 || _busy || !IsOnline)
        {
            return;
        }

        _busy = true;
        _message = null;
        try
        {
            var created = await LabelService.CreateAsync(name);
            if (created == null)
            {
                _message = $"Le label « {name} » existe déjà.";
                return;
            }

            _newLabelName = string.Empty;
            await ReloadAsync();
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ReloadAsync()
    {
        _loading = true;
        _labels = await LabelService.GetAllAsync();
        _drafts = _labels.ToDictionary(l => l.Id, l => l.Name);
        _loading = false;
    }

    private async Task RenameAsync(Label label)
    {
        var newName = _drafts[label.Id].Trim();
        if (newName.Length == 0 || newName == label.Name || !IsOnline)
        {
            return;
        }

        _busy = true;
        try
        {
            await LabelService.RenameAsync(label.Id, newName);
            await ReloadAsync();
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task DeleteAsync(Label label)
    {
        if (!IsOnline)
        {
            return;
        }

        var confirmed = await Confirm.ConfirmAsync(
            $"Supprimer le label « {label.Name} » de tous les livres ?",
            title: "Supprimer le label");
        if (!confirmed)
        {
            return;
        }

        _busy = true;
        try
        {
            await LabelService.DeleteAsync(label.Id);
            await ReloadAsync();
        }
        finally
        {
            _busy = false;
        }
    }
}
