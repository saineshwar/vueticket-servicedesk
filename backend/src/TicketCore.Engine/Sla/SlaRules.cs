using TicketCore.Common;
using TicketCore.Engine.Calendars;

namespace TicketCore.Engine.Sla;

/// <summary>
/// The SLA and escalation rules as pure functions: state + event + calendar in, new state out.
/// No database, no clock, no scheduler; the API and the worker only store and schedule what these decide.
/// </summary>
public static class SlaRules
{
    /// <summary>At the final escalation level the owner is reminded every 4 business hours until it is cleared.</summary>
    public const int FinalLevelFollowUpMinutes = 240;

    // ---------- Policy selection ----------

    /// <summary>The most specific active policy for the ticket's priority: desk + type, then desk, then type, then global.</summary>
    public static SlaPolicyDefinition? SelectPolicy(IEnumerable<SlaPolicyDefinition> policies, int serviceDeskId, int ticketTypeId, int priorityId) =>
        policies
            .Where(p => p.PriorityId == priorityId
                        && (p.ServiceDeskId is null || p.ServiceDeskId == serviceDeskId)
                        && (p.TicketTypeId is null || p.TicketTypeId == ticketTypeId))
            .OrderByDescending(p => (p.ServiceDeskId is null ? 0 : 2) + (p.TicketTypeId is null ? 0 : 1))
            .FirstOrDefault();

    // ---------- Clock changes (called by the API when tickets change) ----------

    /// <summary>A new ticket: first response and resolution start counting from creation.</summary>
    public static SlaState Start(long ticketId, DateTime createdAt, SlaPolicyDefinition policy, int calendarId, BusinessCalendar calendar)
    {
        var state = new SlaState { TicketId = ticketId, SlaPolicyId = policy.SlaPolicyId, CalendarId = calendarId };
        Recalculate(state, createdAt, policy, calendar);
        state.NextEventAt = NextEventAt(state);
        return state;
    }

    /// <summary>
    /// Sets due and reminder times from the ticket's creation time. Used at start and when the priority (policy) changes.
    /// Met clocks are left alone; a breach already recorded stays recorded.
    /// </summary>
    public static void Recalculate(SlaState state, DateTime createdAt, SlaPolicyDefinition policy, BusinessCalendar calendar)
    {
        state.SlaPolicyId = policy.SlaPolicyId;
        if (state.FirstResponseMetAt is null)
        {
            state.FirstResponseDueAt = calendar.AddBusinessMinutes(createdAt, policy.FirstResponseMinutes);
            state.FirstResponseRemindAt = RemindAt(createdAt, policy.FirstResponseMinutes, policy.ReminderMinutes, calendar);
            if (state.FirstResponseBreachedAt is null) state.FirstResponseRemindedAt = null;
        }
        if (state.ResolutionMetAt is null)
        {
            var minutes = policy.ResolutionMinutes + state.PausedMinutes;
            state.ResolutionDueAt = calendar.AddBusinessMinutes(createdAt, minutes);
            state.ResolutionRemindAt = RemindAt(createdAt, minutes, policy.ReminderMinutes, calendar);
            if (state.ResolutionBreachedAt is null) state.ResolutionRemindedAt = null;
        }
        state.NextEventAt = NextEventAt(state);
    }

    /// <summary>A public reply from support meets the first-response clock and the pending next-response clock.</summary>
    public static void SupportReplied(SlaState state, DateTime now)
    {
        state.FirstResponseMetAt ??= now;
        ClearNextResponse(state);
        state.NextEventAt = NextEventAt(state);
    }

    /// <summary>
    /// A requester reply after the first response starts the next-response clock.
    /// Before the first response, the first-response clock already covers it.
    /// </summary>
    public static void RequesterReplied(SlaState state, DateTime now, SlaPolicyDefinition policy, BusinessCalendar calendar)
    {
        if (state.FirstResponseMetAt is not null && state.PausedAt is null && state.ResolutionMetAt is null)
        {
            state.NextResponseDueAt = calendar.AddBusinessMinutes(now, policy.NextResponseMinutes);
            state.NextResponseRemindAt = RemindAt(now, policy.NextResponseMinutes, policy.ReminderMinutes, calendar);
            state.NextResponseRemindedAt = null;
            state.NextResponseBreachedAt = null;
        }
        state.NextEventAt = NextEventAt(state);
    }

    /// <summary>
    /// Awaiting the requester pauses resolution (and drops next response); leaving that status adds the paused business
    /// time to the resolution due time. Resolving or closing meets resolution; reopening restarts it.
    /// </summary>
    public static void StatusChanged(SlaState state, TicketStatus from, TicketStatus to, DateTime now, DateTime createdAt,
        SlaPolicyDefinition policy, BusinessCalendar calendar)
    {
        if (state.PausedAt is { } pausedAt && to != TicketStatus.AwaitingRequester)
        {
            state.PausedMinutes += calendar.BusinessMinutesBetween(pausedAt, now);
            state.PausedAt = null;
            Recalculate(state, createdAt, policy, calendar);
        }

        if (to == TicketStatus.AwaitingRequester && state.PausedAt is null)
        {
            state.PausedAt = now;
            ClearNextResponse(state);
        }

        if (to is TicketStatus.Resolved or TicketStatus.Closed)
        {
            state.ResolutionMetAt ??= now;
            ClearNextResponse(state);
        }
        else if (from is TicketStatus.Resolved or TicketStatus.Closed && state.ResolutionMetAt is not null)
        {
            // Reopened: resolution is no longer met. Its due time stays, so a reopened ticket may already be overdue.
            state.ResolutionMetAt = null;
            state.ResolutionRemindedAt = state.ResolutionBreachedAt is null ? null : state.ResolutionRemindedAt;
        }

        state.NextEventAt = NextEventAt(state);
    }

    // ---------- Monitoring (called by the worker) ----------

    /// <summary>Records every reminder and breach that is due at <paramref name="now"/> and returns them. Each fires once.</summary>
    public static IReadOnlyList<SlaEvent> Evaluate(SlaState state, DateTime now)
    {
        var events = new List<SlaEvent>();

        if (state.FirstResponseMetAt is null && state.FirstResponseDueAt is { } frDue)
        {
            if (state.FirstResponseBreachedAt is null && now >= frDue)
            {
                state.FirstResponseBreachedAt = now;
                events.Add(new SlaEvent(SlaClock.FirstResponse, SlaEventKind.Breach, frDue));
            }
            else if (state.FirstResponseRemindedAt is null && state.FirstResponseBreachedAt is null
                     && state.FirstResponseRemindAt is { } at && now >= at)
            {
                state.FirstResponseRemindedAt = now;
                events.Add(new SlaEvent(SlaClock.FirstResponse, SlaEventKind.Reminder, frDue));
            }
        }

        if (state.NextResponseDueAt is { } nrDue)
        {
            if (state.NextResponseBreachedAt is null && now >= nrDue)
            {
                state.NextResponseBreachedAt = now;
                events.Add(new SlaEvent(SlaClock.NextResponse, SlaEventKind.Breach, nrDue));
            }
            else if (state.NextResponseRemindedAt is null && state.NextResponseBreachedAt is null
                     && state.NextResponseRemindAt is { } at && now >= at)
            {
                state.NextResponseRemindedAt = now;
                events.Add(new SlaEvent(SlaClock.NextResponse, SlaEventKind.Reminder, nrDue));
            }
        }

        // Resolution does not run while awaiting the requester.
        if (state.ResolutionMetAt is null && state.PausedAt is null && state.ResolutionDueAt is { } resDue)
        {
            if (state.ResolutionBreachedAt is null && now >= resDue)
            {
                state.ResolutionBreachedAt = now;
                events.Add(new SlaEvent(SlaClock.Resolution, SlaEventKind.Breach, resDue));
            }
            else if (state.ResolutionRemindedAt is null && state.ResolutionBreachedAt is null
                     && state.ResolutionRemindAt is { } at && now >= at)
            {
                state.ResolutionRemindedAt = now;
                events.Add(new SlaEvent(SlaClock.Resolution, SlaEventKind.Reminder, resDue));
            }
        }

        state.NextEventAt = NextEventAt(state);
        return events;
    }

    /// <summary>The earliest pending reminder or breach, or null when nothing is pending.</summary>
    public static DateTime? NextEventAt(SlaState s)
    {
        var candidates = new List<DateTime?>();
        if (s.FirstResponseMetAt is null && s.FirstResponseBreachedAt is null)
        {
            if (s.FirstResponseRemindedAt is null) candidates.Add(s.FirstResponseRemindAt);
            candidates.Add(s.FirstResponseDueAt);
        }
        if (s.NextResponseDueAt is not null && s.NextResponseBreachedAt is null)
        {
            if (s.NextResponseRemindedAt is null) candidates.Add(s.NextResponseRemindAt);
            candidates.Add(s.NextResponseDueAt);
        }
        if (s.ResolutionMetAt is null && s.ResolutionBreachedAt is null && s.PausedAt is null)
        {
            if (s.ResolutionRemindedAt is null) candidates.Add(s.ResolutionRemindAt);
            candidates.Add(s.ResolutionDueAt);
        }
        return candidates.Where(c => c is not null).Min();
    }

    // ---------- Escalation ----------

    public enum EscalationStep
    {
        None,
        Started,
        ReasonAdded,
        Advanced,
        FollowUp
    }

    /// <summary>A breach on a clock the policy escalates starts level 1, or adds a reason to the active escalation.</summary>
    public static (EscalationState? State, EscalationStep Step) OnBreach(EscalationState? active, string clock, long ticketId,
        DateTime now, SlaPolicyDefinition policy, BusinessCalendar calendar)
    {
        if (!policy.Escalates(clock) || policy.LevelAt(1) is not { } first) return (active, EscalationStep.None);

        if (active is not null)
            return active.Reasons.Add(clock) ? (active, EscalationStep.ReasonAdded) : (active, EscalationStep.None);

        var started = new EscalationState
        {
            TicketId = ticketId,
            Level = 1,
            Reasons = [clock],
            StartedAt = now,
            WindowEndsAt = first.WindowMinutes is { } w ? calendar.AddBusinessMinutes(now, w) : null
        };
        return (started, EscalationStep.Started);
    }

    /// <summary>
    /// When the current level's window has run out, move to the next level. At the final level, remind the owner
    /// every <see cref="FinalLevelFollowUpMinutes"/> business minutes.
    /// </summary>
    public static EscalationStep Advance(EscalationState active, DateTime now, SlaPolicyDefinition policy, BusinessCalendar calendar)
    {
        var next = policy.LevelAt(active.Level + 1);
        if (next is not null)
        {
            if (active.WindowEndsAt is not { } ends || now < ends) return EscalationStep.None;
            active.Level = next.Level;
            active.StartedAt = now;
            active.WindowEndsAt = next.WindowMinutes is { } w ? calendar.AddBusinessMinutes(now, w) : null;
            active.LastFollowUpAt = null;
            return EscalationStep.Advanced;
        }

        var since = active.LastFollowUpAt ?? active.StartedAt;
        if (calendar.AddBusinessMinutes(since, FinalLevelFollowUpMinutes) > now) return EscalationStep.None;
        active.LastFollowUpAt = now;
        return EscalationStep.FollowUp;
    }

    /// <summary>Removes met clocks from the escalation's reasons. Returns true when nothing is left and it is cleared.</summary>
    public static bool Clear(EscalationState active, IEnumerable<string> metClocks, DateTime now)
    {
        foreach (var clock in metClocks) active.Reasons.Remove(clock);
        if (active.Reasons.Count > 0) return false;
        active.ClearedAt = now;
        return true;
    }

    /// <summary>Which escalation reasons a ticket change satisfies.</summary>
    public static readonly IReadOnlyList<string> AllClocks = [SlaClock.FirstResponse, SlaClock.NextResponse, SlaClock.Resolution];

    public static IReadOnlyList<string> ClocksMetBy(bool supportPublicReply, TicketStatus? newStatus)
    {
        var met = new List<string>();
        if (supportPublicReply) { met.Add(SlaClock.FirstResponse); met.Add(SlaClock.NextResponse); }
        // A finished ticket needs no further response either, so it satisfies every clock and ends any escalation.
        if (newStatus is TicketStatus.Resolved or TicketStatus.Closed or TicketStatus.Rejected) return AllClocks;
        return met;
    }

    private static DateTime? RemindAt(DateTime start, int minutes, int reminderMinutes, BusinessCalendar calendar) =>
        reminderMinutes > 0 && reminderMinutes < minutes ? calendar.AddBusinessMinutes(start, minutes - reminderMinutes) : null;

    private static void ClearNextResponse(SlaState state)
    {
        state.NextResponseDueAt = null;
        state.NextResponseRemindAt = null;
        state.NextResponseRemindedAt = null;
        state.NextResponseBreachedAt = null;
    }
}
