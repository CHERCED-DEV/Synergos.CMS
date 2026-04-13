using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Phase 12 — Flow Engine schema.
///
/// Creates the operational configuration layer used by Synergos Flow Engine:
///
///   PlatformRoot
///   └── FlowSettingsRoot        (container — no properties)
///       └── FlowDefinition      (operational flow config)
///             ├─ General tab:  flowAlias, flowName, flowDescription, isActive
///             ├─ Execution tab: executionMode, timeoutMs, maxRetries, schemaVersion
///             ├─ Webhook tab:  webhookTargetUrl
///             └─ Tracks tab:   tracksJson, outcomesJson (JSON textareas)
///
/// Design note: tracks and outcomes are stored as JSON text rather than Block Lists.
/// This trades a richer backoffice UI for dramatically simpler seeding and service
/// implementation. A production upgrade would migrate these to Block Lists.
///
/// DataTypes (SelectFlowExecutionMode, TextAreaJson) are created in DataTypeInitializer
/// (Phase 1a) so they are available to all phases without ordering constraints.
/// </summary>
internal sealed class FlowEngineInitializer : SchemaInitializerBase
{
    private readonly ILogger<FlowEngineInitializer> _logger;

    public FlowEngineInitializer(
        IContentTypeService cts,
        IDataTypeService    dts,
        IShortStringHelper  ssh,
        ILogger<FlowEngineInitializer>? logger = null)
        : base(cts, dts, ssh)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FlowEngineInitializer>.Instance;
    }

    public override void Initialize()
    {
        var engineFolderId = EnsureRootFolder("Flow Engine");

        EnsureFlowDefinition(engineFolderId);
        EnsureFlowSettingsRoot(engineFolderId);

        PatchAllowedChildren();
    }

    // ─── Document Types ───────────────────────────────────────────────────────

    private void EnsureFlowDefinition(int folderId)
    {
        if (Cts.Get(ContentTypeKeys.FlowDefinition) is not null) return;
        if (Cts.Get("flowDefinition") is not null) return;
        // Guard against orphaned umbracoNode rows from partial prior runs

        var ct = new ContentType(Ssh, folderId)
        {
            Key           = ContentTypeKeys.FlowDefinition,
            Name          = "Flow Definition",
            Alias         = "flowDefinition",
            Icon          = "icon-shuffle",
            Description   = "Operational flow configuration managed from backoffice and published to the Flow Engine.",
            IsElement     = false,
            AllowedAsRoot = false
        };

        // ── Tab: General ──────────────────────────────────────────────────
        var generalTab = Tab("General", "general", 0);
        generalTab.PropertyTypes!.Add(Prop("flowAlias", "Flow Alias",
            DataTypeKeys.TextIdentifier, 0, mandatory: true,
            description: "Unique identifier used by services and webhooks. Use kebab-case: approval-flow, onboarding-check."));
        generalTab.PropertyTypes!.Add(Prop("flowName", "Flow Name",
            DataTypeKeys.TextTitle, 10, mandatory: true,
            description: "Human-readable name shown in backoffice and API responses."));
        generalTab.PropertyTypes!.Add(Prop("flowDescription", "Description",
            DataTypeKeys.TextAreaNotes, 20,
            description: "Optional description of what this flow does and when it is triggered."));
        generalTab.PropertyTypes!.Add(Prop("isActive", "Active",
            DataTypeKeys.ToggleBoolean, 30,
            description: "When disabled, the engine will reject executions of this flow with 423 Locked."));
        ct.PropertyGroups.Add(generalTab);

        // ── Tab: Execution ────────────────────────────────────────────────
        var execTab = Tab("Execution", "execution", 10);
        execTab.PropertyTypes!.Add(Prop("executionMode", "Execution Mode",
            DataTypeKeys.SelectFlowExecutionMode, 0, mandatory: true,
            description: "sequential: tracks run one by one. parallel: all tracks run simultaneously. firstMatch: first track whose condition is true runs."));
        execTab.PropertyTypes!.Add(Prop("timeoutMs", "Timeout (ms)",
            DataTypeKeys.NumberInteger, 10,
            description: "Maximum total execution time in milliseconds. 0 = no timeout. Recommended: 5000–15000."));
        execTab.PropertyTypes!.Add(Prop("maxRetries", "Max Retries",
            DataTypeKeys.NumberInteger, 20,
            description: "Number of retry attempts when a track fails and onFailure=retry. 0 = no retries."));
        execTab.PropertyTypes!.Add(Prop("schemaVersion", "Schema Version",
            DataTypeKeys.NumberInteger, 30,
            description: "Contract version sent to the engine. Increment when the track/outcome structure changes. Engine will reject unknown versions."));
        ct.PropertyGroups.Add(execTab);

        // ── Tab: Webhook ──────────────────────────────────────────────────
        var webhookTab = Tab("Webhook", "webhook", 20);
        webhookTab.PropertyTypes!.Add(Prop("webhookTargetUrl", "Engine URL",
            DataTypeKeys.TextUrl, 0, mandatory: true,
            description: "Full URL of Synergos.API endpoint that receives published configurations. Example: http://localhost:5002/api/engine/flows/register"));
        ct.PropertyGroups.Add(webhookTab);

        // ── Tab: Tracks & Outcomes ────────────────────────────────────────
        var tracksTab = Tab("Tracks & Outcomes", "tracksOutcomes", 30);
        tracksTab.PropertyTypes!.Add(Prop("tracksJson", "Tracks (JSON)",
            DataTypeKeys.TextAreaJson, 0,
            description: "JSON array of track definitions. Each track: { trackId, name, order, stepType, condition, stepConfig, onFailure }. condition syntax: \"field==value\", \"field<=value\", or empty string for always-execute."));
        tracksTab.PropertyTypes!.Add(Prop("outcomesJson", "Outcomes (JSON)",
            DataTypeKeys.TextAreaJson, 10,
            description: "JSON array of outcome definitions. Each outcome: { code, label, httpStatus, triggerWebhook, webhookUrl }. The engine picks the first matching outcome after all tracks complete."));
        ct.PropertyGroups.Add(tracksTab);

        TrySave(ct);
    }

    private void EnsureFlowSettingsRoot(int folderId)
    {
        if (Cts.Get(ContentTypeKeys.FlowSettingsRoot) is not null) return;
        if (Cts.Get("flowSettingsRoot") is not null) return;

        var ct = new ContentType(Ssh, folderId)
        {
            Key           = ContentTypeKeys.FlowSettingsRoot,
            Name          = "Flow Settings Root",
            Alias         = "flowSettingsRoot",
            Icon          = "icon-settings",
            Description   = "Container for all FlowDefinition nodes. Place under PlatformRoot.",
            IsElement     = false,
            AllowedAsRoot = false
        };

        // No properties — this is a structural container only.
        TrySave(ct);
    }

    // ─── Allowed children patch ───────────────────────────────────────────────

    private void PatchAllowedChildren()
    {
        // FlowSettingsRoot → FlowDefinition
        AddAllowedChild(ContentTypeKeys.FlowSettingsRoot, ContentTypeKeys.FlowDefinition);

        // SiteRoot → FlowSettingsRoot  (Flow Settings lives under the Flow Engine Demo siteRoot)
        AddAllowedChild(ContentTypeKeys.SiteRoot, ContentTypeKeys.FlowSettingsRoot);
    }

    private void AddAllowedChild(Guid parentKey, Guid childKey)
    {
        var parent = Cts.Get(parentKey);
        var child  = Cts.Get(childKey);

        if (parent is null || child is null)
        {
            _logger.LogDebug(
                "FlowEngineInitializer.AddAllowedChild: parent={ParentKey} or child={ChildKey} not found — skipped.",
                parentKey, childKey);
            return;
        }

        var existing   = parent.AllowedContentTypes?.ToList() ?? [];
        var alreadyHas = existing.Any(a => a.Id.Value == child.Id);
        if (alreadyHas) return;

        existing.Add(new ContentTypeSort(
            new Lazy<int>(() => child.Id), existing.Count, child.Alias));

        parent.AllowedContentTypes = existing;

        try
        {
            Cts.Save(parent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "FlowEngineInitializer.AddAllowedChild: failed to save parent {ParentAlias} after adding child {ChildAlias}. " +
                "Allowed-children patch may be incomplete — check manually in backoffice.",
                parent.Alias, child.Alias);
        }
    }

    // ─── Safe save ───────────────────────────────────────────────────────────

    /// <summary>
    /// Saves a content type, catching all exceptions that indicate a conflict or
    /// prior-run orphan (UNIQUE constraint, DuplicateNameException, table lock).
    /// Logs a Warning so the incident is traceable, then lets the pipeline continue.
    ///
    /// If the type still does not exist after TrySave (creation truly failed), a
    /// subsequent Warning is emitted. In that case, check for orphaned rows:
    ///   SELECT uniqueId, text FROM umbracoNode WHERE uniqueId = '{key}';
    /// and delete the orphan if present before re-running.
    /// </summary>
    private void TrySave(IContentType ct)
    {
        try
        {
            Cts.Save(ct);
            _logger.LogDebug("FlowEngineInitializer: created ContentType '{Alias}' ({Key}).", ct.Alias, ct.Key);
        }
        catch (Exception ex)
        {
            var existing = Cts.Get(ct.Key) ?? Cts.Get(ct.Alias);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "FlowEngineInitializer.TrySave: ContentType '{Alias}' exists (Id={Id}) after save error — continuing pipeline.",
                    ct.Alias, existing.Id);
                return;
            }

            _logger.LogWarning(ex,
                "FlowEngineInitializer.TrySave: ContentType '{Alias}' ({TypeKey}) was NOT created ({ExType}). " +
                "Flow Engine will not be functional. " +
                "Check for orphaned umbracoNode row: SELECT uniqueId, text FROM umbracoNode WHERE uniqueId='{OrphanKey}';",
                ct.Alias, ct.Key, ex.GetType().Name, ct.Key);
        }
    }
}
