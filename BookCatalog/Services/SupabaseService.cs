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

    public Task InitializeAsync() => Client.InitializeAsync();
}
