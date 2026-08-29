using System.ComponentModel.DataAnnotations;
using Microsoft.JSInterop;
using BookCatalog.Services;

namespace BookCatalog.Pages;

public partial class ResetPassword
{
    private enum State { Checking, Ready, Invalid, Done }
    private State _state = State.Checking;
    private readonly NewPassword _model = new();
    private string? _error;
    private bool _busy;
    private bool _showPassword;

    private string PasswordInputType => _showPassword ? "text" : "password";
    private void ToggleShowPassword() => _showPassword = !_showPassword;
    private void GoToLogin() => Navigation.NavigateTo("login");

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        var fragment = await JS.InvokeAsync<string>("authRedirect.consumeHash");
        var parts = ParseFragment(fragment);

        if (parts.TryGetValue("error_description", out var description))
        {
            Fail(description);
            return;
        }
        if (parts.ContainsKey("error"))
        {
            Fail("Le lien de réinitialisation est invalide ou a expiré.");
            return;
        }

        if (!parts.TryGetValue("access_token", out var accessToken) ||
            !parts.TryGetValue("refresh_token", out var refreshToken))
        {
            Fail("Ouvrez cette page depuis le lien reçu par email pour réinitialiser votre mot de passe.");
            return;
        }

        var error = await AuthService.SetSessionFromRecoveryAsync(accessToken, refreshToken);
        if (error != null)
        {
            Fail(error);
            return;
        }

        _state = State.Ready;
        StateHasChanged();
    }

    private void Fail(string message)
    {
        _error = message;
        _state = State.Invalid;
        StateHasChanged();
    }

    private async Task SubmitAsync()
    {
        if (_model.Password != _model.Confirm)
        {
            _error = "Les deux mots de passe ne correspondent pas.";
            return;
        }

        _busy = true;
        _error = null;
        var error = await AuthService.UpdatePasswordAsync(_model.Password);
        _busy = false;

        if (error != null)
        {
            _error = error;
            return;
        }

        // Drop the recovery session so the user re-authenticates with the new password.
        await AuthService.LogoutAsync();
        _state = State.Done;
    }

    private static Dictionary<string, string> ParseFragment(string? fragment)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(fragment))
        {
            return result;
        }

        foreach (var pair in fragment.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(pair[..eq]);
            var value = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            result[key] = value;
        }

        return result;
    }

    private class NewPassword
    {
        [Required]
        [MinLength(6)]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string Confirm { get; set; } = string.Empty;
    }
}
