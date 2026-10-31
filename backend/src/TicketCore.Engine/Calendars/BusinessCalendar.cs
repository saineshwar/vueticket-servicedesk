namespace TicketCore.Engine.Calendars;

/// <summary>A working window on one weekday, in minutes from local midnight (end exclusive, up to 1440).</summary>
public sealed record WorkingWindow(DayOfWeek Day, int StartMinute, int EndMinute);

/// <summary>
/// Working time for one calendar: a time zone, weekly windows and holidays. All inputs and outputs are UTC.
/// Pure and independent of the database: this is the only place in the system that does business-time arithmetic.
/// </summary>
public sealed class BusinessCalendar
{
    private const int MaxDaysSearched = 800; // a calendar with almost no working time must not loop forever

    private readonly TimeZoneInfo _zone;
    private readonly bool _is24x7;
    private readonly IReadOnlyDictionary<DayOfWeek, (int Start, int End)[]> _windows;
    private readonly HashSet<DateOnly> _holidays;

    public BusinessCalendar(TimeZoneInfo zone, bool is24x7, IEnumerable<WorkingWindow> windows, IEnumerable<DateOnly> holidays)
    {
        _zone = zone;
        _is24x7 = is24x7;
        _windows = windows
            .GroupBy(w => w.Day)
            .ToDictionary(g => g.Key, g => g.OrderBy(w => w.StartMinute).Select(w => (w.StartMinute, w.EndMinute)).ToArray());
        _holidays = holidays.ToHashSet();

        if (!_is24x7 && _windows.Count == 0)
            throw new ArgumentException("A calendar that is not 24x7 needs at least one working window.");
        foreach (var (_, list) in _windows)
            foreach (var (start, end) in list)
                if (start < 0 || end > 1440 || start >= end)
                    throw new ArgumentException($"Invalid working window {start}-{end}.");
    }

    public static BusinessCalendar AlwaysOpen { get; } = new(TimeZoneInfo.Utc, true, [], []);

    /// <summary>The UTC moment when <paramref name="minutes"/> of working time have passed after <paramref name="startUtc"/>.</summary>
    public DateTime AddBusinessMinutes(DateTime startUtc, int minutes)
    {
        startUtc = AsUtc(startUtc);
        if (minutes <= 0) return startUtc;
        if (_is24x7) return startUtc.AddMinutes(minutes);

        var cursor = TimeZoneInfo.ConvertTimeFromUtc(startUtc, _zone);
        double remaining = minutes;

        for (var day = 0; day < MaxDaysSearched; day++)
        {
            var date = cursor.Date;
            foreach (var (windowStart, windowEnd) in WindowsOn(date))
            {
                var from = Max(cursor, windowStart);
                if (from >= windowEnd) continue;

                var available = (windowEnd - from).TotalMinutes;
                if (remaining <= available) return ToUtc(from.AddMinutes(remaining));
                remaining -= available;
            }
            cursor = date.AddDays(1);
        }
        throw new InvalidOperationException("The calendar has no working time in the next two years.");
    }

    /// <summary>Working minutes between two UTC moments (0 when <paramref name="toUtc"/> is not after <paramref name="fromUtc"/>).</summary>
    public int BusinessMinutesBetween(DateTime fromUtc, DateTime toUtc)
    {
        fromUtc = AsUtc(fromUtc);
        toUtc = AsUtc(toUtc);
        if (toUtc <= fromUtc) return 0;
        if (_is24x7) return (int)Math.Floor((toUtc - fromUtc).TotalMinutes);

        var from = TimeZoneInfo.ConvertTimeFromUtc(fromUtc, _zone);
        var to = TimeZoneInfo.ConvertTimeFromUtc(toUtc, _zone);
        double total = 0;
        for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
        {
            foreach (var (windowStart, windowEnd) in WindowsOn(date))
            {
                var start = Max(from, windowStart);
                var end = Min(to, windowEnd);
                if (end > start) total += (end - start).TotalMinutes;
            }
        }
        return (int)Math.Floor(total);
    }

    /// <summary>True when the moment falls inside working time.</summary>
    public bool IsOpen(DateTime utc)
    {
        if (_is24x7) return true;
        var local = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), _zone);
        return WindowsOn(local.Date).Any(w => local >= w.Start && local < w.End);
    }

    private IEnumerable<(DateTime Start, DateTime End)> WindowsOn(DateTime localDate)
    {
        if (_holidays.Contains(DateOnly.FromDateTime(localDate))) yield break;
        if (!_windows.TryGetValue(localDate.DayOfWeek, out var windows)) yield break;
        foreach (var (start, end) in windows)
            yield return (localDate.AddMinutes(start), localDate.AddMinutes(end));
    }

    /// <summary>
    /// Local wall-clock time to UTC. A time skipped by a daylight-saving jump is moved forward past the gap;
    /// an ambiguous time (clocks going back) uses the earlier (daylight) offset.
    /// </summary>
    private DateTime ToUtc(DateTime local)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (_zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        if (_zone.IsAmbiguousTime(local))
        {
            var offset = _zone.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        }
        return TimeZoneInfo.ConvertTimeToUtc(local, _zone);
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc) // the database stores UTC without a kind
    };

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
