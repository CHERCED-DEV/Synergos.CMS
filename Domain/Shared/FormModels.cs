namespace Synergos.CMS.Domain.Shared;

/// <summary>
/// Domain model for a rendered form definition.
/// Framework-agnostic — no Umbraco dependencies.
/// </summary>
public sealed record FormDefinitionModel(
    string FormName,
    string FormAlias,
    string SubmitLabel,
    string? RecipientEmail,
    string? WebhookUrl,
    string? ThankYouMessage,
    string? ThankYouRedirectUrl,
    string? IntegrationProvider,
    string? IntegrationId,
    IReadOnlyList<FormFieldModel> Fields);

/// <summary>A single form field ready for rendering.</summary>
public sealed record FormFieldModel(
    string Label,
    string Name,
    string Type,
    string? Placeholder,
    bool Required,
    string? Options,
    string? ValidationPattern,
    string Width);

/// <summary>Incoming form submission payload.</summary>
public sealed record FormSubmissionPayload(
    string FormAlias,
    Dictionary<string, string> Fields);

/// <summary>Result of processing a form submission.</summary>
public sealed record FormSubmissionResult(
    bool Success,
    string? ThankYouMessage,
    string? RedirectUrl,
    string? ErrorMessage);
