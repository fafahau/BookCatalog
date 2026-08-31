using Microsoft.AspNetCore.Components;
using BookCatalog.Models;
using BookCatalog.Services;

namespace BookCatalog.Pages;

public partial class Users
{
    [CascadingParameter(Name = "IsOnline")]
    private bool IsOnline { get; set; } = true;

    private List<Profile> _profiles = new();
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _loading = true;
        _profiles = await UserService.GetAllAsync();
        _loading = false;
    }

    // Supabase.Postgrest already hands back timestamptz values converted to the
    // browser's local time (just tagged Unspecified), so calling ToLocalTime()
    // here would shift them a second time. Format the value as-is.
    private static string FormatDate(DateTime dt) =>
        dt.ToString("dd/MM/yyyy");

    private static string FormatDateTime(DateTime dt) =>
        dt.ToString("dd/MM/yyyy à HH:mm");

    // A superadmin row is only editable by another superadmin; you can never edit your own row.
    private bool IsRoleLocked(Profile profile) =>
        profile.Id == AuthService.CurrentUserId
        || (profile.IsSuperAdmin && !AuthService.IsSuperAdmin);

    // A superadmin profile can never be removed here (RLS enforces this too); nor can your own.
    private bool IsRemoveLocked(Profile profile) =>
        profile.Id == AuthService.CurrentUserId || profile.IsSuperAdmin;

    private async Task ChangeRoleAsync(Profile profile, string role)
    {
        // Only a superadmin may grant or touch the 'superadmin' role. The RLS
        // policy profiles_admin_update enforces this server-side too; this is
        // just a clean client-side stop so we never fire a doomed request.
        if (!IsOnline || ((role == "superadmin" || profile.IsSuperAdmin) && !AuthService.IsSuperAdmin))
        {
            return;
        }

        await UserService.SetRoleAsync(profile.Id, role);
        await ReloadAsync();
    }

    private async Task RemoveAsync(Profile profile)
    {
        if (!IsOnline)
        {
            return;
        }

        var who = string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.Id.ToString() : profile.DisplayName;
        var confirmed = await Confirm.ConfirmAsync(
            $"Retirer l'accès de {who} ?",
            title: "Retirer l'accès",
            confirmLabel: "Retirer");
        if (!confirmed)
        {
            return;
        }

        await UserService.RemoveAsync(profile.Id);
        await ReloadAsync();
    }
}
