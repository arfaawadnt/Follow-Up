using FollowUp.Domain.Common;

namespace FollowUp.Domain.Laboratories;

/// <summary>
/// A laboratory's collection schedule: on which weekdays it is visited (<see cref="WorkDays"/>) and at
/// which times of day (<see cref="VisitTimes"/>). The midnight board job reads this to generate each
/// day's board (Workflows §2/§4).
/// </summary>
public sealed class VisitSchedule : ValueObject
{
    private readonly SortedSet<DayOfWeek> _workDays;
    private readonly List<TimeOnly> _visitTimes;

    private VisitSchedule(IEnumerable<DayOfWeek> workDays, IEnumerable<TimeOnly> visitTimes)
    {
        _workDays = new SortedSet<DayOfWeek>(workDays);
        _visitTimes = visitTimes.Distinct().OrderBy(t => t).ToList();
    }

    public IReadOnlyCollection<DayOfWeek> WorkDays => _workDays;
    public IReadOnlyList<TimeOnly> VisitTimes => _visitTimes;

    public static VisitSchedule Create(IEnumerable<DayOfWeek> workDays, IEnumerable<TimeOnly> visitTimes)
    {
        var days = workDays?.ToArray() ?? Array.Empty<DayOfWeek>();
        var times = visitTimes?.ToArray() ?? Array.Empty<TimeOnly>();
        if (days.Length > 0 && times.Length == 0)
            throw new DomainException("A schedule with work days must define at least one visit time.");
        return new VisitSchedule(days, times);
    }

    public static VisitSchedule Empty => new(Array.Empty<DayOfWeek>(), Array.Empty<TimeOnly>());

    /// <summary>How many board visits this lab generates on the given date (0 when not a work day).</summary>
    public int OccurrencesOn(DateOnly date) =>
        _workDays.Contains(date.DayOfWeek) ? _visitTimes.Count : 0;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        foreach (var d in _workDays) yield return d;
        yield return "|";
        foreach (var t in _visitTimes) yield return t;
    }
}
