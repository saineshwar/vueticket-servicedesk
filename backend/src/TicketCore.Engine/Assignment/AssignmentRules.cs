using TicketCore.Common;
using TicketCore.Data.Engine;

namespace TicketCore.Engine.Assignment;

/// <summary>Who gets the next ticket. Pure: candidates in, choice out.</summary>
public static class AssignmentRules
{
    /// <summary>
    /// Load-balanced: the available agent with the fewest open tickets, below the team's maximum; ties go to whoever
    /// was assigned least recently (never-assigned first). Round-robin: least recently assigned, below the maximum.
    /// Returns null when everyone is at the limit.
    /// </summary>
    public static AgentCandidate? Pick(IEnumerable<AgentCandidate> candidates, AssignmentMode mode, int maxOpenTickets)
    {
        var eligible = candidates.Where(c => c.OpenTickets < maxOpenTickets);
        var ordered = mode == AssignmentMode.RoundRobin
            ? eligible.OrderBy(c => c.LastAssignedAt ?? DateTime.MinValue).ThenBy(c => c.UserId)
            : eligible.OrderBy(c => c.OpenTickets).ThenBy(c => c.LastAssignedAt ?? DateTime.MinValue).ThenBy(c => c.UserId);
        return ordered.FirstOrDefault();
    }
}
