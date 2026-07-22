using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Path interno de Forms (ADR 0018 + ADR 0030). Procesa submissions
/// hechas por <c>elementFormContainer</c> blocks que tienen
/// <c>formInternalKey</c> seteado. Honeypot + rate limit + field
/// validation, luego delega a <see cref="IFormSubmissionHandler"/> y
/// hace PRG (POST → Redirect → GET) al referrer con
/// <c>?{SuccessQueryParam}=1</c> o <c>?{ErrorQueryParam}={code}</c>.
/// </summary>
/// <remarks>
/// Sin <c>[ValidateAntiForgeryToken]</c> — los forms son
/// editor-defined SSR plain HTML; agregar antiforgery requeriría
/// emitir tokens en cada renderer y hacer caching más doloroso.
/// La defensa primaria es honeypot + rate limit (ADR 0030 lo documenta).
/// </remarks>
[ApiController]
[Route("api/forms")]
public sealed class FormSubmissionsController : ControllerBase
{
    private static readonly Regex FormKeyRegex = new(
        @"^[a-z][a-z0-9-]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IFormSubmissionHandler _handler;
    private readonly InMemoryFormRateLimiter _rateLimiter;
    private readonly IOptions<FormsSettings> _options;
    private readonly ILogger<FormSubmissionsController> _logger;
    private readonly IAnalyticsTracker _analytics;
    private readonly IFormSubmissionNotifier _notifier;
    /// <summary>Qué campos declaró el autor. Sin esto el servidor no puede exigir los obligatorios.</summary>
    private readonly IFormDefinitionReader _definitions;

    public FormSubmissionsController(
        IFormSubmissionHandler handler,
        InMemoryFormRateLimiter rateLimiter,
        IOptions<FormsSettings> options,
        ILogger<FormSubmissionsController> logger,
        IAnalyticsTracker analytics,
        IFormSubmissionNotifier notifier,
        IFormDefinitionReader definitions)
    {
        _handler = handler;
        _rateLimiter = rateLimiter;
        _options = options;
        _logger = logger;
        _analytics = analytics;
        _notifier = notifier;
        _definitions = definitions;
    }

    [HttpPost("{formKey}/submit")]
    [AllowAnonymous]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Submit(string formKey, CancellationToken cancellationToken)
    {
        var settings = _options.Value;
        var referrer = Request.Headers.Referer.ToString();
        if (string.IsNullOrWhiteSpace(referrer))
        {
            referrer = "/";
        }

        if (!FormKeyRegex.IsMatch(formKey))
        {
            return BadRequest(new { error = "invalid-form-key" });
        }

        // Honeypot — si el campo hidden trae valor, asumir bot. Responde
        // success-fake para no leakear info al atacante.
        if (Request.Form.TryGetValue(settings.HoneypotFieldName, out var hp) &&
            !string.IsNullOrWhiteSpace(hp.ToString()))
        {
            _logger.LogInformation(
                "Form honeypot triggered: formKey={FormKey} ip={Ip}",
                formKey,
                HttpContext.Connection.RemoteIpAddress?.ToString());
            _analytics.Track("form.honeypot-triggered", new Dictionary<string, object?>
            {
                ["formKey"] = formKey,
            });
            return RedirectWithQuery(referrer, settings.SuccessQueryParam, "1");
        }

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!_rateLimiter.TryRegister(clientIp, formKey))
        {
            _analytics.Track("form.rate-limited", new Dictionary<string, object?>
            {
                ["formKey"] = formKey,
            });
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { error = "rate-limit-exceeded" });
        }

        var fields = ExtractFields(settings);
        if (fields.Count == 0)
        {
            return RedirectWithQuery(referrer, settings.ErrorQueryParam, "no-fields");
        }
        if (fields.Count > settings.MaxFieldsPerSubmission)
        {
            return RedirectWithQuery(referrer, settings.ErrorQueryParam, "too-many-fields");
        }

        // Los campos OBLIGATORIOS se exigen aquí, contra la definición publicada. Antes no lo
        // hacía nadie: el navegador tenía `novalidate`, no hay JS de validación, y este
        // endpoint solo miraba abuso (honeypot/rate-limit/nº de campos). Un formulario entero
        // vacío se persistía y disparaba la notificación por email.
        //
        // La lista sale del CONTENIDO, no del POST: una lista enviada por el cliente la
        // controla quien envía, y este backstop existe justo para el POST que se salta el
        // navegador. Si la definición no se encuentra (formKey huérfano tras despublicar) NO se
        // rechaza: se registra y se deja pasar — el envío ya pasó honeypot y rate-limit, y
        // tirarlo perdería datos de un usuario real por un problema de contenido.
        var definition = _definitions.GetByKey(formKey);
        if (definition is null)
        {
            _logger.LogWarning(
                "Form definition not found for formKey={FormKey}; required-field check skipped.",
                formKey);
        }
        else
        {
            var missing = definition.Fields
                .Where(f => f.Required)
                .Where(f => !fields.TryGetValue(f.Name, out var v) || string.IsNullOrWhiteSpace(v))
                .Select(f => f.Name)
                .ToArray();

            if (missing.Length > 0)
            {
                _analytics.Track("form.missing-required", new Dictionary<string, object?>
                {
                    ["formKey"] = formKey,
                    ["missingCount"] = missing.Length,
                });
                return RedirectWithQuery(referrer, settings.ErrorQueryParam, "missing-required");
            }
        }

        var request = new FormSubmissionRequest(
            FormKey: formKey,
            Fields: fields,
            ClientIp: clientIp,
            UserAgent: Request.Headers.UserAgent.ToString(),
            Referrer: referrer,
            ReceivedAtUtc: DateTime.UtcNow);

        var result = await _handler.SubmitAsync(request, cancellationToken);
        if (!result.Success)
        {
            _analytics.Track("form.submit-failed", new Dictionary<string, object?>
            {
                ["formKey"] = formKey,
                ["errorCode"] = result.ErrorCode,
            });
            return RedirectWithQuery(referrer, settings.ErrorQueryParam,
                result.ErrorCode ?? "unknown");
        }

        _analytics.Track("form.submitted", new Dictionary<string, object?>
        {
            ["formKey"] = formKey,
            ["fieldCount"] = fields.Count,
        });

        // Olas 80.3 + 91 — notificación via IFormSubmissionNotifier
        // composite. Cada canal (email/webhook) decide si actúa según
        // sus propios settings. Try-catch interno por canal — fallos
        // NO rompen la persistencia que ya ocurrió.
        await _notifier.NotifySubmittedAsync(request, result, cancellationToken);

        return RedirectWithQuery(referrer, settings.SuccessQueryParam, "1");
    }

    private Dictionary<string, string> ExtractFields(FormsSettings settings)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in Request.Form)
        {
            if (string.Equals(name, settings.HoneypotFieldName, StringComparison.Ordinal))
            {
                continue;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var raw = values.ToString();
            var trimmed = raw.Length > settings.MaxFieldLengthChars
                ? raw[..settings.MaxFieldLengthChars]
                : raw;
            fields[name] = trimmed.Trim();
        }
        return fields;
    }

    private IActionResult RedirectWithQuery(string referrer, string queryName, string queryValue)
    {
        var separator = referrer.Contains('?') ? '&' : '?';
        var url = $"{referrer}{separator}{Uri.EscapeDataString(queryName)}={Uri.EscapeDataString(queryValue)}";
        return Redirect(url);
    }
}
