using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace BookCatalog.Models;

[Table("books")]
public class Book : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("title")]
    public string Title { get; set; } = string.Empty;

    [Column("author")]
    public string Author { get; set; } = string.Empty;

    [Column("isbn")]
    public string? Isbn { get; set; }

    [Column("collection_id")]
    public Guid CollectionId { get; set; }

    [Column("photo_url_1")]
    public string? PhotoUrl1 { get; set; }

    [Column("photo_url_2")]
    public string? PhotoUrl2 { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }
}
