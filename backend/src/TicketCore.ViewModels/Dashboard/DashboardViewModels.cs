using TicketCore.ViewModels.Tickets;

namespace TicketCore.ViewModels.Dashboard;

/// <summary>The home page. Sections the caller has no rights for are null, so the page shows only what applies.</summary>
public sealed record DashboardResponse(MyRequestsSummary Requests, MyWorkSummary? Work, TeamSummary? Team);

/// <summary>Everyone: the caller's own requests.</summary>
public sealed record MyRequestsSummary(int Open, int AwaitingYou, int ResolvedLast30Days, IReadOnlyList<TicketListItemDto> Recent);

/// <summary>Support staff: tickets assigned to the caller, most urgent first. Queue counts are null without the queue permission.</summary>
public sealed record MyWorkSummary(
    int AssignedOpen,
    int Overdue,
    int DueWithinHour,
    int? Unassigned,
    int? Escalated,
    IReadOnlyList<TicketListItemDto> NextUp);

/// <summary>Leads: open work across the teams where the caller has dashboard.team.</summary>
public sealed record TeamSummary(
    int Open,
    int Unassigned,
    int Overdue,
    int Escalated,
    IReadOnlyList<PriorityCount> OpenByPriority,
    IReadOnlyList<AgentLoad> Agents,
    IReadOnlyList<DailyFlow> Last14Days);

public sealed record PriorityCount(int PriorityId, string PriorityName, int Count);

public sealed record AgentLoad(long UserId, string Name, string Status, int Open, int Overdue);

/// <summary>Date is a UTC day, yyyy-MM-dd.</summary>
public sealed record DailyFlow(string Date, int Created, int Resolved);
