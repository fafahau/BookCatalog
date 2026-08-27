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

    public bool IsAdmin => Role == "admin";
}
