using TicketCore.Data.Engine;

namespace TicketCore.Engine.Sla;

/// <summary>Copies between the database row and the engine's state (kept separate so the rules never see the database).</summary>
internal static class SlaStateMapping
{
    public static SlaState ToState(this TicketSlaRow r) => new()
    {
        TicketId = r.TicketId, SlaPolicyId = r.SlaPolicyId, CalendarId = r.CalendarId,
        FirstResponseDueAt = Utc(r.FirstResponseDueAt), FirstResponseRemindAt = Utc(r.FirstResponseRemindAt),
        FirstResponseMetAt = Utc(r.FirstResponseMetAt), FirstResponseRemindedAt = Utc(r.FirstResponseRemindedAt),
        FirstResponseBreachedAt = Utc(r.FirstResponseBreachedAt),
        NextResponseDueAt = Utc(r.NextResponseDueAt), NextResponseRemindAt = Utc(r.NextResponseRemindAt),
        NextResponseRemindedAt = Utc(r.NextResponseRemindedAt), NextResponseBreachedAt = Utc(r.NextResponseBreachedAt),
        ResolutionDueAt = Utc(r.ResolutionDueAt), ResolutionRemindAt = Utc(r.ResolutionRemindAt), ResolutionMetAt = Utc(r.ResolutionMetAt),
        ResolutionRemindedAt = Utc(r.ResolutionRemindedAt), ResolutionBreachedAt = Utc(r.ResolutionBreachedAt),
        PausedAt = Utc(r.PausedAt), PausedMinutes = r.PausedMinutes,
        UnassignedAlert1At = Utc(r.UnassignedAlert1At), UnassignedAlert2At = Utc(r.UnassignedAlert2At),
        NextEventAt = Utc(r.NextEventAt), RowVersion = r.RowVersion
    };

    public static TicketSlaRow ToRow(this SlaState s) => new()
    {
        TicketId = s.TicketId, SlaPolicyId = s.SlaPolicyId, CalendarId = s.CalendarId,
        FirstResponseDueAt = s.FirstResponseDueAt, FirstResponseRemindAt = s.FirstResponseRemindAt, FirstResponseMetAt = s.FirstResponseMetAt,
        FirstResponseRemindedAt = s.FirstResponseRemindedAt, FirstResponseBreachedAt = s.FirstResponseBreachedAt,
        NextResponseDueAt = s.NextResponseDueAt, NextResponseRemindAt = s.NextResponseRemindAt,
        NextResponseRemindedAt = s.NextResponseRemindedAt, NextResponseBreachedAt = s.NextResponseBreachedAt,
        ResolutionDueAt = s.ResolutionDueAt, ResolutionRemindAt = s.ResolutionRemindAt, ResolutionMetAt = s.ResolutionMetAt,
        ResolutionRemindedAt = s.ResolutionRemindedAt, ResolutionBreachedAt = s.ResolutionBreachedAt,
        PausedAt = s.PausedAt, PausedMinutes = s.PausedMinutes,
        UnassignedAlert1At = s.UnassignedAlert1At, UnassignedAlert2At = s.UnassignedAlert2At,
        NextEventAt = s.NextEventAt, RowVersion = s.RowVersion
    };

    public static EscalationState ToState(this EscalationRow r) => new()
    {
        TicketEscalationId = r.TicketEscalationId, TicketId = r.TicketId, Level = r.Level,
        Reasons = r.Reasons.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(),
        OwnerUserId = r.OwnerUserId, StartedAt = Utc(r.StartedAt)!.Value, WindowEndsAt = Utc(r.WindowEndsAt),
        LastFollowUpAt = Utc(r.LastFollowUpAt), ClearedAt = Utc(r.ClearedAt)
    };

    public static EscalationRow ToRow(this EscalationState s) => new()
    {
        TicketEscalationId = s.TicketEscalationId, TicketId = s.TicketId, Level = (byte)s.Level,
        Reasons = string.Join(',', s.Reasons.Order()), OwnerUserId = s.OwnerUserId, StartedAt = s.StartedAt,
        WindowEndsAt = s.WindowEndsAt, LastFollowUpAt = s.LastFollowUpAt, ClearedAt = s.ClearedAt
    };

    private static DateTime? Utc(DateTime? value) => value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;
}
