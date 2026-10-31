using TicketCore.Common;

namespace TicketCore.Core.Tickets;

/// <summary>Status rules, kept free of I/O so they can be tested directly.</summary>
public static class TicketRules
{
    private static readonly TicketStatus[] ManualTargets =
        [TicketStatus.InProgress, TicketStatus.AwaitingRequester, TicketStatus.Resolved, TicketStatus.Closed];

    /// <summary>
    /// Statuses support staff can choose from the given status. New and Open follow assignment, approval
    /// statuses follow the approval flow, and a closed ticket can only be reopened with ticket.reopen.closed.
    /// </summary>
    public static IReadOnlyList<TicketStatus> StaffTargets(TicketStatus from, bool canReopenClosed) => from switch
    {
        TicketStatus.Closed => canReopenClosed ? [TicketStatus.InProgress] : [],
        TicketStatus.PendingApproval or TicketStatus.Rejected => [],
        _ => ManualTargets.Where(s => s != from).ToList()
    };

    public static bool CanStaffSet(TicketStatus from, TicketStatus to, bool canReopenClosed) =>
        StaffTargets(from, canReopenClosed).Contains(to);

    /// <summary>A requester can reply unless the ticket is finished for good.</summary>
    public static bool RequesterCanReply(TicketStatus status) =>
        status is not (TicketStatus.Closed or TicketStatus.Rejected);

    /// <summary>A requester reply hands the ticket back to support (and reopens a resolved one).</summary>
    public static TicketStatus? AfterRequesterReply(TicketStatus from) => from switch
    {
        TicketStatus.AwaitingRequester or TicketStatus.Resolved => TicketStatus.InProgress,
        _ => null
    };

    /// <summary>The first public reply from support moves a new or open ticket into progress.</summary>
    public static TicketStatus? AfterSupportReply(TicketStatus from) => from switch
    {
        TicketStatus.New or TicketStatus.Open => TicketStatus.InProgress,
        _ => null
    };

    public static bool AudienceAllows(DeskAudience audience, UserType requesterType) => audience switch
    {
        DeskAudience.Both => true,
        DeskAudience.Customers => requesterType == UserType.Customer,
        DeskAudience.Employees => requesterType != UserType.Customer,
        _ => false
    };
}
