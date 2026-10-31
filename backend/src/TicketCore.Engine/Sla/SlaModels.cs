namespace TicketCore.Engine.Sla;

public static class SlaClock
{
    public const string FirstResponse = "first_response";
    public const string NextResponse = "next_response";
    public const string Resolution = "resolution";
}

public sealed record EscalationLevelDefinition(int Level, int RoleId, string RoleName, int? WindowMinutes);

/// <summary>Targets for one priority, in business minutes, with the escalation path.</summary>
public sealed record SlaPolicyDefinition(
    int SlaPolicyId,
    string Name,
    int? ServiceDeskId,
    int? TicketTypeId,
    int PriorityId,
    int FirstResponseMinutes,
    int NextResponseMinutes,
    int ResolutionMinutes,
    int ReminderMinutes,
    bool EscalateOnFirstResponse,
    bool EscalateOnNextResponse,
    bool EscalateOnResolution,
    IReadOnlyList<EscalationLevelDefinition> Levels)
{
    public bool Escalates(string clock) => clock switch
    {
        SlaClock.FirstResponse => EscalateOnFirstResponse,
        SlaClock.NextResponse => EscalateOnNextResponse,
        SlaClock.Resolution => EscalateOnResolution,
        _ => false
    };

    public EscalationLevelDefinition? LevelAt(int level) => Levels.FirstOrDefault(l => l.Level == level);
}

/// <summary>Mirror of a TicketSla row. Times are UTC.</summary>
public sealed class SlaState
{
    public long TicketId { get; set; }
    public int? SlaPolicyId { get; set; }
    public int? CalendarId { get; set; }

    public DateTime? FirstResponseDueAt { get; set; }
    public DateTime? FirstResponseRemindAt { get; set; }
    public DateTime? FirstResponseMetAt { get; set; }
    public DateTime? FirstResponseRemindedAt { get; set; }
    public DateTime? FirstResponseBreachedAt { get; set; }

    public DateTime? NextResponseDueAt { get; set; }
    public DateTime? NextResponseRemindAt { get; set; }
    public DateTime? NextResponseRemindedAt { get; set; }
    public DateTime? NextResponseBreachedAt { get; set; }

    public DateTime? ResolutionDueAt { get; set; }
    public DateTime? ResolutionRemindAt { get; set; }
    public DateTime? ResolutionMetAt { get; set; }
    public DateTime? ResolutionRemindedAt { get; set; }
    public DateTime? ResolutionBreachedAt { get; set; }

    public DateTime? PausedAt { get; set; }
    public int PausedMinutes { get; set; }

    public DateTime? UnassignedAlert1At { get; set; }
    public DateTime? UnassignedAlert2At { get; set; }

    public DateTime? NextEventAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public enum SlaEventKind
{
    Reminder,
    Breach
}

/// <summary>Something the monitor found due: a reminder or a breach on one clock.</summary>
public sealed record SlaEvent(string Clock, SlaEventKind Kind, DateTime DueAt);

/// <summary>Mirror of the active TicketEscalation row.</summary>
public sealed class EscalationState
{
    public long TicketEscalationId { get; set; }
    public long TicketId { get; set; }
    public int Level { get; set; }
    public HashSet<string> Reasons { get; set; } = [];
    public long? OwnerUserId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? WindowEndsAt { get; set; }
    public DateTime? LastFollowUpAt { get; set; }
    public DateTime? ClearedAt { get; set; }
}
