using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Synergos.CMS.Presentation.Api;

/// <summary>
/// Global error surface. Two routes:
///
/// <list type="bullet">
/// <item><c>/error</c> — landing for unhandled exceptions (via
///       <see cref="UseExceptionHandler"/>). Logs the full exception and
///       returns a safe 500 page — no stack trace leaks to the client.</item>
/// <item><c>/error/{statusCode}</c> — landing for 4xx/5xx re-execution
///       (via <see cref="UseStatusCodePagesWithReExecute"/>). Renders
///       a per-status-code friendly page.</item>
/// </list>
///
/// View lives at <c>Views/Shared/Error.cshtml</c> — keeps HTML out of C#.
/// </summary>
public sealed class ErrorController : Controller
{
    private readonly ILogger<ErrorController> _logger;
    public ErrorController(ILogger<ErrorController> logger) => _logger = logger;

    [Route("/error")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index()
    {
        var feature = HttpContext.Features.Get<IExceptionHandlerFeature>();
        if (feature?.Error is not null)
        {
            _logger.LogError(feature.Error,
                "Unhandled exception on {Path} — returning 500.",
                feature.Path);
        }

        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View("Error", new ErrorViewModel(StatusCodes.Status500InternalServerError));
    }

    [Route("/error/{statusCode:int}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult StatusCode(int statusCode)
    {
        Response.StatusCode = statusCode;
        return View("Error", new ErrorViewModel(statusCode));
    }
}

public sealed record ErrorViewModel(int StatusCode);
