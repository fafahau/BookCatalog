namespace BookCatalog;

/// <summary>
/// Maps a label name to one of a small fixed set of chip colors, so the same
/// label always renders in the same color everywhere and the several labels on
/// one book stay visually distinct. The palette itself lives in app.css as
/// <c>.label-chip--c0 … .label-chip--c{Count - 1}</c>.
/// </summary>
public static class LabelColor
{
    /// <summary>Number of colors in the palette (kept in sync with app.css).</summary>
    public const int Count = 8;

    /// <summary>Stable palette index in <c>[0, Count)</c> for a label name (case-insensitive).</summary>
    public static int IndexFor(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return 0;
        }

        // FNV-1a over the normalized name — deterministic across processes,
        // unlike string.GetHashCode().
        uint hash = 2166136261;
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            hash = (hash ^ ch) * 16777619;
        }

        return (int)(hash % Count);
    }

    /// <summary>Full class attribute for a label chip: <c>label-chip label-chip--cN</c>.</summary>
    public static string ClassFor(string? name) => $"label-chip label-chip--c{IndexFor(name)}";
}
