using System.Text.Json;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Extensions;
using Synergos.CMS.Domain.Orchestration;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Application.Orchestration;

/// <summary>
/// Reads FlowDefinition nodes from the Umbraco published content cache
/// and maps them to immutable FlowConfig domain records.
///
/// Lifetime: Scoped — requires an active content cache (published request context).
///
/// Content tree expected:
///   PlatformRoot
///   └── flowSettingsRoot
///       └── flowDefinition  (alias property: "flowAlias")
///
/// All FlowDefinition nodes must be PUBLISHED to be visible here.
/// </summary>
public sealed class FlowConfigurationService
{
    private readonly IContentContextAccessor           _accessor;
    private readonly ILogger<FlowConfigurationService> _logger;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public FlowConfigurationService(
        IContentContextAccessor            accessor,
        ILogger<FlowConfigurationService>  logger)
    {
        _accessor = accessor;
        _logger   = logger;
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Returns all published FlowDefinition nodes as FlowConfig records.</summary>
    public IReadOnlyList<FlowConfig> GetAll()
    {
        var result = new List<FlowConfig>();

        var root = FindFlowSettingsRoot();
        if (root is null)
        {
            _logger.LogWarning("FlowConfigurationService: no published '{Alias}' node found.", ContentTypeKeys.Aliases.FlowSettingsRootAlias);
            return result;
        }

        foreach (var child in root.Children ?? Enumerable.Empty<IPublishedContent>())
        {
            if (child.ContentType.Alias != ContentTypeKeys.Aliases.FlowDefinitionAlias) continue;

            var config = TryMap(child);
            if (config is not null) result.Add(config);
        }

        return result;
    }

    /// <summary>Returns a single FlowConfig by its flowAlias property, or null if not found.</summary>
    public FlowConfig? GetByAlias(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias)) return null;

        var root = FindFlowSettingsRoot();
        if (root is null) return null;

        var node = root.Children?
            .FirstOrDefault(c =>
                c.ContentType.Alias == ContentTypeKeys.Aliases.FlowDefinitionAlias &&
                c.Value<string>("flowAlias") == alias);

        return node is null ? null : TryMap(node);
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private IPublishedContent? FindFlowSettingsRoot()
    {
        var roots = _accessor.GetAtRoot();

        foreach (var rootNode in roots)
        {
            if (rootNode.ContentType.Alias == ContentTypeKeys.Aliases.PlatformRoot)
            {
                return rootNode.Children?
                    .FirstOrDefault(c => c.ContentType.Alias == ContentTypeKeys.Aliases.FlowSettingsRootAlias);
            }

            if (rootNode.ContentType.Alias == ContentTypeKeys.Aliases.FlowSettingsRootAlias)
                return rootNode;
        }

        return null;
    }

    private FlowConfig? TryMap(IPublishedContent node)
    {
        try
        {
            var alias = node.Value<string>("flowAlias")?.Trim();
            if (string.IsNullOrWhiteSpace(alias))
            {
                _logger.LogWarning("FlowConfigurationService: FlowDefinition node {Id} has empty flowAlias — skipped.", node.Id);
                return null;
            }

            // Aliases mirror Schema/Initializers/DataTypeInitializer.cs SelectExecutionMode.
            // FromAlias accepts unknown / null and falls back to Sequential.
            var execMode = FlowExecutionModeExtensions.FromAlias(node.Value<string>("executionMode"));

            var tracksJson   = node.Value<string>("tracksJson") ?? "[]";
            var outcomesJson = node.Value<string>("outcomesJson") ?? "[]";

            var tracks   = ParseTracks(tracksJson, alias);
            var outcomes = ParseOutcomes(outcomesJson, alias);

            return new FlowConfig(
                FlowAlias:        alias,
                FlowName:         node.Value<string>("flowName") ?? alias,
                FlowDescription:  node.Value<string>("flowDescription"),
                ExecutionMode:    execMode,
                TimeoutMs:        node.Value<int>("timeoutMs"),
                MaxRetries:       node.Value<int>("maxRetries"),
                IsActive:         node.Value<bool>("isActive"),
                WebhookTargetUrl: node.Value<string>("webhookTargetUrl") ?? string.Empty,
                SchemaVersion:    node.Value<int>("schemaVersion") is int sv && sv > 0 ? sv : 1,
                Tracks:           tracks,
                Outcomes:         outcomes
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FlowConfigurationService: failed to map FlowDefinition node {Id}.", node.Id);
            return null;
        }
    }

    private IReadOnlyList<TrackConfig> ParseTracks(string json, string flowAlias)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return [];

        try
        {
            var raw = JsonSerializer.Deserialize<List<TrackConfigRaw>>(json, _jsonOpts);
            if (raw is null) return [];

            return raw
                .Select(r => new TrackConfig(
                    TrackId:    r.TrackId ?? Guid.NewGuid().ToString("N")[..8],
                    Name:       r.Name ?? r.TrackId ?? "Unnamed",
                    Order:      r.Order,
                    StepType:   r.StepType ?? "validate",
                    Condition:  r.Condition ?? string.Empty,
                    StepConfig: r.StepConfig is not null
                        ? JsonSerializer.Serialize(r.StepConfig)
                        : "{}",
                    OnFailure:  r.OnFailure ?? "stop"))
                .OrderBy(t => t.Order)
                .ToList();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "FlowConfigurationService: invalid tracksJson for flow '{Alias}'.", flowAlias);
            return [];
        }
    }

    private IReadOnlyList<OutcomeConfig> ParseOutcomes(string json, string flowAlias)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return [];

        try
        {
            var raw = JsonSerializer.Deserialize<List<OutcomeConfigRaw>>(json, _jsonOpts);
            if (raw is null) return [];

            return raw
                .Select(r => new OutcomeConfig(
                    Code:           r.Code ?? "UNKNOWN",
                    Label:          r.Label ?? r.Code ?? "Unknown",
                    HttpStatus:     r.HttpStatus > 0 ? r.HttpStatus : 200,
                    TriggerWebhook: r.TriggerWebhook,
                    WebhookUrl:     r.WebhookUrl))
                .ToList();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "FlowConfigurationService: invalid outcomesJson for flow '{Alias}'.", flowAlias);
            return [];
        }
    }

    // ─── JSON deserialization helpers ─────────────────────────────────────────

    private sealed class TrackConfigRaw
    {
        public string? TrackId    { get; set; }
        public string? Name       { get; set; }
        public int     Order      { get; set; }
        public string? StepType   { get; set; }
        public string? Condition  { get; set; }
        public object? StepConfig { get; set; }
        public string? OnFailure  { get; set; }
    }

    private sealed class OutcomeConfigRaw
    {
        public string? Code           { get; set; }
        public string? Label          { get; set; }
        public int     HttpStatus     { get; set; }
        public bool    TriggerWebhook { get; set; }
        public string? WebhookUrl     { get; set; }
    }
}
