using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TicketCore.Api.Infrastructure;

/// <summary>
/// When the browser cancels a request (navigating away, closing a tab), the database call is cancelled too.
/// That is not a server fault, so the exception is absorbed here, answered with 499 and logged at debug level,
/// instead of reaching the global handler and being logged as an error.
/// </summary>
public sealed class ClientAbortedFilter(ILogger<ClientAbortedFilter> logger) : IExceptionFilter
{
    public const int ClientClosedRequest = 499;

    public void OnException(ExceptionContext context)
    {
        if (!context.HttpContext.RequestAborted.IsCancellationRequested) return;

        logger.LogDebug("Request {Method} {Path} was cancelled by the client",
            context.HttpContext.Request.Method, context.HttpContext.Request.Path);
        context.Result = new StatusCodeResult(ClientClosedRequest);
        context.ExceptionHandled = true;
    }
}
