using BookCatalog.Models;
using Supabase.Postgrest;

namespace BookCatalog.Services;

/// <summary>Admin-only user management (brief 3.6 / Users.razor).</summary>
public class UserService
{
    private readonly Supabase.Client _client;

    public UserService(SupabaseService supabaseService)
    {
        _client = supabaseService.Client;
    }

    public async Task<List<Profile>> GetAllAsync()
    {
        var result = await _client.From<Profile>()
            .Order("created_at", Constants.Ordering.Ascending)
            .Get();
        return result.Models;
    }

    public async Task SetRoleAsync(Guid userId, string role)
    {
        await _client.From<Profile>()
            .Filter("id", Constants.Operator.Equals, userId.ToString())
            .Set(p => p.Role, role)
            .Update();
    }

    /// <summary>
    /// Revokes catalog access by deleting the profile row (RLS policies require a profile to read/write
    /// collections or books). The underlying auth.users account still exists - fully deleting it requires
    /// the service_role key, which must never ship in this client-side app; do that from the Supabase dashboard.
    /// </summary>
    public async Task RemoveAsync(Guid userId)
    {
        await _client.From<Profile>()
            .Filter("id", Constants.Operator.Equals, userId.ToString())
            .Delete();
    }
}
