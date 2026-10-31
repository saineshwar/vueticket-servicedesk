using TicketCore.Engine.Calendars;
using TicketCore.Engine.Sla;

namespace TicketCore.Engine.Tests;

public class BusinessCalendarTests
{
    private static readonly TimeZoneInfo India = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    /// <summary>Monday to Friday 09:00-18:00, India time (UTC+05:30, no daylight saving).</summary>
    private static BusinessCalendar Weekdays(params DateOnly[] holidays) =>
        new(India, false, WeekdayWindows(540, 1080), holidays);

    private static IEnumerable<WorkingWindow> WeekdayWindows(int start, int end) =>
        new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
            .Select(d => new WorkingWindow(d, start, end));

    /// <summary>India local wall-clock time as UTC. 2 Nov 2026 is a Monday.</summary>
    private static DateTime Ist(int day, int hour, int minute = 0, int month = 11) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, month, day, hour, minute, 0, DateTimeKind.Unspecified), India);

    [Fact]
    public void Adds_within_the_same_working_day()
    {
        Assert.Equal(Ist(2, 11), Weekdays().AddBusinessMinutes(Ist(2, 10), 60));
    }

    [Fact]
    public void Ticket_after_closing_starts_counting_at_next_opening()
    {
        // The old SQL gave after-hours tickets extra time; here 20:00 + 60 min is 10:00 next morning.
        Assert.Equal(Ist(3, 10), Weekdays().AddBusinessMinutes(Ist(2, 20), 60));
    }

    [Fact]
    public void Ticket_before_opening_starts_counting_at_opening()
    {
        // The old SQL counted the minutes before opening as working time.
        Assert.Equal(Ist(2, 9, 30), Weekdays().AddBusinessMinutes(Ist(2, 7), 30));
    }

    [Fact]
    public void Weekend_is_skipped()
    {
        // Friday 17:30 + 60 = 30 min on Friday + 30 min on Monday.
        Assert.Equal(Ist(9, 9, 30), Weekdays().AddBusinessMinutes(Ist(6, 17, 30), 60));
    }

    [Fact]
    public void Holiday_is_skipped()
    {
        // Wednesday 4 Nov is a holiday: Tuesday 17:30 + 60 lands on Thursday 09:30.
        var calendar = Weekdays(new DateOnly(2026, 11, 4));
        Assert.Equal(Ist(5, 9, 30), calendar.AddBusinessMinutes(Ist(3, 17, 30), 60));
    }

    [Fact]
    public void Several_windows_in_a_day_skip_the_gap()
    {
        // 09:00-13:00 and 14:00-18:00: 12:30 + 60 = 30 before lunch + 30 after.
        var calendar = new BusinessCalendar(India, false, WeekdayWindows(540, 780).Concat(WeekdayWindows(840, 1080)), []);
        Assert.Equal(Ist(2, 14, 30), calendar.AddBusinessMinutes(Ist(2, 12, 30), 60));
    }

    [Fact]
    public void Due_time_can_land_exactly_on_closing()
    {
        Assert.Equal(Ist(2, 18), Weekdays().AddBusinessMinutes(Ist(2, 17), 60));
    }

    [Fact]
    public void Hours_and_minutes_both_count()
    {
        // The old SQL dropped the hours when minutes were also set: 4 h 30 min became 30 min.
        Assert.Equal(Ist(2, 13, 30), Weekdays().AddBusinessMinutes(Ist(2, 9), 4 * 60 + 30));
    }

    [Fact]
    public void Plan_example_friday_evening_into_saturday()
    {
        // Monday to Saturday 08:30-19:30: an Urgent ticket at Fri 18:50 with a 60-minute first response is due Sat 08:50.
        var monToSat = Enum.GetValues<DayOfWeek>().Where(d => d != DayOfWeek.Sunday).Select(d => new WorkingWindow(d, 510, 1170));
        var calendar = new BusinessCalendar(India, false, monToSat, []);
        Assert.Equal(Ist(7, 8, 50), calendar.AddBusinessMinutes(Ist(6, 18, 50), 60));
    }

    [Fact]
    public void Always_open_calendar_adds_plain_minutes()
    {
        var start = new DateTime(2026, 11, 7, 23, 30, 0, DateTimeKind.Utc);
        Assert.Equal(start.AddMinutes(90), BusinessCalendar.AlwaysOpen.AddBusinessMinutes(start, 90));
        Assert.Equal(90, BusinessCalendar.AlwaysOpen.BusinessMinutesBetween(start, start.AddMinutes(90)));
    }

    [Fact]
    public void Minutes_between_count_only_working_time()
    {
        // Fri 17:00 -> Mon 10:00 is 60 minutes on Friday and 60 on Monday.
        Assert.Equal(120, Weekdays().BusinessMinutesBetween(Ist(6, 17), Ist(9, 10)));
        Assert.Equal(0, Weekdays().BusinessMinutesBetween(Ist(7, 10), Ist(8, 22))); // whole weekend
        Assert.Equal(0, Weekdays().BusinessMinutesBetween(Ist(9, 10), Ist(6, 17))); // reversed
    }

    [Theory]
    [InlineData(2, 10, true)]
    [InlineData(2, 18, false)]  // closing time is exclusive
    [InlineData(2, 8, false)]
    [InlineData(7, 11, false)]  // Saturday
    public void Is_open_follows_the_windows(int day, int hour, bool expected)
    {
        Assert.Equal(expected, Weekdays().IsOpen(Ist(day, hour)));
    }

    [Fact]
    public void Adding_then_measuring_round_trips()
    {
        var calendar = Weekdays(new DateOnly(2026, 11, 4));
        var random = new Random(42);
        for (var i = 0; i < 500; i++)
        {
            var start = Ist(2, 0).AddMinutes(random.Next(0, 14 * 24 * 60));
            var minutes = random.Next(1, 5 * 540);
            var due = calendar.AddBusinessMinutes(start, minutes);
            Assert.Equal(minutes, calendar.BusinessMinutesBetween(start, due));
        }
    }

    [Fact]
    public void Daylight_saving_gap_never_produces_a_missing_local_time()
    {
        // London clocks jump from 01:00 to 02:00 on Sunday 29 March 2026. With a Sunday 00:00-06:00 window,
        // 00:30 + 60 minutes lands in the skipped hour and is moved to 02:00 local (01:00 UTC).
        var calendar = new BusinessCalendar(London, false, [new WorkingWindow(DayOfWeek.Sunday, 0, 360)], []);
        var start = new DateTime(2026, 3, 29, 0, 30, 0, DateTimeKind.Utc); // 00:30 GMT
        var due = calendar.AddBusinessMinutes(start, 60);
        Assert.Equal(new DateTime(2026, 3, 29, 1, 0, 0, DateTimeKind.Utc), due);
        Assert.False(London.IsInvalidTime(TimeZoneInfo.ConvertTimeFromUtc(due, London)));
    }

    [Fact]
    public void Calendar_without_working_time_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new BusinessCalendar(India, false, [], []));
        Assert.Throws<ArgumentException>(() => new BusinessCalendar(India, false, [new WorkingWindow(DayOfWeek.Monday, 600, 540)], []));
    }

    [Fact]
    public void Database_times_without_a_kind_are_treated_as_utc()
    {
        var unspecified = DateTime.SpecifyKind(Ist(2, 10), DateTimeKind.Unspecified);
        Assert.Equal(Ist(2, 11), Weekdays().AddBusinessMinutes(unspecified, 60));
    }
}
