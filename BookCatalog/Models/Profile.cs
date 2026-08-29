using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace BookCatalog.Models;

[Table("profiles")]
public class Profile : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("display_name")]
    public string? DisplayName { get; set; }

    [Column("role")]
    public string Role { get; set; } = "readonly";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>Mirror of auth.users.last_sign_in_at, maintained by the on_auth_user_sign_in trigger. Null until the user signs in at least once after the trigger was installed.</summary>
    [Column("last_sign_in_at")]
    public DateTime? LastSignInAt { get; set; }

    public bool IsSuperAdmin => Role == "superadmin";

    /// <summary>True for both 'admin' and 'superadmin' — superadmin is a superset of admin.</summary>
    public bool IsAdmin => Role is "admin" or "superadmin";
}
