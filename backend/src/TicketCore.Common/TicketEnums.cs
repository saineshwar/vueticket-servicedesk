namespace TicketCore.Common;

/// <summary>Where a ticket is in its lifecycle. SLA breaches and escalation are separate fields, not statuses.</summary>
public enum TicketStatus : byte
{
    New = 1,
    Open = 2,
    InProgress = 3,
    AwaitingRequester = 4,
    Resolved = 5,
    Closed = 6,
    PendingApproval = 7,
    Rejected = 8
}

public enum DeskAudience : byte
{
    Employees = 1,
    Customers = 2,
    Both = 3
}

public enum TicketChannel : byte
{
    Portal = 1,
    Agent = 2,
    Email = 3
}

public static class TicketPriorities
{
    public const byte Medium = 3;
}

/// <summary>Activity names written to TicketHistory.</summary>
public static class TicketActivities
{
    public const string Created = "created";
    public const string StatusChanged = "status";
    public const string PriorityChanged = "priority";
    public const string Assigned = "assigned";
    public const string Replied = "replied";
    public const string NoteAdded = "note";
    public const string RequesterReplied = "requester_replied";
    public const string Reopened = "reopened";
    public const string Attached = "attached";
    public const string Transferred = "transferred";
    public const string Deleted = "deleted";
    public const string Restored = "restored";

    // Written by the engine; shown to support staff only.
    public const string SlaBreached = "sla_breached";
    public const string Escalated = "escalated";
    public const string EscalationCleared = "escalation_cleared";
}

/// <summary>How the engine hands new tickets to a team's available agents.</summary>
public enum AssignmentMode : byte
{
    Manual = 0,
    LoadBalanced = 1,
    RoundRobin = 2
}
