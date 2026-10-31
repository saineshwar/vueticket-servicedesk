using Microsoft.AspNetCore.Mvc;
using TicketCore.Common;

namespace TicketCore.Api.Infrastructure;

/// <summary>Turns a failed service result into an RFC 7807 problem response with a stable error code.</summary>
public static class ResultMapping
{
    public static ActionResult ToProblem(this ControllerBase controller, OperationError error)
    {
        var status = error.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Title = error.Message,
            Type = $"https://vueticket/errors/{error.Code}"
        };
        problem.Extensions["code"] = error.Code;
        return controller.StatusCode(status, problem);
    }

    public static ActionResult<T> ToActionResult<T>(this ControllerBase controller, OperationResult<T> result) =>
        result.Succeeded ? controller.Ok(result.Value) : controller.ToProblem(result.Error!);

    public static ActionResult ToActionResult(this ControllerBase controller, OperationResult result) =>
        result.Succeeded ? controller.NoContent() : controller.ToProblem(result.Error!);
}
