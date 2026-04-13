namespace Synergos.CMS.Domain.Orchestration;

// ─── Enums ────────────────────────────────────────────────────────────────────

public enum FlowExecutionMode
{
    Sequential,   // Tracks run one after the other
    Parallel,     // Tracks run simultaneously
    FirstMatch    // First track whose condition evaluates true is executed
}

public enum FlowStepType
{
    Validate,
    Enrich,
    Notify,
    Transform,
    Query
}

public enum FlowOnFailure
{
    Stop,
    Continue,
    Retry
}

// ─── Domain records (no Umbraco dependencies) ─────────────────────────────────

/// <summary>
/// Immutable snapshot of a FlowDefinition read from Umbraco.
/// Passed to FlowWebhookDispatcher and serialized as the webhook payload.
/// </summary>
public sealed record FlowConfig(
    string             FlowAlias,
    string             FlowName,
    string?            FlowDescription,
    FlowExecutionMode  ExecutionMode,
    int                TimeoutMs,
    int                MaxRetries,
    bool               IsActive,
    string             WebhookTargetUrl,
    int                SchemaVersion,
    IReadOnlyList<TrackConfig>   Tracks,
    IReadOnlyList<OutcomeConfig> Outcomes
);

/// <summary>
/// A single step within a flow. Deserialized from tracksJson in Umbraco.
/// </summary>
public sealed record TrackConfig(
    string  TrackId,
    string  Name,
    int     Order,
    string  StepType,
    /// <summary>
    /// Simple condition string: "field operator value" e.g. "amount<=10000", "priority==high", "" = always.
    /// </summary>
    string  Condition,
    /// <summary>Free-form step parameters serialized as JSON string.</summary>
    string  StepConfig,
    string  OnFailure
);

/// <summary>
/// Maps a flow outcome code to an HTTP status and optional chaining behavior.
/// Deserialized from outcomesJson in Umbraco.
/// </summary>
public sealed record OutcomeConfig(
    string  Code,
    string  Label,
    int     HttpStatus,
    bool    TriggerWebhook,
    string? WebhookUrl
);

/// <summary>Result of a FlowWebhookDispatcher.Publish() call.</summary>
public sealed record FlowPublicationResult(
    bool    Success,
    string  FlowAlias,
    string? Error = null,
    int?    EngineStatusCode = null
);
