namespace TicketCore.Common;

/// <summary>
/// The fixed permission catalog. Roles are built from these keys by administrators.
/// Keep in sync with database/Scripts/0002_SeedPermissionsAndRoles.sql (a test checks this).
/// </summary>
public static class Permissions
{
    // Tickets
    public const string TicketView = "ticket.view";
    public const string TicketCreateOnBehalf = "ticket.create.onbehalf";
    public const string TicketReply = "ticket.reply";
    public const string TicketNote = "ticket.note";
    public const string TicketStatus = "ticket.status";
    public const string TicketPriority = "ticket.priority";
    public const string TicketAssign = "ticket.assign";
    public const string TicketTransfer = "ticket.transfer";
    public const string TicketMerge = "ticket.merge";
    public const string TicketDelete = "ticket.delete";
    public const string TicketRestore = "ticket.restore";
    public const string TicketReopenClosed = "ticket.reopen.closed";

    // Queues and work
    public const string QueueUnassigned = "queue.unassigned";
    public const string QueueEscalated = "queue.escalated";
    public const string AgentCheckIn = "agent.checkin";
    public const string AgentReceiveAuto = "agent.receive.auto";

    // Approvals
    public const string ApprovalDecide = "approval.decide";

    // Knowledge base
    public const string KbReadInternal = "kb.read.internal";
    public const string KbEdit = "kb.edit";
    public const string KbPublish = "kb.publish";

    // Reports
    public const string ReportView = "report.view";
    public const string ReportExport = "report.export";
    public const string ReportSchedule = "report.schedule";
    public const string DashboardTeam = "dashboard.team";

    // People
    public const string RequesterManage = "requester.manage";
    public const string UserManage = "user.manage";
    public const string RoleManage = "role.manage";
    public const string OrgManage = "org.manage";

    // Configuration
    public const string ConfigSla = "config.sla";
    public const string ConfigCalendars = "config.calendars";
    public const string ConfigRouting = "config.routing";
    public const string ConfigDesks = "config.desks";
    public const string ConfigForms = "config.forms";
    public const string ConfigEmail = "config.email";
    public const string ConfigJobs = "config.jobs";
    public const string AuditView = "audit.view";

    public static readonly IReadOnlyList<PermissionDefinition> Catalog =
    [
        new(TicketView, "Tickets", "View tickets"),
        new(TicketCreateOnBehalf, "Tickets", "Create tickets on behalf of a requester"),
        new(TicketReply, "Tickets", "Reply publicly to requesters"),
        new(TicketNote, "Tickets", "Add internal notes"),
        new(TicketStatus, "Tickets", "Change ticket status"),
        new(TicketPriority, "Tickets", "Change ticket priority"),
        new(TicketAssign, "Tickets", "Assign and reassign tickets"),
        new(TicketTransfer, "Tickets", "Transfer tickets to another team"),
        new(TicketMerge, "Tickets", "Merge and link tickets"),
        new(TicketDelete, "Tickets", "Delete tickets"),
        new(TicketRestore, "Tickets", "Restore deleted tickets"),
        new(TicketReopenClosed, "Tickets", "Reopen closed tickets"),

        new(QueueUnassigned, "Queues and work", "See and work the unassigned queue"),
        new(QueueEscalated, "Queues and work", "See the escalated queue"),
        new(AgentCheckIn, "Queues and work", "Set availability (check in / out)"),
        new(AgentReceiveAuto, "Queues and work", "Receive automatically assigned tickets"),

        new(ApprovalDecide, "Approvals", "Approve or reject requests"),

        new(KbReadInternal, "Knowledge base", "Read internal articles"),
        new(KbEdit, "Knowledge base", "Create and edit articles"),
        new(KbPublish, "Knowledge base", "Publish articles"),

        new(ReportView, "Reports", "View reports"),
        new(ReportExport, "Reports", "Export reports"),
        new(ReportSchedule, "Reports", "Schedule emailed reports"),
        new(DashboardTeam, "Reports", "See team dashboards"),

        new(RequesterManage, "People", "Create and edit requesters"),
        new(UserManage, "People", "Manage users"),
        new(RoleManage, "People", "Manage roles and role assignments"),
        new(OrgManage, "People", "Manage the organization tree"),

        new(ConfigSla, "Configuration", "Configure SLA policies and escalation paths"),
        new(ConfigCalendars, "Configuration", "Configure business calendars and holidays"),
        new(ConfigRouting, "Configuration", "Configure routing and assignment"),
        new(ConfigDesks, "Configuration", "Configure service desks, ticket types and categories"),
        new(ConfigForms, "Configuration", "Configure request forms and approvals"),
        new(ConfigEmail, "Configuration", "Configure mailboxes and email templates"),
        new(ConfigJobs, "Configuration", "Manage background jobs"),
        new(AuditView, "Configuration", "View the audit log"),
    ];

    private static readonly HashSet<string> Keys = Catalog.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);

    public static bool Exists(string key) => Keys.Contains(key);
}

public sealed record PermissionDefinition(string Key, string Group, string Description);
