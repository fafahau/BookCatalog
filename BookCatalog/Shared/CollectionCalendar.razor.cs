using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace BookCatalog.Shared;

/// <summary>
/// Month grid showing how many books were added to a collection on each day
/// (bucketed by local date of <c>created_at</c>). Rendered admin/superadmin-only
/// by <see cref="Pages.CollectionDetail"/>.
/// </summary>
public partial class CollectionCalendar
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly string[] WeekdayLabels = { "Lun", "Mar", "Mer", "Jeu", "Ven", "Sam", "Dim" };

    [Parameter, EditorRequired]
    public Guid CollectionId { get; set; }

    private readonly Dictionary<DateOnly, int> _counts = new();
    private DateOnly _month = FirstOfMonth(DateTime.Today);
    private List<DayCell> _cells = new();
    private int _monthTotal;
    private bool _loading = true;

    private sealed record DayCell(DateOnly Date, bool InMonth, int Count, bool IsToday);

    private string MonthLabel
    {
        get
        {
            var s = _month.ToString("MMMM yyyy", Fr);
            return char.ToUpper(s[0], Fr) + s[1..];
        }
    }

    protected override async Task OnInitializedAsync()
    {
        var dates = await BookService.GetCreatedAtByCollectionAsync(CollectionId);
        foreach (var date in dates)
        {
            var key = DateOnly.FromDateTime(date.ToLocalTime());
            _counts[key] = _counts.GetValueOrDefault(key) + 1;
        }

        _loading = false;
        BuildCells();
    }

    private void PrevMonth()
    {
        _month = _month.AddMonths(-1);
        BuildCells();
    }

    private void NextMonth()
    {
        _month = _month.AddMonths(1);
        BuildCells();
    }

    private void BuildCells()
    {
        // Grid starts on the Monday on or before the 1st; always 6 rows so the
        // layout doesn't jump between months.
        var offset = ((int)_month.ToDateTime(TimeOnly.MinValue).DayOfWeek + 6) % 7;
        var start = _month.AddDays(-offset);
        var today = DateOnly.FromDateTime(DateTime.Today);

        _cells = Enumerable.Range(0, 42)
            .Select(i =>
            {
                var date = start.AddDays(i);
                var inMonth = date.Month == _month.Month && date.Year == _month.Year;
                return new DayCell(date, inMonth, _counts.GetValueOrDefault(date), date == today);
            })
            .ToList();

        _monthTotal = _counts
            .Where(kv => kv.Key.Month == _month.Month && kv.Key.Year == _month.Year)
            .Sum(kv => kv.Value);
    }

    private static string CellClass(DayCell cell)
    {
        var classes = new List<string> { "calendar-day" };
        if (!cell.InMonth)
        {
            classes.Add("muted");
        }
        if (cell.Count > 0)
        {
            classes.Add("has-books");
        }
        if (cell.IsToday)
        {
            classes.Add("today");
        }
        return string.Join(' ', classes);
    }

    private static string DayTitle(DayCell cell)
    {
        var plural = cell.Count > 1 ? "s" : "";
        return $"{cell.Count} livre{plural} ajouté{plural} le {cell.Date:dd/MM/yyyy}";
    }

    private static DateOnly FirstOfMonth(DateTime day) => new(day.Year, day.Month, 1);
}
