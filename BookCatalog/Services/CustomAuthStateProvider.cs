using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace BookCatalog.Services;

public class CustomAuthStateProvider : AuthenticationStateProvider
{
    private readonly AuthService _authService;

    public CustomAuthStateProvider(AuthService authService)
    {
        _authService = authService;
        _authService.AuthStateChanged += () => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        ClaimsIdentity identity;

        if (_authService.IsAuthenticated && _authService.CurrentProfile != null)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, _authService.CurrentUserId?.ToString() ?? string.Empty),
                new(ClaimTypes.Role, _authService.CurrentProfile.Role)
            };
            identity = new ClaimsIdentity(claims, "supabase");
        }
        else
        {
            identity = new ClaimsIdentity();
        }

        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }
}
