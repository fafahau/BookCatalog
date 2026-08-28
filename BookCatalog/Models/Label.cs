using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace BookCatalog.Models;

[Table("labels")]
public class Label : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;
}
