using BookCatalog.Models;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace BookCatalog.Services;

public class AuthService
{
    private readonly Supabase.Client _client;

    public Profile? CurrentProfile { get; private set; }

    public event Action? AuthStateChanged;

    public AuthService(SupabaseService supabaseService)
    {
        _client = supabaseService.Client;
        _client.Auth.AddStateChangedListener(OnAuthStateChange);
    }

    public bool IsAuthenticated => _client.Auth.CurrentSession != null && _client.Auth.CurrentUser != null;

    public bool IsAdmin => CurrentProfile?.IsAdmin ?? false;

    public Guid? CurrentUserId =>
        Guid.TryParse(_client.Auth.CurrentUser?.Id, out var id) ? id : null;

    private async void OnAuthStateChange(IGotrueClient<User, Session> sender, Supabase.Gotrue.Constants.AuthState state)
    {
        if (state == Supabase.Gotrue.Constants.AuthState.SignedOut)
        {
            CurrentProfile = null;
            AuthStateChanged?.Invoke();
            return;
        }

        if (state == Supabase.Gotrue.Constants.AuthState.SignedIn || state == Supabase.Gotrue.Constants.AuthState.TokenRefreshed)
        {
            await LoadCurrentProfileAsync();
            AuthStateChanged?.Invoke();
        }
    }

    /// <summary>Called once at startup after Client.InitializeAsync() to pick up a session restored from localStorage.</summary>
    public async Task RestoreSessionAsync()
    {
        if (IsAuthenticated)
        {
            await LoadCurrentProfileAsync();
        }
    }

    public async Task<string?> LoginAsync(string email, string password)
    {
        try
        {
            var session = await _client.Auth.SignInWithPassword(email, password);
            if (session == null)
            {
                return "Identifiants invalides.";
            }

            await LoadCurrentProfileAsync();
            AuthStateChanged?.Invoke();
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <returns>null on success, otherwise an error message. A successful signup may or may not need email confirmation.</returns>
    public async Task<(bool success, bool needsEmailConfirmation, string? error)> RegisterAsync(string email, string password, string displayName)
    {
        try
        {
            var options = new SignUpOptions
            {
                Data = new Dictionary<string, object> { ["display_name"] = displayName }
            };
            var session = await _client.Auth.SignUp(email, password, options);
            if (session?.AccessToken != null)
            {
                await LoadCurrentProfileAsync();
                AuthStateChanged?.Invoke();
                return (true, false, null);
            }

            return (true, true, null);
        }
        catch (Exception ex)
        {
            return (false, false, ex.Message);
        }
    }

    public async Task LogoutAsync()
    {
        await _client.Auth.SignOut();
        CurrentProfile = null;
        AuthStateChanged?.Invoke();
    }

    private async Task LoadCurrentProfileAsync()
    {
        var userId = CurrentUserId;
        if (userId == null)
        {
            CurrentProfile = null;
            return;
        }

        try
        {
            CurrentProfile = await _client.From<Profile>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, userId.Value.ToString())
                .Single();
        }
        catch
        {
            CurrentProfile = null;
        }
    }
}
