using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BookCatalog.Shared;

public partial class ThemeToggle
{
    private const string StorageKey = "bookcatalog.theme";

    // "auto" follows the device preference; "light"/"dark" are explicit overrides.
    private string _choice = "auto";

    [Parameter] public string? Class { get; set; }

    protected override void OnInitialized()
    {
        try
        {
            var stored = Js.Invoke<string?>("localStorage.getItem", StorageKey);
            if (stored is "light" or "dark")
            {
                _choice = stored;
            }
        }
        catch
        {
            // localStorage unavailable (private browsing) - stay on "auto".
        }
    }

    private string ButtonClass(string choice) => choice == _choice ? "active" : "";

    private void SetChoice(string choice)
    {
        _choice = choice;
        try
        {
            if (choice == "auto")
            {
                Js.InvokeVoid("localStorage.removeItem", StorageKey);
                Js.InvokeVoid("document.documentElement.removeAttribute", "data-theme");
            }
            else
            {
                Js.InvokeVoid("localStorage.setItem", StorageKey, choice);
                Js.InvokeVoid("document.documentElement.setAttribute", "data-theme", choice);
            }
            Js.InvokeVoid("bookcatalogApplyThemeMeta", choice);
        }
        catch
        {
            // Ignore - the in-memory choice still updates the button state.
        }
    }
}
