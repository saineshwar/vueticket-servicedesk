using TicketCore.Common;
using TicketCore.Data.Engine;
using TicketCore.Engine;
using TicketCore.Engine.Assignment;

namespace TicketCore.Engine.Tests;

public class AssignmentRulesTests
{
    private static readonly DateTime Earlier = new(2026, 11, 2, 4, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Earlier.AddHours(1);

    [Fact]
    public void Load_balanced_picks_the_fewest_open_tickets()
    {
        var pick = AssignmentRules.Pick([new(1, 5, Earlier), new(2, 2, Later), new(3, 4, null)], AssignmentMode.LoadBalanced, 20);
        Assert.Equal(2, pick!.UserId);
    }

    [Fact]
    public void Ties_go_to_whoever_was_assigned_least_recently_never_assigned_first()
    {
        var pick = AssignmentRules.Pick([new(1, 3, Later), new(2, 3, Earlier), new(3, 3, null)], AssignmentMode.LoadBalanced, 20);
        Assert.Equal(3, pick!.UserId);
    }

    [Fact]
    public void Busy_agents_do_not_get_more_tickets()
    {
        // The old SQL job counted only agents *under* a threshold, so busy agents looked empty and got more work.
        var pick = AssignmentRules.Pick([new(1, 25, null), new(2, 3, Later)], AssignmentMode.LoadBalanced, 20);
        Assert.Equal(2, pick!.UserId);
    }

    [Fact]
    public void Nobody_is_picked_when_everyone_is_at_the_limit()
    {
        Assert.Null(AssignmentRules.Pick([new(1, 20, null), new(2, 21, Earlier)], AssignmentMode.LoadBalanced, 20));
        Assert.Null(AssignmentRules.Pick([], AssignmentMode.LoadBalanced, 20));
    }

    [Fact]
    public void Round_robin_ignores_load_below_the_limit()
    {
        var pick = AssignmentRules.Pick([new(1, 0, Later), new(2, 10, Earlier)], AssignmentMode.RoundRobin, 20);
        Assert.Equal(2, pick!.UserId);
    }
}
