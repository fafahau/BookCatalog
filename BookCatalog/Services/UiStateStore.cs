using Microsoft.JSInterop;
using Newtonsoft.Json;

namespace BookCatalog.Services;

/// <summary>
/// Tiny synchronous localStorage-backed key/value store for UI preferences —
/// filters, sort, view toggles — that should survive navigation and reloads
/// until the user changes them. WebAssembly's JS runtime is always in-process,
/// so the synchronous calls are safe (same trick as LocalStorageSessionPersistence).
/// </summary>
public class UiStateStore
{
    private const string Prefix = "bookcatalog.ui.";
    private readonly IJSInProcessRuntime _js;

    public UiStateStore(IJSInProcessRuntime js)
    {
        _js = js;
    }

    public T? Get<T>(string key)
    {
        try
        {
            var json = _js.Invoke<string?>("localStorage.getItem", Prefix + key);
            return string.IsNullOrEmpty(json) ? default : JsonConvert.DeserializeObject<T>(json);
        }
        catch
        {
            return default;
        }
    }

    public void Set<T>(string key, T value)
    {
        try
        {
            _js.InvokeVoid("localStorage.setItem", Prefix + key, JsonConvert.SerializeObject(value));
        }
        catch
        {
            // Storage can be unavailable (private browsing) - preferences just won't persist.
        }
    }
}
