using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Web.Common.Controllers;
using Synergos.CMS.Application.Orchestration;
using Synergos.CMS.Domain.Orchestration;
using Synergos.CMS.Presentation.Filters;

namespace Synergos.CMS.Presentation.Api;

/// <summary>
/// Internal API for the Synergos Flow Engine.
///
/// Umbraco acts as the configuration plane: FlowDefinition nodes are created
/// and managed in the backoffice, then published to Synergos.API via webhook.
///
/// Endpoints:
///   GET  /api/synergos/v1/orchestration/flows
///        → List all published FlowDefinition configs
///
///   GET  /api/synergos/v1/orchestration/flows/{alias}
///        → Get a single FlowDefinition config by alias
///
///   POST /api/synergos/v1/orchestration/flows/{alias}/publish
///        → Read FlowDefinition from Umbraco, sign, POST to Synergos.API
///
///   POST /api/synergos/v1/orchestration/flows/{alias}/trigger
///        Body: { "caseId": "...", "input": { ... } }
///        → Relay execution request to Synergos.API and return the result
///
/// Authentication: X-Api-Key header — key configured in Synergos:Security:ApiKey.
/// </summary>
[ApiController]
[Route("api/synergos/v1/orchestration")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public sealed class FlowOrchestrationController : UmbracoApiController
{
    private readonly FlowConfigurationService             _configService;
    private readonly IFlowWebhookDispatcher               _dispatcher;
    private readonly ILogger<FlowOrchestrationController> _logger;

    public FlowOrchestrationController(
        FlowConfigurationService             configService,
        IFlowWebhookDispatcher               dispatcher,
        ILogger<FlowOrchestrationController> logger)
    {
        _configService = configService;
        _dispatcher    = dispatcher;
        _logger        = logger;
    }

    // ─── GET /flows ───────────────────────────────────────────────────────────

    /// <summary>Lists all published FlowDefinition configurations.</summary>
    [HttpGet("flows")]
    [Produces("application/json")]
    public IActionResult GetFlows()
    {
        var flows = _configService.GetAll();

        return Ok(new
        {
            count = flows.Count,
            flows = flows.Select(MapToSummary)
        });
    }

    // ─── GET /flows/{alias} ───────────────────────────────────────────────────

    /// <summary>Returns the full FlowConfig for a specific alias.</summary>
    [HttpGet("flows/{alias}")]
    [Produces("application/json")]
    public IActionResult GetFlow(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return BadRequest(new { error = "alias is required." });

        var flow = _configService.GetByAlias(alias);
        if (flow is null)
            return NotFound(new { error = $"No published FlowDefinition found with alias '{alias}'." });

        return Ok(MapToDetail(flow));
    }

    // ─── POST /flows/{alias}/publish ──────────────────────────────────────────

    /// <summary>
    /// Reads the FlowDefinition from Umbraco, signs the payload with HMAC-SHA256,
    /// and POSTs it to the engine URL configured on the node.
    /// </summary>
    [HttpPost("flows/{alias}/publish")]
    [Produces("application/json")]
    public async Task<IActionResult> PublishFlow(string alias, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return BadRequest(new { error = "alias is required." });

        var flow = _configService.GetByAlias(alias);
        if (flow is null)
            return NotFound(new { error = $"No published FlowDefinition found with alias '{alias}'." });

        if (!flow.IsActive)
        {
            return UnprocessableEntity(new
            {
                error    = $"Flow '{alias}' is inactive (isActive=false). Activate it in the backoffice before publishing.",
                flowAlias = alias
            });
        }

        _logger.LogInformation(
            "FlowOrchestration: publishing flow '{Alias}' (mode={Mode}) to {Url}.",
            alias, flow.ExecutionMode, flow.WebhookTargetUrl);

        var result = await _dispatcher.PublishAsync(flow, ct);

        if (result.Success)
        {
            return Ok(new
            {
                published        = true,
                flowAlias        = result.FlowAlias,
                engineStatusCode = result.EngineStatusCode,
                executionMode    = flow.ExecutionMode.ToString().ToLowerInvariant(),
                trackCount       = flow.Tracks.Count,
                outcomeCount     = flow.Outcomes.Count,
                publishedAt      = DateTime.UtcNow.ToString("O")
            });
        }

        return StatusCode(502, new
        {
            published = false,
            flowAlias = result.FlowAlias,
            error     = result.Error,
            engineStatusCode = result.EngineStatusCode
        });
    }

    // ─── POST /flows/{alias}/trigger ─────────────────────────────────────────

    /// <summary>
    /// Relays an execution request to Synergos.API and returns the result.
    /// Body: { "caseId": "abc-123", "input": { "amount": 5000, "priority": "high" } }
    /// </summary>
    [HttpPost("flows/{alias}/trigger")]
    [Produces("application/json")]
    public async Task<IActionResult> TriggerFlow(
        string                  alias,
        [FromBody] TriggerRequest body,
        CancellationToken       ct)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return BadRequest(new { error = "alias is required." });

        if (string.IsNullOrWhiteSpace(body?.CaseId))
            return BadRequest(new { error = "caseId is required in the request body." });

        var flow = _configService.GetByAlias(alias);
        if (flow is null)
            return NotFound(new { error = $"No published FlowDefinition found with alias '{alias}'." });

        if (!flow.IsActive)
            return StatusCode(423, new { error = $"Flow '{alias}' is inactive.", flowAlias = alias });

        _logger.LogInformation(
            "FlowOrchestration: triggering flow '{Alias}' with caseId '{CaseId}'.",
            alias, body.CaseId);

        var (success, responseBody, statusCode) = await _dispatcher.TriggerAsync(
            flow, body.CaseId, body.Input ?? [], ct);

        if (success && responseBody is not null)
        {
            // Parse and return the engine response as-is
            try
            {
                var parsed = JsonSerializer.Deserialize<object>(responseBody);
                return StatusCode(statusCode ?? 200, parsed);
            }
            catch
            {
                return StatusCode(statusCode ?? 200, responseBody);
            }
        }

        return StatusCode(502, new
        {
            error      = responseBody ?? "Engine did not respond.",
            statusCode,
            caseId     = body.CaseId,
            flowAlias  = alias
        });
    }

    // ─── Response shapes ──────────────────────────────────────────────────────

    private static object MapToSummary(FlowConfig f) => new
    {
        flowAlias     = f.FlowAlias,
        flowName      = f.FlowName,
        executionMode = f.ExecutionMode.ToString().ToLowerInvariant(),
        isActive      = f.IsActive,
        trackCount    = f.Tracks.Count,
        outcomeCount  = f.Outcomes.Count,
        schemaVersion = f.SchemaVersion
    };

    private static object MapToDetail(FlowConfig f) => new
    {
        flowAlias        = f.FlowAlias,
        flowName         = f.FlowName,
        flowDescription  = f.FlowDescription,
        executionMode    = f.ExecutionMode.ToString().ToLowerInvariant(),
        timeoutMs        = f.TimeoutMs,
        maxRetries       = f.MaxRetries,
        isActive         = f.IsActive,
        webhookTargetUrl = f.WebhookTargetUrl,
        schemaVersion    = f.SchemaVersion,
        tracks           = f.Tracks.Select(t => new
        {
            trackId   = t.TrackId,
            name      = t.Name,
            order     = t.Order,
            stepType  = t.StepType,
            condition = t.Condition,
            onFailure = t.OnFailure
        }),
        outcomes = f.Outcomes.Select(o => new
        {
            code           = o.Code,
            label          = o.Label,
            httpStatus     = o.HttpStatus,
            triggerWebhook = o.TriggerWebhook
        })
    };
}

/// <summary>Request body for the /trigger endpoint.</summary>
public sealed class TriggerRequest
{
    public string CaseId { get; set; } = string.Empty;
    public Dictionary<string, object?> Input { get; set; } = [];
}
