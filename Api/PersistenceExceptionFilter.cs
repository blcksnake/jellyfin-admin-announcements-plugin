using System;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.Announcements.Api;

/// <summary>Report storage failures without exposing server paths to the caller.</summary>
public sealed class PersistenceExceptionFilter : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not IOException and not UnauthorizedAccessException) return;
        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = 503, Title = "Announcement storage is unavailable.",
            Detail = "The change was not saved. Check server logs and storage permissions before retrying."
        }) { StatusCode = 503 };
        context.ExceptionHandled = true;
    }
}
