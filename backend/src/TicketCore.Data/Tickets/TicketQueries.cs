using Dapper;
using TicketCore.Common;
using TicketCore.ViewModels.Tickets;
using TicketCore.ViewModels.Users;

namespace TicketCore.Data.Tickets.Queries;

/// <summary>A ticket with the names the API needs. Access checks happen in Core before this is shown.</summary>
public sealed class TicketRecord
{
    public long TicketId { get; init; }
    public string TrackingId { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public byte StatusValue { get; init; }
    public int PriorityId { get; init; }
    public string PriorityName { get; init; } = string.Empty;
    public int ServiceDeskId { get; init; }
    public string ServiceDeskName { get; init; } = string.Empty;
    public int CategoryId { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public string TicketTypeName { get; init; } = string.Empty;
    public int TeamId { get; init; }
    public string TeamName { get; init; } = string.Empty;
    public long RequesterUserId { get; init; }
    public string RequesterName { get; init; } = string.Empty;
    public string RequesterEmail { get; init; } = string.Empty;
    public long? AssigneeUserId { get; init; }
    public string? AssigneeName { get; init; }
    public string? AssigneeEmail { get; init; }
    public bool IsDeleted { get; init; }
    public DateTime? DeletedAt { get; init; }
    public string? DeleteReason { get; init; }
    public byte ChannelValue { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime? FirstRespondedAt { get; init; }
    public DateTime? ResolvedAt { get; init; }
    /// <summary>The earliest SLA target not yet met; null when nothing is pending or the clock is paused.</summary>
    public DateTime? SlaDueAt { get; init; }
    public bool SlaPaused { get; init; }
    public byte? EscalationLevel { get; init; }

    public TicketStatus Status => (TicketStatus)StatusValue;
}

/// <summary>The engine's SLA state for one ticket, as written by the worker.</summary>
public sealed class TicketSlaRecord
{
    public string? PolicyName { get; init; }
    public DateTime? FirstResponseDueAt { get; init; }
    public DateTime? FirstResponseMetAt { get; init; }
    public DateTime? FirstResponseBreachedAt { get; init; }
    public DateTime? NextResponseDueAt { get; init; }
    public DateTime? NextResponseBreachedAt { get; init; }
    public DateTime? ResolutionDueAt { get; init; }
    public DateTime? ResolutionMetAt { get; init; }
    public DateTime? ResolutionBreachedAt { get; init; }
    public DateTime? PausedAt { get; init; }
}

public sealed class TicketEscalationRecord
{
    public byte Level { get; init; }
    public string? RoleName { get; init; }
    public string Reasons { get; init; } = string.Empty;
    public long? OwnerUserId { get; init; }
    public string? OwnerName { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? WindowEndsAt { get; init; }
}

/// <summary>What the API needs to stream a file; access is checked against the ticket first.</summary>
public sealed class AttachmentRecord
{
    public long AttachmentId { get; init; }
    public long TicketId { get; init; }
    public bool IsInternal { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string StorageKey { get; init; } = string.Empty;
}

public interface ITicketQueries
{
    Task<TicketRecord?> GetAsync(long ticketId, bool includeDeleted = false, CancellationToken ct = default);
    Task<IReadOnlyList<AttachmentDto>> GetAttachmentsAsync(long ticketId, bool includeInternal, CancellationToken ct = default);
    Task<AttachmentRecord?> GetAttachmentAsync(long attachmentId, CancellationToken ct = default);
    Task<PagedResult<TicketListItemDto>> ListAsync(long userId, TicketListQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<TicketCommentDto>> GetCommentsAsync(long ticketId, bool includeInternal, CancellationToken ct = default);
    Task<IReadOnlyList<TicketHistoryDto>> GetHistoryAsync(long ticketId, CancellationToken ct = default);
    Task<TicketSlaRecord?> GetSlaAsync(long ticketId, CancellationToken ct = default);
    Task<TicketEscalationRecord?> GetActiveEscalationAsync(long ticketId, CancellationToken ct = default);

    /// <summary>Active users who hold ticket.view on the team (directly or through an ancestor unit).</summary>
    Task<IReadOnlyList<AssignableUserDto>> GetAssignableUsersAsync(int teamId, CancellationToken ct = default);
}

public sealed class TicketQueries(ISqlConnectionFactory connections) : ITicketQueries
{
    private const string NameOf = "COALESCE(NULLIF(LTRIM(RTRIM(CONCAT({0}.FirstName, ' ', {0}.LastName))), ''), {0}.UserName)";

    private static readonly string SelectTicket = $"""
        SELECT t.TicketId, t.TrackingId, t.Subject, t.Description, t.Status AS StatusValue,
               CAST(t.PriorityId AS INT) AS PriorityId, p.Name AS PriorityName,
               t.ServiceDeskId, d.Name AS ServiceDeskName, t.CategoryId, c.Name AS CategoryName, tt.Name AS TicketTypeName,
               t.TeamId, o.Name AS TeamName,
               t.RequesterUserId, {string.Format(NameOf, "r")} AS RequesterName, r.Email AS RequesterEmail,
               t.AssigneeUserId, CASE WHEN a.UserId IS NULL THEN NULL ELSE {string.Format(NameOf, "a")} END AS AssigneeName,
               a.Email AS AssigneeEmail, t.IsDeleted, t.DeletedAt, t.DeleteReason,
               t.Channel AS ChannelValue, t.CreatedAt, t.UpdatedAt, t.FirstRespondedAt, t.ResolvedAt,
               CASE WHEN s.PausedAt IS NULL THEN sd.DueAt END AS SlaDueAt,
               CAST(CASE WHEN s.PausedAt IS NULL THEN 0 ELSE 1 END AS BIT) AS SlaPaused,
               e.Level AS EscalationLevel
        FROM dbo.Ticket t
        LEFT JOIN dbo.TicketSla s ON s.TicketId = t.TicketId
        LEFT JOIN dbo.TicketEscalation e ON e.TicketId = t.TicketId AND e.ClearedAt IS NULL
        OUTER APPLY (SELECT MIN(v) AS DueAt
                     FROM (VALUES (CASE WHEN s.FirstResponseMetAt IS NULL THEN s.FirstResponseDueAt END),
                                  (s.NextResponseDueAt),
                                  (CASE WHEN s.ResolutionMetAt IS NULL THEN s.ResolutionDueAt END)) due(v)) sd
        JOIN dbo.Priority p ON p.PriorityId = t.PriorityId
        JOIN dbo.ServiceDesk d ON d.ServiceDeskId = t.ServiceDeskId
        JOIN dbo.Category c ON c.CategoryId = t.CategoryId
        JOIN dbo.TicketType tt ON tt.TicketTypeId = t.TicketTypeId
        JOIN dbo.OrgUnit o ON o.OrgUnitId = t.TeamId
        JOIN dbo.[User] r ON r.UserId = t.RequesterUserId
        LEFT JOIN dbo.[User] a ON a.UserId = t.AssigneeUserId
        """;

    // Units where the user holds a permission, expanded through the closure table.
    private const string ScopeCte = """
        WITH scope AS (
            SELECT DISTINCT c.DescendantId AS OrgUnitId
            FROM dbo.RoleAssignment ra
            JOIN dbo.RolePermission rp ON rp.RoleId = ra.RoleId AND rp.PermissionKey = @scopePermission
            JOIN dbo.OrgUnitClosure c ON c.AncestorId = ra.OrgUnitId AND (ra.IncludeDescendants = 1 OR c.Depth = 0)
            JOIN dbo.[User] u ON u.UserId = ra.UserId AND u.IsActive = 1
            WHERE ra.UserId = @userId
        )
        """;

    private static readonly int[] OpenStatuses =
    [
        (int)TicketStatus.New, (int)TicketStatus.Open, (int)TicketStatus.InProgress,
        (int)TicketStatus.AwaitingRequester, (int)TicketStatus.PendingApproval
    ];

    private static readonly int[] DoneStatuses = [(int)TicketStatus.Resolved, (int)TicketStatus.Closed, (int)TicketStatus.Rejected];

    public async Task<TicketRecord?> GetAsync(long ticketId, bool includeDeleted = false, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<TicketRecord>(new CommandDefinition(
            SelectTicket + " WHERE t.TicketId = @ticketId AND (@includeDeleted = 1 OR t.IsDeleted = 0)",
            new { ticketId, includeDeleted }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AttachmentDto>> GetAttachmentsAsync(long ticketId, bool includeInternal, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        var rows = await db.QueryAsync<AttachmentDto>(new CommandDefinition($"""
            SELECT at.AttachmentId, at.TicketCommentId, at.FileName, at.ContentType, at.SizeBytes, at.IsInternal,
                   {string.Format(NameOf, "u")} AS UploadedByName, at.UploadedAt
            FROM dbo.Attachment at
            JOIN dbo.[User] u ON u.UserId = at.UploadedByUserId
            WHERE at.TicketId = @ticketId AND (@includeInternal = 1 OR at.IsInternal = 0)
            ORDER BY at.UploadedAt, at.AttachmentId
            """, new { ticketId, includeInternal }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<AttachmentRecord?> GetAttachmentAsync(long attachmentId, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<AttachmentRecord>(new CommandDefinition("""
            SELECT AttachmentId, TicketId, IsInternal, FileName, ContentType, SizeBytes, StorageKey
            FROM dbo.Attachment WHERE AttachmentId = @attachmentId
            """, new { attachmentId }, cancellationToken: ct));
    }

    public async Task<PagedResult<TicketListItemDto>> ListAsync(long userId, TicketListQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var (viewFilter, scopePermission) = query.View switch
        {
            TicketView.Requested => ("t.RequesterUserId = @userId", (string?)null),
            TicketView.Mine => ("t.AssigneeUserId = @userId", null),
            TicketView.Team => ("t.TeamId IN (SELECT OrgUnitId FROM scope)", Permissions.TicketView),
            TicketView.Unassigned => ("t.AssigneeUserId IS NULL AND t.TeamId IN (SELECT OrgUnitId FROM scope)", Permissions.QueueUnassigned),
            TicketView.Deleted => ("t.TeamId IN (SELECT OrgUnitId FROM scope)", Permissions.TicketRestore),
            TicketView.Escalated => ("e.TicketEscalationId IS NOT NULL AND (e.OwnerUserId = @userId OR t.TeamId IN (SELECT OrgUnitId FROM scope))",
                Permissions.QueueEscalated),
            _ => throw new ArgumentOutOfRangeException(nameof(query))
        };
        var deletedFilter = query.View == TicketView.Deleted ? "t.IsDeleted = 1" : "t.IsDeleted = 0";

        int[] statuses = (query.View == TicketView.Deleted ? "all" : query.Status?.ToLowerInvariant()) switch
        {
            null or "" or "open" => OpenStatuses,
            "done" => DoneStatuses,
            "all" => [.. OpenStatuses, .. DoneStatuses],
            var s when Enum.TryParse<TicketStatus>(s, true, out var one) => [(int)one],
            _ => OpenStatuses
        };

        var q = string.IsNullOrWhiteSpace(query.Q) ? null : query.Q.Trim();
        var where = $"""
            WHERE {deletedFilter} AND {viewFilter} AND t.Status IN @statuses
              AND (@q IS NULL OR t.TrackingId = @q OR t.Subject LIKE '%' + @q + '%')
            """;
        var cte = scopePermission is null ? string.Empty : ScopeCte;
        var args = new { userId, scopePermission, statuses, q, skip = (page - 1) * pageSize, take = pageSize };

        await using var db = await connections.OpenAsync(ct);
        var total = await db.ExecuteScalarAsync<int>(new CommandDefinition($"""
            {cte} SELECT COUNT(1) FROM dbo.Ticket t
            LEFT JOIN dbo.TicketEscalation e ON e.TicketId = t.TicketId AND e.ClearedAt IS NULL
            {where}
            """, args, cancellationToken: ct));

        // Escalated work comes first by level; other support queues put the most urgent SLA target first.
        var order = query.View switch
        {
            TicketView.Requested => "t.PriorityId, t.UpdatedAt DESC",
            TicketView.Escalated => "e.Level DESC, e.StartedAt",
            _ => "CASE WHEN e.Level IS NULL THEN 1 ELSE 0 END, CASE WHEN s.PausedAt IS NULL AND sd.DueAt IS NOT NULL THEN 0 ELSE 1 END, " +
                 "sd.DueAt, t.PriorityId, t.UpdatedAt DESC"
        };
        var rows = await db.QueryAsync<TicketRecord>(new CommandDefinition($"""
            {cte}
            {SelectTicket}
            {where}
            ORDER BY CASE WHEN t.Status IN (5, 6, 8) THEN 1 ELSE 0 END, {order}
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
            """, args, cancellationToken: ct));

        // Requesters see their own requests without internal SLA or escalation details.
        var showSla = query.View != TicketView.Requested;
        var items = rows.Select(r => new TicketListItemDto(
            r.TicketId, r.TrackingId, r.Subject, r.Status.ToString(), r.PriorityId, r.PriorityName,
            r.ServiceDeskName, r.CategoryName, r.TeamName, r.RequesterName, r.AssigneeName, r.CreatedAt, r.UpdatedAt,
            showSla ? r.SlaDueAt : null, showSla && r.SlaPaused, showSla ? r.EscalationLevel : null)).ToList();
        return new PagedResult<TicketListItemDto>(items, total);
    }

    public async Task<IReadOnlyList<TicketCommentDto>> GetCommentsAsync(long ticketId, bool includeInternal, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        var rows = await db.QueryAsync<TicketCommentDto>(new CommandDefinition($"""
            SELECT tc.TicketCommentId, tc.AuthorUserId, {string.Format(NameOf, "u")} AS AuthorName,
                   tc.AuthorIsSupport, tc.IsInternal, tc.Body, tc.CreatedAt
            FROM dbo.TicketComment tc
            JOIN dbo.[User] u ON u.UserId = tc.AuthorUserId
            WHERE tc.TicketId = @ticketId AND (@includeInternal = 1 OR tc.IsInternal = 0)
            ORDER BY tc.CreatedAt, tc.TicketCommentId
            """, new { ticketId, includeInternal }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<TicketHistoryDto>> GetHistoryAsync(long ticketId, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        var rows = await db.QueryAsync<TicketHistoryDto>(new CommandDefinition($"""
            SELECT h.Activity, h.FromValue, h.ToValue,
                   CASE WHEN u.UserId IS NULL THEN NULL ELSE {string.Format(NameOf, "u")} END AS UserName, h.OccurredAt
            FROM dbo.TicketHistory h
            LEFT JOIN dbo.[User] u ON u.UserId = h.UserId
            WHERE h.TicketId = @ticketId
            ORDER BY h.OccurredAt, h.TicketHistoryId
            """, new { ticketId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<TicketSlaRecord?> GetSlaAsync(long ticketId, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<TicketSlaRecord>(new CommandDefinition("""
            SELECT p.Name AS PolicyName, s.FirstResponseDueAt, s.FirstResponseMetAt, s.FirstResponseBreachedAt,
                   s.NextResponseDueAt, s.NextResponseBreachedAt, s.ResolutionDueAt, s.ResolutionMetAt, s.ResolutionBreachedAt, s.PausedAt
            FROM dbo.TicketSla s
            LEFT JOIN dbo.SlaPolicy p ON p.SlaPolicyId = s.SlaPolicyId
            WHERE s.TicketId = @ticketId
            """, new { ticketId }, cancellationToken: ct));
    }

    public async Task<TicketEscalationRecord?> GetActiveEscalationAsync(long ticketId, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<TicketEscalationRecord>(new CommandDefinition($"""
            SELECT e.Level, r.Name AS RoleName, e.Reasons, e.OwnerUserId,
                   CASE WHEN o.UserId IS NULL THEN NULL ELSE {string.Format(NameOf, "o")} END AS OwnerName,
                   e.StartedAt, e.WindowEndsAt
            FROM dbo.TicketEscalation e
            LEFT JOIN dbo.TicketSla s ON s.TicketId = e.TicketId
            LEFT JOIN dbo.EscalationLevel l ON l.SlaPolicyId = s.SlaPolicyId AND l.Level = e.Level
            LEFT JOIN dbo.Role r ON r.RoleId = l.RoleId
            LEFT JOIN dbo.[User] o ON o.UserId = e.OwnerUserId
            WHERE e.TicketId = @ticketId AND e.ClearedAt IS NULL
            """, new { ticketId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AssignableUserDto>> GetAssignableUsersAsync(int teamId, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        var rows = await db.QueryAsync<AssignableUserDto>(new CommandDefinition($"""
            SELECT DISTINCT u.UserId, {string.Format(NameOf, "u")} AS DisplayName
            FROM dbo.RoleAssignment ra
            JOIN dbo.RolePermission rp ON rp.RoleId = ra.RoleId AND rp.PermissionKey = @permission
            JOIN dbo.OrgUnitClosure c ON c.AncestorId = ra.OrgUnitId AND c.DescendantId = @teamId
                                      AND (ra.IncludeDescendants = 1 OR c.Depth = 0)
            JOIN dbo.[User] u ON u.UserId = ra.UserId AND u.IsActive = 1
            ORDER BY DisplayName
            """, new { teamId, permission = Permissions.TicketView }, cancellationToken: ct));
        return rows.ToList();
    }
}
