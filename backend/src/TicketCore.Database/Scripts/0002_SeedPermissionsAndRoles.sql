-- 0002: Permission catalog and built-in role templates.
-- The permission list must match TicketCore.Common.Permissions.Catalog (checked by a unit test).

INSERT INTO dbo.Permission (PermissionKey, GroupName, Description) VALUES
('ticket.view',            N'Tickets',         N'View tickets'),
('ticket.create.onbehalf', N'Tickets',         N'Create tickets on behalf of a requester'),
('ticket.reply',           N'Tickets',         N'Reply publicly to requesters'),
('ticket.note',            N'Tickets',         N'Add internal notes'),
('ticket.status',          N'Tickets',         N'Change ticket status'),
('ticket.priority',        N'Tickets',         N'Change ticket priority'),
('ticket.assign',          N'Tickets',         N'Assign and reassign tickets'),
('ticket.transfer',        N'Tickets',         N'Transfer tickets to another team'),
('ticket.merge',           N'Tickets',         N'Merge and link tickets'),
('ticket.delete',          N'Tickets',         N'Delete tickets'),
('ticket.restore',         N'Tickets',         N'Restore deleted tickets'),
('ticket.reopen.closed',   N'Tickets',         N'Reopen closed tickets'),
('queue.unassigned',       N'Queues and work', N'See and work the unassigned queue'),
('queue.escalated',        N'Queues and work', N'See the escalated queue'),
('agent.checkin',          N'Queues and work', N'Set availability (check in / out)'),
('agent.receive.auto',     N'Queues and work', N'Receive automatically assigned tickets'),
('approval.decide',        N'Approvals',       N'Approve or reject requests'),
('kb.read.internal',       N'Knowledge base',  N'Read internal articles'),
('kb.edit',                N'Knowledge base',  N'Create and edit articles'),
('kb.publish',             N'Knowledge base',  N'Publish articles'),
('report.view',            N'Reports',         N'View reports'),
('report.export',          N'Reports',         N'Export reports'),
('report.schedule',        N'Reports',         N'Schedule emailed reports'),
('dashboard.team',         N'Reports',         N'See team dashboards'),
('requester.manage',       N'People',          N'Create and edit requesters'),
('user.manage',            N'People',          N'Manage users'),
('role.manage',            N'People',          N'Manage roles and role assignments'),
('org.manage',             N'People',          N'Manage the organization tree'),
('config.sla',             N'Configuration',   N'Configure SLA policies and escalation paths'),
('config.calendars',       N'Configuration',   N'Configure business calendars and holidays'),
('config.routing',         N'Configuration',   N'Configure routing and assignment'),
('config.desks',           N'Configuration',   N'Configure service desks, ticket types and categories'),
('config.forms',           N'Configuration',   N'Configure request forms and approvals'),
('config.email',           N'Configuration',   N'Configure mailboxes and email templates'),
('config.jobs',            N'Configuration',   N'Manage background jobs'),
('audit.view',             N'Configuration',   N'View the audit log');

INSERT INTO dbo.Role (Name, Description, IsSystem) VALUES
(N'System Admin',       N'Full access to the whole organization. Locked.', 1),
(N'Agent',              N'Works tickets in their team.', 0),
(N'Team Lead',          N'Agent permissions plus queues, transfers and team reports. (Agent Admin in the old system.)', 0),
(N'Head of Department', N'Team Lead permissions across a department. (HOD in the old system.)', 0),
(N'Business Unit Head', N'Views and reports for a business unit; final escalation level.', 0),
(N'Desk Admin',         N'Configures SLA, calendars, routing, forms and email for their own area.', 0);

-- System Admin: everything.
INSERT INTO dbo.RolePermission (RoleId, PermissionKey)
SELECT r.RoleId, p.PermissionKey FROM dbo.Role r CROSS JOIN dbo.Permission p WHERE r.Name = N'System Admin';

DECLARE @agent TABLE (PermissionKey VARCHAR(64));
INSERT INTO @agent VALUES ('ticket.view'),('ticket.create.onbehalf'),('ticket.reply'),('ticket.note'),('ticket.status'),
    ('ticket.priority'),('ticket.assign'),('agent.checkin'),('agent.receive.auto'),('kb.read.internal'),('requester.manage');

DECLARE @lead TABLE (PermissionKey VARCHAR(64));
INSERT INTO @lead SELECT PermissionKey FROM @agent;
INSERT INTO @lead VALUES ('queue.unassigned'),('queue.escalated'),('ticket.transfer'),('ticket.merge'),('ticket.delete'),
    ('ticket.restore'),('kb.edit'),('kb.publish'),('report.view'),('dashboard.team');

DECLARE @head TABLE (PermissionKey VARCHAR(64));
INSERT INTO @head SELECT PermissionKey FROM @lead;
INSERT INTO @head VALUES ('ticket.reopen.closed'),('report.export'),('report.schedule');

INSERT INTO dbo.RolePermission (RoleId, PermissionKey) SELECT r.RoleId, a.PermissionKey FROM dbo.Role r CROSS JOIN @agent a WHERE r.Name = N'Agent';
INSERT INTO dbo.RolePermission (RoleId, PermissionKey) SELECT r.RoleId, l.PermissionKey FROM dbo.Role r CROSS JOIN @lead l WHERE r.Name = N'Team Lead';
INSERT INTO dbo.RolePermission (RoleId, PermissionKey) SELECT r.RoleId, h.PermissionKey FROM dbo.Role r CROSS JOIN @head h WHERE r.Name = N'Head of Department';

INSERT INTO dbo.RolePermission (RoleId, PermissionKey)
SELECT r.RoleId, v.PermissionKey FROM dbo.Role r
CROSS JOIN (VALUES ('ticket.view'),('queue.escalated'),('report.view'),('report.export'),('dashboard.team'),('kb.read.internal')) v(PermissionKey)
WHERE r.Name = N'Business Unit Head';

INSERT INTO dbo.RolePermission (RoleId, PermissionKey)
SELECT r.RoleId, v.PermissionKey FROM dbo.Role r
CROSS JOIN (VALUES ('config.sla'),('config.calendars'),('config.routing'),('config.desks'),('config.forms'),('config.email'),('ticket.view'),('report.view')) v(PermissionKey)
WHERE r.Name = N'Desk Admin';
