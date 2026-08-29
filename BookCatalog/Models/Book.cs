using Newtonsoft.Json;
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

    /// <summary>
    /// Label names attached to this book, resolved from the <c>book_labels</c> join
    /// by <see cref="Services.LabelService"/>. Not a column — never sent to the API.
    /// </summary>
    [JsonIgnore]
    public List<string> LabelNames { get; set; } = new();

    [Column("collection_id")]
    public Guid CollectionId { get; set; }

    [Column("photo_url_1")]
    public string? PhotoUrl1 { get; set; }

    [Column("photo_url_2")]
    public string? PhotoUrl2 { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Last change to the book: a direct edit, or a label linked / unlinked.
    /// Maintained by database triggers — never written from the client.
    /// </summary>
    [Column("updated_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public DateTime UpdatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }
}
