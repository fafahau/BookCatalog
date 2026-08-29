namespace BookCatalog.Layout;

public partial class RedirectToLogin
{
    protected override void OnInitialized()
    {
        Navigation.NavigateTo("login");
    }
}
