using BookCatalog.Models;
using Microsoft.JSInterop;
using Newtonsoft.Json;

namespace BookCatalog.Services;

/// <summary>
/// A flattened, self-contained copy of everything needed to browse the catalogue
/// with no network: collections, every book (with its label names already
/// resolved), and the full list of label names for the filter datalists.
/// </summary>
public sealed class LibrarySnapshot
{
    public DateTime SyncedAt { get; set; }
    public List<SnapshotCollection> Collections { get; set; } = new();
    public List<SnapshotBook> Books { get; set; } = new();
    public List<string> Labels { get; set; } = new();
}

public sealed class SnapshotCollection
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    public BookCollection ToModel() => new()
    {
        Id = Id,
        Name = Name,
        CreatedAt = CreatedAt,
        CreatedBy = CreatedBy,
    };

    public static SnapshotCollection From(BookCollection c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        CreatedAt = c.CreatedAt,
        CreatedBy = c.CreatedBy,
    };
}

public sealed class SnapshotBook
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? Isbn { get; set; }
    public Guid CollectionId { get; set; }
    public string? PhotoUrl1 { get; set; }
    public string? PhotoUrl2 { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public List<string> LabelNames { get; set; } = new();

    public Book ToModel() => new()
    {
        Id = Id,
        Title = Title,
        Author = Author,
        Isbn = Isbn,
        CollectionId = CollectionId,
        PhotoUrl1 = PhotoUrl1,
        PhotoUrl2 = PhotoUrl2,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
        CreatedBy = CreatedBy,
        LabelNames = new List<string>(LabelNames),
    };

    public static SnapshotBook From(Book b, IReadOnlyList<string> labelNames) => new()
    {
        Id = b.Id,
        Title = b.Title,
        Author = b.Author,
        Isbn = b.Isbn,
        CollectionId = b.CollectionId,
        PhotoUrl1 = b.PhotoUrl1,
        PhotoUrl2 = b.PhotoUrl2,
        CreatedAt = b.CreatedAt,
        UpdatedAt = b.UpdatedAt,
        CreatedBy = b.CreatedBy,
        LabelNames = labelNames.ToList(),
    };
}

/// <summary>A cached profile, tied to the user it belongs to so a different sign-in can't inherit it.</summary>
public sealed class CachedProfile
{
    public Guid UserId { get; set; }
    public string Role { get; set; } = "readonly";
    public string? DisplayName { get; set; }
}

/// <summary>
/// Synchronous localStorage persistence for the offline library snapshot and the
/// last-known profile. WebAssembly's JS runtime is always in-process, so the
/// synchronous calls are safe (same trick as <see cref="UiStateStore"/> and
/// <see cref="LocalStorageSessionPersistence"/>).
/// </summary>
public sealed class OfflineLibraryStore
{
    private const string SnapshotKey = "bookcatalog.offline.library";
    private const string ProfileKey = "bookcatalog.offline.profile";

    private readonly IJSInProcessRuntime _js;

    public OfflineLibraryStore(IJSInProcessRuntime js)
    {
        _js = js;
    }

    public LibrarySnapshot? LoadSnapshot()
    {
        try
        {
            var json = _js.Invoke<string?>("localStorage.getItem", SnapshotKey);
            return string.IsNullOrEmpty(json) ? null : JsonConvert.DeserializeObject<LibrarySnapshot>(json);
        }
        catch
        {
            return null;
        }
    }

    public void SaveSnapshot(LibrarySnapshot snapshot)
    {
        try
        {
            _js.InvokeVoid("localStorage.setItem", SnapshotKey, JsonConvert.SerializeObject(snapshot));
        }
        catch
        {
            // Storage full or unavailable (private browsing) — the snapshot just won't persist.
        }
    }

    public CachedProfile? LoadProfile()
    {
        try
        {
            var json = _js.Invoke<string?>("localStorage.getItem", ProfileKey);
            return string.IsNullOrEmpty(json) ? null : JsonConvert.DeserializeObject<CachedProfile>(json);
        }
        catch
        {
            return null;
        }
    }

    public void SaveProfile(CachedProfile profile)
    {
        try
        {
            _js.InvokeVoid("localStorage.setItem", ProfileKey, JsonConvert.SerializeObject(profile));
        }
        catch
        {
        }
    }
}
