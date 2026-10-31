using TicketCore.Common;
using TicketCore.Core.Tickets;

namespace TicketCore.Core.Tests;

public class TicketRulesTests
{
    [Fact]
    public void Staff_cannot_set_new_or_open_manually()
    {
        var targets = TicketRules.StaffTargets(TicketStatus.InProgress, canReopenClosed: true);
        Assert.DoesNotContain(TicketStatus.New, targets);
        Assert.DoesNotContain(TicketStatus.Open, targets);
        Assert.DoesNotContain(TicketStatus.InProgress, targets); // not the current status
        Assert.Contains(TicketStatus.Resolved, targets);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void Closed_ticket_reopens_only_with_permission(bool canReopen, int expectedCount)
    {
        var targets = TicketRules.StaffTargets(TicketStatus.Closed, canReopen);
        Assert.Equal(expectedCount, targets.Count);
        if (canReopen) Assert.Equal(TicketStatus.InProgress, targets[0]);
    }

    [Theory]
    [InlineData(TicketStatus.AwaitingRequester, TicketStatus.InProgress)]
    [InlineData(TicketStatus.Resolved, TicketStatus.InProgress)]
    [InlineData(TicketStatus.New, null)]
    [InlineData(TicketStatus.InProgress, null)]
    public void Requester_reply_hands_ticket_back_to_support(TicketStatus from, TicketStatus? expected)
    {
        Assert.Equal(expected, TicketRules.AfterRequesterReply(from));
    }

    [Theory]
    [InlineData(TicketStatus.Closed, false)]
    [InlineData(TicketStatus.Rejected, false)]
    [InlineData(TicketStatus.Resolved, true)]
    [InlineData(TicketStatus.New, true)]
    public void Requester_cannot_reply_to_finished_tickets(TicketStatus status, bool allowed)
    {
        Assert.Equal(allowed, TicketRules.RequesterCanReply(status));
    }

    [Theory]
    [InlineData(TicketStatus.New, TicketStatus.InProgress)]
    [InlineData(TicketStatus.Open, TicketStatus.InProgress)]
    [InlineData(TicketStatus.AwaitingRequester, null)]
    public void First_support_reply_moves_ticket_into_progress(TicketStatus from, TicketStatus? expected)
    {
        Assert.Equal(expected, TicketRules.AfterSupportReply(from));
    }

    [Theory]
    [InlineData(DeskAudience.Employees, UserType.Employee, true)]
    [InlineData(DeskAudience.Employees, UserType.Staff, true)]
    [InlineData(DeskAudience.Employees, UserType.Customer, false)]
    [InlineData(DeskAudience.Customers, UserType.Customer, true)]
    [InlineData(DeskAudience.Customers, UserType.Employee, false)]
    [InlineData(DeskAudience.Both, UserType.Customer, true)]
    public void Desk_audience_limits_who_can_raise_requests(DeskAudience audience, UserType type, bool allowed)
    {
        Assert.Equal(allowed, TicketRules.AudienceAllows(audience, type));
    }
}
