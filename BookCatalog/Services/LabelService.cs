using BookCatalog.Models;
using Supabase.Postgrest;
using Supabase.Postgrest.Exceptions;

namespace BookCatalog.Services;

/// <summary>
/// Labels live in their own <c>labels</c> table and link to books many-to-many
/// through <c>book_labels</c> (each book carries 0-N labels).
/// </summary>
public class LabelService
{
    private readonly Supabase.Client _client;

    public LabelService(SupabaseService supabaseService)
    {
        _client = supabaseService.Client;
    }

    public async Task<List<Label>> GetAllAsync()
    {
        var result = await _client.From<Label>()
            .Order("name", Constants.Ordering.Ascending)
            .Get();
        return result.Models;
    }

    /// <summary>Distinct label names, alphabetical — for autocomplete datalists.</summary>
    public async Task<List<string>> GetNamesAsync()
    {
        var labels = await GetAllAsync();
        return labels
            .Select(l => l.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Renames a label everywhere it is used — a single row update.</summary>
    public async Task RenameAsync(Guid id, string newName)
    {
        newName = newName.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        await _client.From<Label>()
            .Filter("id", Constants.Operator.Equals, id.ToString())
            .Set(l => l.Name, newName)
            .Update();
    }

    /// <summary>Deletes a label; the <c>book_labels</c> rows go with it via ON DELETE CASCADE.</summary>
    public async Task DeleteAsync(Guid id)
    {
        await _client.From<Label>()
            .Filter("id", Constants.Operator.Equals, id.ToString())
            .Delete();
    }

    /// <summary>
    /// Creates a new label. Returns <c>null</c> if one with the same name already
    /// exists (case-insensitive) — the caller can surface that to the user.
    /// </summary>
    public async Task<Label?> CreateAsync(string name)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (await FindByNameAsync(name) != null)
        {
            return null;
        }

        try
        {
            var inserted = await _client.From<Label>().Insert(new Label { Name = name });
            return inserted.Models.First();
        }
        catch (PostgrestException)
        {
            // Lost a race on the unique(lower(name)) index — treat as "already exists".
            return null;
        }
    }

    /// <summary>Finds a label by name (case-insensitive) or creates it.</summary>
    public async Task<Label> GetOrCreateAsync(string name)
    {
        name = name.Trim();

        var existing = await FindByNameAsync(name);
        if (existing != null)
        {
            return existing;
        }

        try
        {
            var inserted = await _client.From<Label>().Insert(new Label { Name = name });
            return inserted.Models.First();
        }
        catch (PostgrestException)
        {
            // Lost a race on the unique(lower(name)) index — the other writer won.
            var winner = await FindByNameAsync(name);
            if (winner != null)
            {
                return winner;
            }
            throw;
        }
    }

    private async Task<Label?> FindByNameAsync(string name)
    {
        // ILike without wildcards = case-insensitive exact match.
        var result = await _client.From<Label>()
            .Filter("name", Constants.Operator.ILike, name)
            .Get();
        return result.Models.FirstOrDefault();
    }

    /// <summary>Label names for a single book, alphabetical.</summary>
    public async Task<List<string>> GetNamesForBookAsync(Guid bookId)
    {
        var links = await _client.From<BookLabel>()
            .Filter("book_id", Constants.Operator.Equals, bookId.ToString())
            .Get();
        if (links.Models.Count == 0)
        {
            return new();
        }

        var nameById = await LabelNamesByIdAsync();
        return OrderedNames(links.Models, nameById);
    }

    /// <summary>Label names keyed by book id, for every book that has at least one label.</summary>
    public async Task<Dictionary<Guid, List<string>>> GetNamesByBookAsync()
    {
        var links = await _client.From<BookLabel>().Get();
        var map = new Dictionary<Guid, List<string>>();
        if (links.Models.Count == 0)
        {
            return map;
        }

        var nameById = await LabelNamesByIdAsync();
        foreach (var group in links.Models.GroupBy(l => l.BookId))
        {
            map[group.Key] = OrderedNames(group, nameById);
        }
        return map;
    }

    /// <summary>Makes the book's links match <paramref name="names"/> exactly, creating labels as needed.</summary>
    public async Task SetBookLabelsAsync(Guid bookId, IEnumerable<string> names)
    {
        var desired = names
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var desiredIds = new HashSet<Guid>();
        foreach (var name in desired)
        {
            desiredIds.Add((await GetOrCreateAsync(name)).Id);
        }

        var existing = await _client.From<BookLabel>()
            .Filter("book_id", Constants.Operator.Equals, bookId.ToString())
            .Get();
        var existingIds = existing.Models.Select(l => l.LabelId).ToHashSet();

        foreach (var labelId in desiredIds.Where(id => !existingIds.Contains(id)))
        {
            await _client.From<BookLabel>().Insert(new BookLabel { BookId = bookId, LabelId = labelId });
        }

        foreach (var labelId in existingIds.Where(id => !desiredIds.Contains(id)))
        {
            await _client.From<BookLabel>()
                .Filter("book_id", Constants.Operator.Equals, bookId.ToString())
                .Filter("label_id", Constants.Operator.Equals, labelId.ToString())
                .Delete();
        }
    }

    /// <summary>Adds the given labels to every book, keeping each book's existing labels.</summary>
    public async Task AddLabelsToBooksAsync(IReadOnlyCollection<Guid> bookIds, IEnumerable<string> names)
    {
        var wanted = names
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (bookIds.Count == 0 || wanted.Count == 0)
        {
            return;
        }

        var labelIds = new List<Guid>();
        foreach (var name in wanted)
        {
            labelIds.Add((await GetOrCreateAsync(name)).Id);
        }

        foreach (var bookId in bookIds)
        {
            var existing = await _client.From<BookLabel>()
                .Filter("book_id", Constants.Operator.Equals, bookId.ToString())
                .Get();
            var have = existing.Models.Select(l => l.LabelId).ToHashSet();

            foreach (var labelId in labelIds.Where(id => !have.Contains(id)))
            {
                await _client.From<BookLabel>().Insert(new BookLabel { BookId = bookId, LabelId = labelId });
            }
        }
    }

    private async Task<Dictionary<Guid, string>> LabelNamesByIdAsync()
    {
        var labels = await _client.From<Label>().Get();
        return labels.Models.ToDictionary(l => l.Id, l => l.Name);
    }

    private static List<string> OrderedNames(IEnumerable<BookLabel> links, IReadOnlyDictionary<Guid, string> nameById) => links
        .Select(link => nameById.GetValueOrDefault(link.LabelId))
        .Where(n => !string.IsNullOrWhiteSpace(n))
        .Select(n => n!)
        .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
        .ToList();
}
