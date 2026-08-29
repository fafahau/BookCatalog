using System.ComponentModel.DataAnnotations;
using BookCatalog.Services;

namespace BookCatalog.Pages;

public partial class Login
{
    private enum Mode { Login, Register, ForgotPassword }
    private Mode _mode = Mode.Login;
    private readonly Credentials _model = new();
    private string? _error;
    private string? _info;
    private bool _busy;
    private bool _showPassword;

    private string PasswordInputType => _showPassword ? "text" : "password";

    private void ToggleShowPassword() => _showPassword = !_showPassword;

    private void SwitchMode(Mode mode)
    {
        _mode = mode;
        _error = null;
        _info = null;
        _showPassword = false;
    }

    private async Task LoginAsync()
    {
        _busy = true;
        _error = null;
        var error = await AuthService.LoginAsync(_model.Email, _model.Password);
        _busy = false;

        if (error == null)
        {
            Navigation.NavigateTo("");
        }
        else
        {
            _error = error;
        }
    }

    private async Task SendResetAsync()
    {
        if (string.IsNullOrWhiteSpace(_model.Email))
        {
            _error = "Saisissez votre adresse email.";
            _info = null;
            return;
        }

        _busy = true;
        _error = null;
        _info = null;
        var redirectTo = Navigation.ToAbsoluteUri("reset-password").ToString();
        var error = await AuthService.SendPasswordResetAsync(_model.Email, redirectTo);
        _busy = false;

        if (error == null)
        {
            _info = "Si un compte existe pour cette adresse, un email de réinitialisation vient d'être envoyé. Suivez le lien qu'il contient pour choisir un nouveau mot de passe.";
        }
        else
        {
            _error = error;
        }
    }

    private async Task RegisterAsync()
    {
        _busy = true;
        _error = null;
        _info = null;
        var (success, needsEmailConfirmation, error) = await AuthService.RegisterAsync(_model.Email, _model.Password, _model.DisplayName);
        _busy = false;

        if (!success)
        {
            _error = error;
        }
        else if (needsEmailConfirmation)
        {
            _info = "Compte créé. Vérifiez votre boîte mail pour confirmer votre adresse avant de vous connecter.";
            _mode = Mode.Login;
        }
        else
        {
            Navigation.NavigateTo("");
        }
    }

    private class Credentials
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MinLength(6)]
        public string Password { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;
    }
}
