using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// API JSON del Dashboard (ADR 0097). Lo consume la app Angular
/// <c>&lt;synergos-dashboard&gt;</c>. Solo lectura; toda la lógica vive en
/// <see cref="IDashboardReadModel"/> (compartido con el <c>/admin</c> SSR).
/// </summary>
/// <remarks>
/// Auth-gate de admin en el entry-point de cada endpoint vía
/// <see cref="IMemberAccessGate"/>: anónimo → 401, sin rol admin → 403.
/// No hay ownership por-recurso (no hay PHI). Cada vista emite el evento de
/// auditoría <c>dashboard.viewed</c>.
/// </remarks>
[ApiController]
[Route("api/dashboard")]
public sealed class DashboardApiController : ControllerBase
{
    private const string AdminRoles = "admin";
    private const int DefaultRangeDays = 30;

    private readonly IDashboardReadModel _readModel;
    private readonly IMemberAccessGate _gate;
    private readonly IAnalyticsTracker _analytics;

    public DashboardApiController(
        IDashboardReadModel readModel,
        IMemberAccessGate gate,
        IAnalyticsTracker analytics)
    {
        _readModel = readModel;
        _gate = gate;
        _analytics = analytics;
    }

    [HttpGet("sales")]
    public IActionResult GetSales([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? granularity)
    {
        if (Deny("sales") is { } denied) return denied;
        var (f, t, g) = ParseRange(from, to, granularity);
        return Ok(_readModel.GetSalesOverview(f, t, g));
    }

    [HttpGet("members")]
    public IActionResult GetMembers([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? granularity)
    {
        if (Deny("members") is { } denied) return denied;
        var (f, t, g) = ParseRange(from, to, granularity);
        return Ok(_readModel.GetMemberProgress(f, t, g));
    }

    [HttpGet("behavior")]
    public IActionResult GetBehavior([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int topN = 20)
    {
        if (Deny("behavior") is { } denied) return denied;
        var (f, t, _) = ParseRange(from, to, null);
        return Ok(_readModel.GetBehavior(f, t, topN));
    }

    [HttpGet("search")]
    public IActionResult GetSearch([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int limit = 10)
    {
        if (Deny("search") is { } denied) return denied;
        var (f, t, _) = ParseRange(from, to, null);
        return Ok(_readModel.GetSearchInsights(f, t, limit));
    }

    [HttpGet("webhooks")]
    public IActionResult GetWebhooks()
    {
        if (Deny("webhooks") is { } denied) return denied;
        return Ok(_readModel.GetWebhookHealth());
    }

    [HttpGet("insights")]
    public IActionResult GetInsights()
    {
        if (Deny("insights") is { } denied) return denied;
        return Ok(_readModel.GetAgentInsights());
    }

    // Gate de admin en dos capas (auth + rol). Devuelve el IActionResult de
    // rechazo, o null si pasa (y registra la vista en auditoría).
    private IActionResult? Deny(string panel)
    {
        if (!_gate.IsAuthenticated)
        {
            return Unauthorized();
        }
        if (!_gate.HasAnyRole(AdminRoles))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
        _analytics.Track("dashboard.viewed", new Dictionary<string, object?> { ["panel"] = panel });
        return null;
    }

    private static (DateTime from, DateTime to, MetricGranularity granularity) ParseRange(
        DateTime? from, DateTime? to, string? granularity)
    {
        var toUtc = to ?? DateTime.UtcNow;
        var fromUtc = from ?? toUtc.AddDays(-DefaultRangeDays);
        if (fromUtc > toUtc)
        {
            (fromUtc, toUtc) = (toUtc, fromUtc);
        }
        var gran = string.Equals(granularity, "hour", StringComparison.OrdinalIgnoreCase)
            ? MetricGranularity.Hour
            : MetricGranularity.Day;
        return (fromUtc, toUtc, gran);
    }
}
