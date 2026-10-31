using TicketCore.Common;
using TicketCore.Engine.Calendars;
using TicketCore.Engine.Sla;

namespace TicketCore.Engine.Tests;

public class SlaRulesTests
{
    private static readonly BusinessCalendar Always = BusinessCalendar.AlwaysOpen;
    private static readonly DateTime T0 = new(2026, 11, 2, 4, 30, 0, DateTimeKind.Utc);

    private static readonly EscalationLevelDefinition[] ThreeLevels =
    [
        new(1, 3, "Team Lead", 30),
        new(2, 4, "Head of Department", 60),
        new(3, 5, "Business Unit Head", null)
    ];

    // Urgent: first response 60, next response 120, resolution 480, reminder 15; escalates first response and resolution.
    private static SlaPolicyDefinition Urgent(int? deskId = null, int? typeId = null, int id = 1) =>
        new(id, "Urgent", deskId, typeId, 1, 60, 120, 480, 15, true, false, true, ThreeLevels);

    private static DateTime At(int minutes) => T0.AddMinutes(minutes);

    [Fact]
    public void Most_specific_policy_wins()
    {
        var global = Urgent(id: 1);
        var desk = Urgent(deskId: 7, id: 2);
        var type = Urgent(typeId: 2, id: 3);
        var deskAndType = Urgent(deskId: 7, typeId: 2, id: 4);
        var all = new[] { global, desk, type, deskAndType };

        Assert.Equal(4, SlaRules.SelectPolicy(all, 7, 2, 1)!.SlaPolicyId);
        Assert.Equal(2, SlaRules.SelectPolicy(all, 7, 1, 1)!.SlaPolicyId);
        Assert.Equal(3, SlaRules.SelectPolicy(all, 8, 2, 1)!.SlaPolicyId);
        Assert.Equal(1, SlaRules.SelectPolicy(all, 8, 1, 1)!.SlaPolicyId);
        Assert.Null(SlaRules.SelectPolicy(all, 8, 1, 2)); // no policy for this priority
    }

    [Fact]
    public void Start_sets_due_and_reminder_times_from_creation()
    {
        var s = SlaRules.Start(10, T0, Urgent(), 1, Always);
        Assert.Equal(At(60), s.FirstResponseDueAt);
        Assert.Equal(At(45), s.FirstResponseRemindAt);
        Assert.Equal(At(480), s.ResolutionDueAt);
        Assert.Equal(At(465), s.ResolutionRemindAt);
        Assert.Equal(At(45), s.NextEventAt);
    }

    [Fact]
    public void Reminder_then_breach_each_fire_once()
    {
        var s = SlaRules.Start(10, T0, Urgent(), 1, Always);

        Assert.Empty(SlaRules.Evaluate(s, At(44)));

        var reminder = Assert.Single(SlaRules.Evaluate(s, At(45)));
        Assert.Equal((SlaClock.FirstResponse, SlaEventKind.Reminder), (reminder.Clock, reminder.Kind));
        Assert.Empty(SlaRules.Evaluate(s, At(50))); // not twice
        Assert.Equal(At(60), s.NextEventAt);

        var breach = Assert.Single(SlaRules.Evaluate(s, At(61)));
        Assert.Equal((SlaClock.FirstResponse, SlaEventKind.Breach), (breach.Clock, breach.Kind));
        Assert.Empty(SlaRules.Evaluate(s, At(62)));
        Assert.Equal(At(465), s.NextEventAt); // next: resolution reminder
    }

    [Fact]
    public void Worker_catching_up_late_records_the_breach_not_a_stale_reminder()
    {
        var s = SlaRules.Start(10, T0, Urgent(), 1, Always);
        var events = SlaRules.Evaluate(s, At(70)); // worker was down past both the reminder and the due time
        Assert.Contains(events, e => e is { Clock: SlaClock.FirstResponse, Kind: SlaEventKind.Breach });
        Assert.DoesNotContain(events, e => e is { Clock: SlaClock.FirstResponse, Kind: SlaEventKind.Reminder });
    }

    [Fact]
    public void Support_reply_meets_first_response()
    {
        var s = SlaRules.Start(10, T0, Urgent(), 1, Always);
        SlaRules.SupportReplied(s, At(20));
        Assert.Equal(At(20), s.FirstResponseMetAt);
        Assert.Empty(SlaRules.Evaluate(s, At(100)));
        Assert.Equal(At(465), s.NextEventAt);
    }

    [Fact]
    public void Requester_reply_after_first_response_starts_next_response()
    {
        var s = SlaRules.Start(10, T0, Urgent(), 1, Always);
        SlaRules.RequesterReplied(s, At(10), Urgent(), Always);
        Assert.Null(s.NextResponseDueAt); // first response not met yet: that clock covers it

        SlaRules.SupportReplied(s, At(20));
        SlaRules.RequesterReplied(s, At(30), Urgent(), Always);
        Assert.Equal(At(150), s.NextResponseDueAt);

        SlaRules.SupportReplied(s, At(40));
        Assert.Null(s.NextResponseDueAt);
    }

    [Fact]
    public void Awaiting_requester_pauses_resolution_and_adds_the_paused_time()
    {
        var policy = Urgent();
        var s = SlaRules.Start(10, T0, policy, 1, Always);
        SlaRules.SupportReplied(s, At(10));

        SlaRules.StatusChanged(s, TicketStatus.InProgress, TicketStatus.AwaitingRequester, At(100), T0, policy, Always);
        Assert.Empty(SlaRules.Evaluate(s, At(600))); // past the original resolution due time, but paused
        Assert.Null(s.NextEventAt);

        SlaRules.StatusChanged(s, TicketStatus.AwaitingRequester, TicketStatus.InProgress, At(700), T0, policy, Always);
        Assert.Equal(600, s.PausedMinutes);
        Assert.Equal(At(480 + 600), s.ResolutionDueAt);
        Assert.Null(s.PausedAt);
    }

    [Fact]
    public void Paused_time_counts_only_business_minutes()
    {
        var india = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        var weekdays = new BusinessCalendar(india, false,
            new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
                .Select(d => new WorkingWindow(d, 540, 1080)), []);
        var policy = Urgent();
        var friday17 = new DateTime(2026, 11, 6, 11, 30, 0, DateTimeKind.Utc);  // Fri 17:00 IST
        var monday10 = new DateTime(2026, 11, 9, 4, 30, 0, DateTimeKind.Utc);   // Mon 10:00 IST

        var s = SlaRules.Start(10, friday17.AddHours(-1), policy, 1, weekdays);
        SlaRules.StatusChanged(s, TicketStatus.InProgress, TicketStatus.AwaitingRequester, friday17, s.ResolutionDueAt!.Value, policy, weekdays);
        SlaRules.StatusChanged(s, TicketStatus.AwaitingRequester, TicketStatus.InProgress, monday10, friday17.AddHours(-1), policy, weekdays);

        Assert.Equal(120, s.PausedMinutes); // Fri 17-18 + Mon 09-10, not the whole weekend
    }

    [Fact]
    public void Resolving_meets_resolution_and_reopening_restarts_it()
    {
        var policy = Urgent();
        var s = SlaRules.Start(10, T0, policy, 1, Always);
        SlaRules.SupportReplied(s, At(10));

        SlaRules.StatusChanged(s, TicketStatus.InProgress, TicketStatus.Resolved, At(100), T0, policy, Always);
        Assert.Equal(At(100), s.ResolutionMetAt);
        Assert.Null(s.NextEventAt);

        SlaRules.StatusChanged(s, TicketStatus.Resolved, TicketStatus.InProgress, At(200), T0, policy, Always);
        Assert.Null(s.ResolutionMetAt);
        Assert.Equal(At(465), s.NextEventAt);
    }

    [Fact]
    public void Priority_change_recalculates_from_creation_but_keeps_met_clocks()
    {
        var urgent = Urgent();
        var low = new SlaPolicyDefinition(9, "Low", null, null, 4, 480, 960, 2400, 120, false, false, false, []);
        var s = SlaRules.Start(10, T0, urgent, 1, Always);
        SlaRules.SupportReplied(s, At(10));

        SlaRules.Recalculate(s, T0, low, Always);
        Assert.Equal(At(10), s.FirstResponseMetAt);
        Assert.Equal(At(2400), s.ResolutionDueAt);
        Assert.Equal(9, s.SlaPolicyId);
    }

    [Fact]
    public void Breach_starts_escalation_and_a_second_breach_adds_a_reason()
    {
        var policy = Urgent();
        var (started, step) = SlaRules.OnBreach(null, SlaClock.FirstResponse, 10, At(60), policy, Always);
        Assert.Equal(SlaRules.EscalationStep.Started, step);
        Assert.Equal(1, started!.Level);
        Assert.Equal(At(90), started.WindowEndsAt);

        var (same, step2) = SlaRules.OnBreach(started, SlaClock.Resolution, 10, At(480), policy, Always);
        Assert.Equal(SlaRules.EscalationStep.ReasonAdded, step2);
        Assert.Equal(1, same!.Level); // a second reason does not jump a level
        Assert.Equal(2, same.Reasons.Count);
    }

    [Fact]
    public void Clocks_the_policy_does_not_escalate_do_nothing()
    {
        var (state, step) = SlaRules.OnBreach(null, SlaClock.NextResponse, 10, At(60), Urgent(), Always);
        Assert.Null(state);
        Assert.Equal(SlaRules.EscalationStep.None, step);
    }

    [Fact]
    public void Escalation_walks_up_the_levels_then_follows_up()
    {
        var policy = Urgent();
        var (e, _) = SlaRules.OnBreach(null, SlaClock.FirstResponse, 10, At(60), policy, Always);

        Assert.Equal(SlaRules.EscalationStep.None, SlaRules.Advance(e!, At(89), policy, Always));
        Assert.Equal(SlaRules.EscalationStep.Advanced, SlaRules.Advance(e!, At(90), policy, Always));
        Assert.Equal(2, e!.Level);
        Assert.Equal(At(150), e.WindowEndsAt);

        Assert.Equal(SlaRules.EscalationStep.Advanced, SlaRules.Advance(e, At(150), policy, Always));
        Assert.Equal(3, e.Level);
        Assert.Null(e.WindowEndsAt); // final level

        Assert.Equal(SlaRules.EscalationStep.None, SlaRules.Advance(e, At(150 + 239), policy, Always));
        Assert.Equal(SlaRules.EscalationStep.FollowUp, SlaRules.Advance(e, At(150 + 240), policy, Always));
        Assert.Equal(SlaRules.EscalationStep.None, SlaRules.Advance(e, At(150 + 300), policy, Always));
        Assert.Equal(3, e.Level);
    }

    [Fact]
    public void Escalation_clears_only_when_every_reason_is_met()
    {
        var policy = Urgent();
        var (e, _) = SlaRules.OnBreach(null, SlaClock.FirstResponse, 10, At(60), policy, Always);
        SlaRules.OnBreach(e, SlaClock.Resolution, 10, At(480), policy, Always);

        Assert.False(SlaRules.Clear(e!, SlaRules.ClocksMetBy(supportPublicReply: true, newStatus: null), At(500)));
        Assert.Null(e!.ClearedAt);

        Assert.True(SlaRules.Clear(e, SlaRules.ClocksMetBy(false, TicketStatus.Resolved), At(520)));
        Assert.Equal(At(520), e.ClearedAt);
    }

    [Fact]
    public void Resolving_clears_an_escalation_for_an_unanswered_ticket()
    {
        // Resolved without a reply (e.g. a duplicate): nothing is left to escalate.
        var (e, _) = SlaRules.OnBreach(null, SlaClock.FirstResponse, 10, At(60), Urgent(), Always);
        Assert.True(SlaRules.Clear(e!, SlaRules.ClocksMetBy(false, TicketStatus.Resolved), At(70)));
        Assert.Equal(At(70), e!.ClearedAt);
    }

    [Fact]
    public void Internal_note_meets_nothing()
    {
        Assert.Empty(SlaRules.ClocksMetBy(supportPublicReply: false, newStatus: null));
    }
}
