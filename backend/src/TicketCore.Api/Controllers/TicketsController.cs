using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TicketCore.Api.Infrastructure;
using TicketCore.Core.Tickets;
using TicketCore.ViewModels.Tickets;
using TicketCore.ViewModels.Users;

namespace TicketCore.Api.Controllers;

/// <summary>
/// Tickets for requesters and support staff alike. Access is decided per ticket in TicketService:
/// requester, assignee, or ticket.view on the ticket's team. Others get 404.
/// Creating and replying take multipart/form-data: a "payload" JSON part plus optional "files".
/// </summary>
[ApiController]
[Route("api/v1/tickets")]
public sealed class TicketsController(ITicketService service) : ControllerBase
{
    // 5 files x 10 MB plus the form itself.
    private const long MaxUpload = 60_000_000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [HttpGet]
    public async Task<ActionResult<PagedResult<TicketListItemDto>>> List(
        [FromQuery] TicketView view = TicketView.Requested,
        [FromQuery] string? status = null,
        [FromQuery] string? q = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default) =>
        Ok(await service.ListAsync(new TicketListQuery(view, status, q, page, pageSize), ct));

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUpload)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUpload)]
    public async Task<ActionResult<TicketDetailDto>> Create([FromForm] string payload, [FromForm] IFormFileCollection? files, CancellationToken ct)
    {
        if (Parse<CreateTicketRequest>(payload) is not { } request) return BadPayload();
        return this.ToActionResult(await service.CreateAsync(request, Incoming(files), ct));
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<TicketDetailDto>> Get(long id, CancellationToken ct) =>
        this.ToActionResult(await service.GetAsync(id, ct));

    [HttpPost("{id:long}/comments")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUpload)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUpload)]
    public async Task<ActionResult<TicketDetailDto>> AddComment(long id, [FromForm] string payload, [FromForm] IFormFileCollection? files, CancellationToken ct)
    {
        if (Parse<AddCommentRequest>(payload) is not { } request) return BadPayload();
        return this.ToActionResult(await service.AddCommentAsync(id, request, Incoming(files), ct));
    }

    [HttpPost("{id:long}/attachments")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUpload)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUpload)]
    public async Task<ActionResult<TicketDetailDto>> AddAttachments(long id, [FromForm] IFormFileCollection files, CancellationToken ct) =>
        this.ToActionResult(await service.AddAttachmentsAsync(id, Incoming(files), ct));

    [HttpPost("{id:long}/status")]
    public async Task<ActionResult<TicketDetailDto>> ChangeStatus(long id, ChangeStatusRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.ChangeStatusAsync(id, request, ct));

    [HttpPost("{id:long}/priority")]
    public async Task<ActionResult<TicketDetailDto>> ChangePriority(long id, ChangePriorityRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.ChangePriorityAsync(id, request, ct));

    [HttpPost("{id:long}/assign")]
    public async Task<ActionResult<TicketDetailDto>> Assign(long id, AssignTicketRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.AssignAsync(id, request, ct));

    [HttpPost("{id:long}/transfer")]
    public async Task<ActionResult<TicketDetailDto>> Transfer(long id, TransferTicketRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.TransferAsync(id, request, ct));

    [HttpPost("{id:long}/delete")]
    public async Task<ActionResult<TicketDetailDto>> Delete(long id, DeleteTicketRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.DeleteAsync(id, request, ct));

    [HttpPost("{id:long}/restore")]
    public async Task<ActionResult<TicketDetailDto>> Restore(long id, CancellationToken ct) =>
        this.ToActionResult(await service.RestoreAsync(id, ct));

    [HttpGet("{id:long}/assignable-users")]
    public async Task<ActionResult<IReadOnlyList<AssignableUserDto>>> AssignableUsers(long id, CancellationToken ct) =>
        this.ToActionResult(await service.GetAssignableUsersAsync(id, ct));

    private static T? Parse<T>(string payload) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(payload, Json); }
        catch (JsonException) { return null; }
    }

    private ActionResult BadPayload() =>
        this.ToProblem(new TicketCore.Common.OperationError(TicketCore.Common.ErrorKind.Validation, "invalid_payload", "The request could not be read."));

    private static IReadOnlyList<IncomingFile> Incoming(IFormFileCollection? files) =>
        files is null ? [] : files.Select(f => new IncomingFile(f.FileName, f.ContentType, f.Length, f.OpenReadStream)).ToList();
}

[ApiController]
[Route("api/v1/attachments")]
public sealed class AttachmentsController(ITicketService service) : ControllerBase
{
    /// <summary>Always downloaded as a file (never shown inline), with the stored content type and nosniff.</summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Download(long id, CancellationToken ct)
    {
        var result = await service.OpenAttachmentAsync(id, ct);
        if (!result.Succeeded) return this.ToProblem(result.Error!);
        var (attachment, content) = result.Value;
        Response.Headers.CacheControl = "private, no-store";
        return File(content, attachment.ContentType, AttachmentPolicy.SafeName(attachment.FileName), enableRangeProcessing: false);
    }
}
