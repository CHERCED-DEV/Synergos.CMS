using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// <see cref="IValidateOptions{TOptions}"/> que warn al startup si
/// alguna key en <see cref="WebhookResilienceSettings.PerChannel"/>
/// no matchea ninguno de los 12 FactoryNames conocidos. Ola 161.
/// </summary>
/// <remarks>
/// Devuelve <c>ValidateOptionsResult.Success</c> incluso si hay
/// warnings — una key wrong no es fatal (silently no aplica el override).
/// El warning se loguea via constructor injection del logger para que
/// el operador lo vea en startup logs.
///
/// Validate se invoca en cada `Get` del IOptions chain en startup, y
/// también cada vez que IOptionsMonitor.OnChange dispara — propagando
/// detection de typos al hot-reload.
/// </remarks>
public sealed class WebhookResilienceSettingsValidator : IValidateOptions<WebhookResilienceSettings>
{
    private static readonly IReadOnlySet<string> KnownFactoryNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "comment-moderation-webhook",
        "comment-moderation-slack",
        "comment-moderation-discord",
        "comment-moderation-teams",
        "form-submission-webhook",
        "form-submission-slack",
        "form-submission-discord",
        "form-submission-teams",
        "cart-abandonment-webhook",
        "cart-abandonment-slack",
        "cart-abandonment-discord",
        "cart-abandonment-teams",
    };

    private readonly ILogger<WebhookResilienceSettingsValidator> _logger;

    public WebhookResilienceSettingsValidator(
        ILogger<WebhookResilienceSettingsValidator> logger) =>
        _logger = logger;

    public ValidateOptionsResult Validate(string? name, WebhookResilienceSettings options)
    {
        if (options.PerChannel.Count == 0)
        {
            return ValidateOptionsResult.Success;
        }

        var unknown = new List<string>();
        foreach (var key in options.PerChannel.Keys)
        {
            if (!KnownFactoryNames.Contains(key))
            {
                _logger.LogWarning(
                    "Synergos:Admin:WebhookResilience:PerChannel key '{Key}' does not match any known notifier FactoryName. Override will silently not apply. Valid keys: {Valid}",
                    key,
                    string.Join(", ", KnownFactoryNames));
                unknown.Add(key);
            }
        }

        // Ola 194 — strict mode falla boot si typo detected.
        if (unknown.Count > 0 && options.StrictValidation)
        {
            return ValidateOptionsResult.Fail(
                $"WebhookResilience.StrictValidation=true and {unknown.Count} unknown PerChannel keys detected: {string.Join(", ", unknown)}");
        }

        return ValidateOptionsResult.Success;
    }
}
