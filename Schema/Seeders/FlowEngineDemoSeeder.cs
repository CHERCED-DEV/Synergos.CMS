using System.Text.Json;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Schema.Seeders;

/// <summary>
/// Seeds two demo FlowDefinition nodes under FlowSettingsRoot.
///
/// Idempotent: checks for existing nodes by flowAlias property before creating.
/// Runs as part of the schema pipeline after FlowEngineInitializer (Phase 12).
///
/// Nodes created:
///   Flow Settings Root
///   ├── approval-flow        (ExecutionMode: sequential — 3 tracks, 3 outcomes)
///   └── notification-pipeline (ExecutionMode: parallel — 2 tracks, 2 outcomes)
///
/// These nodes serve as the demo payload for the Flow Engine demo.
/// Edit them in the backoffice to demonstrate live configuration changes.
/// </summary>
internal sealed class FlowEngineDemoSeeder
{
    private readonly IContentService             _content;
    private readonly IContentTypeService         _contentTypes;
    private readonly ILogger                     _logger;

    private const string ExecutionModeKey = "executionMode";

    public FlowEngineDemoSeeder(
        IContentService     content,
        IContentTypeService contentTypes,
        ILogger             logger)
    {
        _content      = content;
        _contentTypes = contentTypes;
        _logger       = logger;
    }

    public void Seed()
    {
        // 1. Find PlatformRoot
        var platformRoot = _content.GetRootContent()
            .FirstOrDefault(c => c.ContentType.Alias == ContentTypeKeys.Aliases.PlatformRoot);

        if (platformRoot is null)
        {
            _logger.LogWarning(
                "FlowEngineDemoSeeder: 'platformRoot' not found — cannot seed flow nodes. " +
                "Create FlowSettingsRoot manually in backoffice.");
            return;
        }

        // 2. Find the Flow Engine Demo siteRoot — FlowSettingsRoot lives under it
        var demoSite = _content.GetPagedChildren(platformRoot.Id, 0, 100, out _)
            .FirstOrDefault(c =>
                c.ContentType.Alias == ContentTypeKeys.Aliases.SiteRoot &&
                string.Equals(c.Name, "Flow Engine Demo", StringComparison.OrdinalIgnoreCase));

        if (demoSite is null)
        {
            _logger.LogWarning(
                "FlowEngineDemoSeeder: 'Flow Engine Demo' siteRoot not found — " +
                "ensure FlowDemoSiteSeeder runs first.");
            return;
        }

        // 3. Find or create FlowSettingsRoot under the demo siteRoot
        var flowRoot = GetOrCreateFlowSettingsRoot(demoSite);
        if (flowRoot is null)
        {
            _logger.LogWarning("FlowEngineDemoSeeder: could not find or create 'flowSettingsRoot'.");
            return;
        }

        // 3. Seed demo flows (create if missing)
        SeedApprovalFlow(flowRoot.Id);
        SeedNotificationPipeline(flowRoot.Id);

        // 4. Repair executionMode if previously seeded with plain string instead of JSON array
        RepairExecutionModeIfNeeded(flowRoot.Id, "approval-flow",        "sequential");
        RepairExecutionModeIfNeeded(flowRoot.Id, "notification-pipeline", "parallel");
    }

    // ─── FlowSettingsRoot ─────────────────────────────────────────────────────

    private IContent? GetOrCreateFlowSettingsRoot(IContent demoSite)
    {
        // Flow Settings Root lives inside the Config folder of the demo site.
        // FlowDemoSiteSeeder creates Config before this seeder runs (pipeline ordering).
        var config = _content.GetPagedChildren(demoSite.Id, 0, 100, out _)
            .FirstOrDefault(c =>
                c.ContentType.Alias == ContentTypeKeys.Aliases.SharedContentFolder &&
                string.Equals(c.Name, "Config", StringComparison.OrdinalIgnoreCase));

        var parentId = config?.Id ?? demoSite.Id;

        // Check if it already exists under Config (or directly under demoSite as fallback)
        var existing = _content.GetPagedChildren(parentId, 0, 100, out _)
            .FirstOrDefault(c => c.ContentType.Alias == ContentTypeKeys.Aliases.FlowSettingsRootAlias);

        if (existing is not null)
        {
            _logger.LogDebug("FlowEngineDemoSeeder: 'flowSettingsRoot' already exists (Id={Id}).", existing.Id);
            return existing;
        }

        var flowRootType = _contentTypes.Get("flowSettingsRoot");
        if (flowRootType is null)
        {
            _logger.LogWarning(
                "FlowEngineDemoSeeder: contentType 'flowSettingsRoot' not found. " +
                "Did FlowEngineInitializer run successfully?");
            return null;
        }

        var node = _content.Create("Flow Settings Root", parentId, "flowSettingsRoot");
        var result = _content.SaveAndPublish(node);

        if (!result.Success)
        {
            _logger.LogWarning(
                "FlowEngineDemoSeeder: failed to publish 'flowSettingsRoot'. Status: {Status}.",
                result.Result);
            return null;
        }

        _logger.LogInformation(
            "FlowEngineDemoSeeder: created 'flowSettingsRoot' under '{Parent}' (Id={Id}).",
            config is not null ? "Config" : demoSite.Name, node.Id);
        return node;
    }

    // ─── approval-flow ────────────────────────────────────────────────────────

    private void SeedApprovalFlow(int parentId)
    {
        const string alias = "approval-flow";

        if (FlowExists(parentId, alias))
        {
            _logger.LogDebug("FlowEngineDemoSeeder: flow '{Alias}' already exists — skipped.", alias);
            return;
        }

        const string tracksJson = """
            [
              {
                "trackId": "validate-amount",
                "name": "Validar Monto",
                "order": 1,
                "stepType": "validate",
                "condition": "amount<=10000",
                "stepConfig": { "maxAmount": 10000, "currency": "USD" },
                "onFailure": "stop"
              },
              {
                "trackId": "enrich-requester",
                "name": "Enriquecer Solicitante",
                "order": 2,
                "stepType": "enrich",
                "condition": "",
                "stepConfig": { "source": "crm", "fields": ["tier", "history"] },
                "onFailure": "continue"
              },
              {
                "trackId": "notify-approver",
                "name": "Notificar Aprobador",
                "order": 3,
                "stepType": "notify",
                "condition": "priority==high",
                "stepConfig": { "channel": "email", "template": "approval-required" },
                "onFailure": "continue"
              }
            ]
            """;

        const string outcomesJson = """
            [
              {
                "code": "APPROVED",
                "label": "Aprobado automáticamente",
                "httpStatus": 200,
                "triggerWebhook": false
              },
              {
                "code": "PENDING_REVIEW",
                "label": "Pendiente de revisión manual",
                "httpStatus": 202,
                "triggerWebhook": true,
                "webhookUrl": ""
              },
              {
                "code": "REJECTED",
                "label": "Rechazado — monto excede límite",
                "httpStatus": 422,
                "triggerWebhook": false
              }
            ]
            """;

        var node = _content.Create("approval-flow", parentId, "flowDefinition");
        node.SetValue("flowAlias",        alias);
        node.SetValue("flowName",         "Flujo de Aprobación");
        node.SetValue("flowDescription",  "Demo flow: validates amount, enriches data, notifies approver. Change executionMode in backoffice to see behavior change.");
        node.SetValue(ExecutionModeKey,    Dropdown("sequential"));
        node.SetValue("timeoutMs",        8000);
        node.SetValue("maxRetries",       2);
        node.SetValue("isActive",         true);
        node.SetValue("webhookTargetUrl", "http://localhost:5002/api/engine/flows/register");
        node.SetValue("schemaVersion",    1);
        node.SetValue("tracksJson",       tracksJson.Trim());
        node.SetValue("outcomesJson",     outcomesJson.Trim());

        PublishNode(node, alias);
    }

    // ─── notification-pipeline ────────────────────────────────────────────────

    private void SeedNotificationPipeline(int parentId)
    {
        const string alias = "notification-pipeline";

        if (FlowExists(parentId, alias))
        {
            _logger.LogDebug("FlowEngineDemoSeeder: flow '{Alias}' already exists — skipped.", alias);
            return;
        }

        const string tracksJson = """
            [
              {
                "trackId": "send-email",
                "name": "Enviar Email",
                "order": 1,
                "stepType": "notify",
                "condition": "",
                "stepConfig": { "channel": "email", "template": "notification-base" },
                "onFailure": "continue"
              },
              {
                "trackId": "send-sms",
                "name": "Enviar SMS",
                "order": 2,
                "stepType": "notify",
                "condition": "sms==true",
                "stepConfig": { "channel": "sms", "provider": "twilio" },
                "onFailure": "continue"
              },
              {
                "trackId": "post-webhook",
                "name": "Publicar a Webhook",
                "order": 3,
                "stepType": "transform",
                "condition": "",
                "stepConfig": { "url": "https://hooks.example.com/notify", "method": "POST" },
                "onFailure": "continue"
              }
            ]
            """;

        const string outcomesJson = """
            [
              {
                "code": "DISPATCHED",
                "label": "Notificaciones enviadas correctamente",
                "httpStatus": 200,
                "triggerWebhook": false
              },
              {
                "code": "PARTIAL",
                "label": "Algunas notificaciones fallaron",
                "httpStatus": 207,
                "triggerWebhook": false
              }
            ]
            """;

        var node = _content.Create("notification-pipeline", parentId, "flowDefinition");
        node.SetValue("flowAlias",        alias);
        node.SetValue("flowName",         "Pipeline de Notificaciones");
        node.SetValue("flowDescription",  "Demo flow: runs email, SMS and webhook notifications in parallel. Change executionMode to sequential to compare.");
        node.SetValue(ExecutionModeKey,    Dropdown("parallel"));
        node.SetValue("timeoutMs",        5000);
        node.SetValue("maxRetries",       1);
        node.SetValue("isActive",         true);
        node.SetValue("webhookTargetUrl", "http://localhost:5002/api/engine/flows/register");
        node.SetValue("schemaVersion",    1);
        node.SetValue("tracksJson",       tracksJson.Trim());
        node.SetValue("outcomesJson",     outcomesJson.Trim());

        PublishNode(node, alias);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private IContent? FindFlow(int parentId, string alias)
    {
        return _content.GetPagedChildren(parentId, 0, 200, out _)
            .FirstOrDefault(c => c.ContentType.Alias == ContentTypeKeys.Aliases.FlowDefinitionAlias &&
                                 c.GetValue<string>("flowAlias") == alias);
    }

    private bool FlowExists(int parentId, string alias) =>
        FindFlow(parentId, alias) is not null;

    /// <summary>
    /// Fixes an already-published flow node if its executionMode is stored
    /// as a plain string instead of a DropDownListFlexible JSON array.
    /// Umbraco crashes with JsonReaderException when the editor tries to open it.
    /// </summary>
    private void RepairExecutionModeIfNeeded(int parentId, string alias, string expectedMode)
    {
        var node = FindFlow(parentId, alias);
        if (node is null) return;

        var raw = node.GetValue<string>(ExecutionModeKey) ?? "";
        // Already correct (JSON array) — nothing to do
        if (raw.StartsWith('[')) return;

        _logger.LogInformation(
            "FlowEngineDemoSeeder: repairing executionMode on '{Alias}' (Id={Id}): '{Raw}' → '{Fixed}'.",
            alias, node.Id, raw, expectedMode);

        node.SetValue(ExecutionModeKey, Dropdown(expectedMode));
        PublishNode(node, alias);
    }

    private void PublishNode(IContent node, string alias)
    {
        var result = _content.SaveAndPublish(node);
        if (result.Success)
            _logger.LogInformation("FlowEngineDemoSeeder: published flow '{Alias}' (Id={Id}).", alias, node.Id);
        else
            _logger.LogWarning(
                "FlowEngineDemoSeeder: could not publish flow '{Alias}'. Status: {Status}.",
                alias, result.Result);
    }

    /// <summary>Serialises a single value as a DropDownListFlexible JSON array, e.g. ["sequential"].</summary>
    private static string Dropdown(string value) => JsonSerializer.Serialize(new[] { value });
}
