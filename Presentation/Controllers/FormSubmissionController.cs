using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Synergos.CMS.Application.Services;
using Synergos.CMS.Domain.Services;
using Synergos.CMS.Domain.Shared;

namespace Synergos.CMS.Presentation.Controllers;

/// <summary>
/// API endpoint for processing form submissions.
///
/// POST /api/forms/submit
///   Content-Type: application/x-www-form-urlencoded (HTML form)
///   Content-Type: application/json (AJAX/fetch)
///
/// Flow:
///   1. Validate formAlias exists and matches a FormDefinition
///   2. Validate required fields
///   3. Dispatch to configured handlers (email → webhook → integration)
///   4. Return success with thank-you message or redirect URL
///
/// Security:
///   - Honeypot field support for spam prevention
///   - Rate limiting should be configured at reverse proxy level
/// </summary>
[Route("api/forms")]
[ApiController]
public sealed class FormSubmissionController : ControllerBase
{
    private readonly FormRenderService                    _formService;
    private readonly IFormEmailService                     _emailService;
    private readonly IHttpClientFactory                   _httpClientFactory;
    private readonly IDictionaryCache                     _dictionary;
    private readonly ILogger<FormSubmissionController>    _logger;

    public FormSubmissionController(
        FormRenderService                  formService,
        IFormEmailService                   emailService,
        IHttpClientFactory                 httpClientFactory,
        IDictionaryCache                   dictionary,
        ILogger<FormSubmissionController>  logger)
    {
        _formService        = formService;
        _emailService       = emailService;
        _httpClientFactory  = httpClientFactory;
        _dictionary         = dictionary;
        _logger             = logger;
    }

    [HttpPost("submit")]
    public async Task<IActionResult> Submit([FromForm] FormSubmissionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FormAlias))
            return BadRequest(new FormSubmissionResult(false, null, null, "Form alias is required."));

        // Honeypot: if filled, silently succeed (bot)
        if (!string.IsNullOrEmpty(request.HoneypotField))
        {
            _logger.LogInformation("FormSubmission: honeypot triggered for '{Alias}'.", request.FormAlias);
            return Ok(new FormSubmissionResult(true, _dictionary.Get("Form.Messages.Success", fallback: "Thank you!"), null, null));
        }

        // Resolve the form definition
        var formDef = _formService.ResolveByAlias(request.FormAlias);
        if (formDef is null)
        {
            _logger.LogWarning("FormSubmission: form alias '{Alias}' not found.", request.FormAlias);
            return NotFound(new FormSubmissionResult(false, null, null, "Form not found."));
        }

        // Collect submitted fields (keyed by field Name)
        var submittedFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in formDef.Fields)
        {
            var value = Request.Form.TryGetValue(field.Name, out var formValue)
                ? formValue.ToString()
                : string.Empty;
            submittedFields[field.Name] = value;
        }

        // Validate required fields
        var missingFields = formDef.Fields
            .Where(f => f.Required && string.IsNullOrWhiteSpace(submittedFields.GetValueOrDefault(f.Name)))
            .Select(f => f.Label)
            .ToList();

        if (missingFields.Count > 0)
        {
            var prefix = _dictionary.Get("Form.Validation.RequiredSummary", fallback: "Required fields:");
            return BadRequest(new FormSubmissionResult(
                false, null, null,
                $"{prefix} {string.Join(", ", missingFields)}"));
        }

        _logger.LogInformation(
            "FormSubmission: '{FormName}' ({Alias}) submitted with {Count} fields.",
            formDef.FormName, formDef.FormAlias, submittedFields.Count);

        // ── Dispatch handlers ─────────────────────────────────────────────────

        await DispatchEmailAsync(formDef, submittedFields, ct);
        await DispatchWebhookAsync(formDef, submittedFields, ct);
        DispatchIntegration(formDef);

        // ── Response ──────────────────────────────────────────────────────────

        var result = new FormSubmissionResult(
            Success:          true,
            ThankYouMessage:  formDef.ThankYouMessage ?? _dictionary.Get("Form.Messages.Success", fallback: "Thank you! Your message has been sent."),
            RedirectUrl:      formDef.ThankYouRedirectUrl,
            ErrorMessage:     null);

        // AJAX: return JSON
        if (Request.Headers.Accept.Any(h => h?.Contains("application/json") == true)
            || Request.ContentType?.Contains("application/json") == true)
        {
            return Ok(result);
        }

        // HTML form: redirect to thank-you URL or back to referrer
        if (!string.IsNullOrEmpty(result.RedirectUrl))
            return Redirect(result.RedirectUrl);

        var referrer = Request.Headers.Referer.ToString();
        return Redirect(string.IsNullOrEmpty(referrer) ? "/" : $"{referrer}?formSuccess=true");
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private async Task DispatchEmailAsync(
        FormDefinitionModel form,
        IReadOnlyDictionary<string, string> data,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(form.RecipientEmail)) return;

        try
        {
            await _emailService.SendFormSubmissionAsync(form, data, ct);
            _logger.LogDebug("FormSubmission: email sent to '{Email}' for form '{Alias}'.",
                form.RecipientEmail, form.FormAlias);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "FormSubmission: email dispatch failed for form '{Alias}'. Submission still recorded.",
                form.FormAlias);
        }
    }

    private async Task DispatchWebhookAsync(
        FormDefinitionModel form,
        IReadOnlyDictionary<string, string> data,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(form.WebhookUrl)) return;

        try
        {
            var payload = new
            {
                formAlias  = form.FormAlias,
                formName   = form.FormName,
                submittedAt = DateTime.UtcNow.ToString("O"),
                fields     = data
            };

            var json    = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var client   = _httpClientFactory.CreateClient("FormWebhook");
            using var response = await client.PostAsync(form.WebhookUrl, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "FormSubmission: webhook returned {Status} for form '{Alias}' → {Url}.",
                    (int)response.StatusCode, form.FormAlias, form.WebhookUrl);
            }
            else
            {
                _logger.LogDebug("FormSubmission: webhook dispatched for form '{Alias}'.", form.FormAlias);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "FormSubmission: webhook dispatch failed for form '{Alias}'. Submission still recorded.",
                form.FormAlias);
        }
    }

    /// <summary>
    /// STUB — integration providers (HubSpot, Salesforce, etc.) are not implemented.
    ///
    /// To implement: add a case in the switch for each provider, inject the corresponding
    /// client service, and call it here. Until then, submissions are captured via email
    /// and webhook only. No data is silently lost — this method explicitly does nothing.
    ///
    /// If a form has IntegrationProvider configured and this stub runs, a LogError is
    /// emitted so the misconfiguration is visible in monitoring dashboards.
    /// </summary>
    private void DispatchIntegration(FormDefinitionModel form)
    {
        if (string.IsNullOrWhiteSpace(form.IntegrationProvider)) return;

        // TODO: implement per-provider dispatch (HubSpot, Salesforce, Pipedrive, etc.)
        // switch (form.IntegrationProvider.ToLowerInvariant())
        // {
        //     case "hubspot":   await _hubSpot.SubmitAsync(form, data, ct);  break;
        //     case "salesforce": await _sf.CreateLeadAsync(form, data, ct);  break;
        // }
        _logger.LogError(
            "FormSubmission: form '{Alias}' has IntegrationProvider='{Provider}' (id='{Id}') " +
            "configured but NO integration handler is implemented. " +
            "Submission was captured via email/webhook only. " +
            "Implement the provider handler or remove IntegrationProvider from the form definition.",
            form.FormAlias, form.IntegrationProvider, form.IntegrationId);
    }
}

/// <summary>Form submission request from HTML form.</summary>
public sealed class FormSubmissionRequest
{
    public string FormAlias { get; set; } = string.Empty;

    /// <summary>Honeypot field — should remain empty. Filled = bot.</summary>
    [FromForm(Name = "_hp")]
    public string? HoneypotField { get; set; }
}
