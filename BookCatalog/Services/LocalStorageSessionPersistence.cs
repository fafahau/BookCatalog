using Microsoft.JSInterop;
using Newtonsoft.Json;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace BookCatalog.Services;

/// <summary>
/// Persists the Supabase session in browser localStorage using synchronous JS interop,
/// which the WebAssembly runtime supports in-process (required by IGotrueSessionPersistence's sync contract).
/// </summary>
public class LocalStorageSessionPersistence : IGotrueSessionPersistence<Session>
{
    private const string StorageKey = "bookcatalog.supabase.session";
    private readonly IJSInProcessRuntime _js;

    public LocalStorageSessionPersistence(IJSInProcessRuntime js)
    {
        _js = js;
    }

    public void SaveSession(Session session)
    {
        try
        {
            var json = JsonConvert.SerializeObject(session);
            _js.InvokeVoid("localStorage.setItem", StorageKey, json);
        }
        catch
        {
            // Storage can be unavailable (private browsing) - session just won't persist across reloads.
        }
    }

    public void DestroySession()
    {
        try
        {
            _js.InvokeVoid("localStorage.removeItem", StorageKey);
        }
        catch
        {
        }
    }

    public Session? LoadSession()
    {
        try
        {
            var json = _js.Invoke<string?>("localStorage.getItem", StorageKey);
            return string.IsNullOrEmpty(json) ? null : JsonConvert.DeserializeObject<Session>(json);
        }
        catch
        {
            return null;
        }
    }
}
