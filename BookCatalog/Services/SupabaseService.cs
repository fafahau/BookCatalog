using Microsoft.Extensions.Configuration;
using Supabase;

namespace BookCatalog.Services;

/// <summary>
/// Singleton wrapper around the Supabase.Client (DI, per brief section 6).
/// </summary>
public class SupabaseService
{
    public Client Client { get; }

    public SupabaseService(IConfiguration configuration, LocalStorageSessionPersistence sessionPersistence)
    {
        var url = configuration["Supabase:Url"]
            ?? throw new InvalidOperationException("Supabase:Url is missing from appsettings.json");
        var anonKey = configuration["Supabase:AnonKey"]
            ?? throw new InvalidOperationException("Supabase:AnonKey is missing from appsettings.json");

        var options = new SupabaseOptions
        {
            AutoRefreshToken = true,
            AutoConnectRealtime = false,
            SessionHandler = sessionPersistence
        };

        Client = new Client(url, anonKey, options);
    }

    public async Task InitializeAsync()
    {
        // Pull any persisted session out of localStorage and into memory. The Supabase
        // client's InitializeAsync() only calls Auth.RetrieveSessionAsync(), which bails
        // out immediately when no session has been loaded yet - so without this call the
        // user is signed out on every page refresh even though a valid session is stored.
        Client.Auth.LoadSession();

        try
        {
            // Validates the restored session and refreshes it if the access token has
            // expired (AutoRefreshToken). This can throw when the device is offline or the
            // refresh call fails; the in-memory session stays intact in that case, so we
            // swallow it and let the background auto-refresh retry - keeping the user
            // signed in while offline instead of bouncing them to the login page.
            await Client.InitializeAsync();
        }
        catch
        {
            // Keep whatever LoadSession() restored.
        }
    }
}
