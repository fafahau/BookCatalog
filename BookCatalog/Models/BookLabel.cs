using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace BookCatalog.Models;

/// <summary>Join row linking a <see cref="Book"/> to a <see cref="Label"/> (many-to-many).</summary>
[Table("book_labels")]
public class BookLabel : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("book_id")]
    public Guid BookId { get; set; }

    [Column("label_id")]
    public Guid LabelId { get; set; }
}
