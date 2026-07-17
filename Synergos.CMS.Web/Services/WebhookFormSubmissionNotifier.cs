using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Canal HTTP webhook para <see cref="IFormSubmissionNotifierChannel"/>.
/// POST a <see cref="FormsSettings.WebhookUrl"/> con payload JSON
/// de la submission. Compatible con Slack/Discord/Teams incoming
/// webhooks o cualquier endpoint custom HTTP.
/// </summary>
/// <remarks>
/// Si <c>WebhookUrl</c> está vacío, el canal es no-op. Errores HTTP
/// loguean Warning pero NO se re-throwean (handle por el composite).
///
/// Payload shape — flat JSON estable: <c>event</c>/<c>siteName</c>/
/// <c>formKey</c>/<c>fields</c>/<c>clientIp</c>/<c>referrer</c>/
/// <c>receivedAtUtc</c>/<c>storageReference</c>.
/// </remarks>
public sealed class WebhookFormSubmissionNotifier : IFormSubmissionNotifierChannel
{
    private const string HttpClientName = "form-submission-webhook";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<FormsSettings> _options;
    private readonly IBrandingProvider _branding;
    private readonly ILogger<WebhookFormSubmissionNotifier> _logger;

    public WebhookFormSubmissionNotifier(
        IHttpClientFactory httpClientFactory,
        IOptions<FormsSettings> options,
        IBrandingProvider branding,
        ILogger<WebhookFormSubmissionNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _branding = branding;
        _logger = logger;
    }

    public static string FactoryName => HttpClientName;

    public async Task NotifySubmittedAsync(
        FormSubmissionRequest request,
        FormSubmissionResult result,
        CancellationToken cancellationToken)
    {
        var settings = _options.Value;
        if (string.IsNullOrWhiteSpace(settings.WebhookUrl))
        {
            return;
        }
        if (!result.Success)
        {
            return;
        }

        var brand = _branding.GetCurrent();
        var siteName = string.IsNullOrWhiteSpace(brand.DisplayName) ? "Synergos" : brand.DisplayName;

        var payload = new
        {
            @event = "form.submitted",
            siteName,
            formKey = request.FormKey,
            fields = request.Fields,
            clientIp = request.ClientIp,
            referrer = request.Referrer,
            receivedAtUtc = request.ReceivedAtUtc.ToString("O"),
            storageReference = result.StorageReference,
        };

        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var content = new ByteArrayContent(bodyBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, settings.WebhookUrl)
        {
            Content = content,
        };

        if (!string.IsNullOrWhiteSpace(settings.WebhookBearerToken))
        {
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.WebhookBearerToken);
        }

        var signed = WebhookSigner.ComputeSignedHeaders(settings.WebhookHmacSecret, bodyBytes);
        if (signed is { } s)
        {
            requestMessage.Headers.TryAddWithoutValidation(WebhookSigner.TimestampHeaderName, s.Timestamp);
            requestMessage.Headers.TryAddWithoutValidation(WebhookSigner.SignatureHeaderName, s.Signature);
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var response = await httpClient.SendAsync(requestMessage, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Form webhook returned non-success: status={Status} url={Url} formKey={FormKey}",
                (int)response.StatusCode, settings.WebhookUrl, request.FormKey);
        }
    }
}
