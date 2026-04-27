using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Canal Slack-shaped para <see cref="IFormSubmissionNotifierChannel"/>.
/// POST a <see cref="FormsSettings.SlackWebhookUrl"/> con payload Block Kit.
/// </summary>
public sealed class SlackFormSubmissionNotifier : IFormSubmissionNotifierChannel
{
    private const string HttpClientName = "form-submission-slack";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<FormsSettings> _options;
    private readonly IBrandingProvider _branding;
    private readonly ILogger<SlackFormSubmissionNotifier> _logger;

    public SlackFormSubmissionNotifier(
        IHttpClientFactory httpClientFactory,
        IOptions<FormsSettings> options,
        IBrandingProvider branding,
        ILogger<SlackFormSubmissionNotifier> logger)
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
        if (string.IsNullOrWhiteSpace(settings.SlackWebhookUrl))
        {
            return;
        }
        if (!result.Success)
        {
            return;
        }

        var brand = _branding.GetCurrent();
        var siteName = string.IsNullOrWhiteSpace(brand.DisplayName) ? "Synergos" : brand.DisplayName;

        // Slack Block Kit: max 50 fields per section. Captureamos 8
        // primeros campos del form (suficiente para preview; el storage
        // tiene la submission completa).
        var fieldBlocks = request.Fields
            .Take(8)
            .Select(kv => new
            {
                type = "mrkdwn",
                text = $"*{kv.Key}:*\n{Truncate(kv.Value, 240)}",
            })
            .ToArray<object>();

        var payload = new
        {
            text = $"[{siteName}] Nueva submission del form: {request.FormKey}",
            blocks = new object[]
            {
                new
                {
                    type = "header",
                    text = new { type = "plain_text", text = $"📩 {request.FormKey} · {siteName}" },
                },
                new
                {
                    type = "context",
                    elements = new object[]
                    {
                        new { type = "mrkdwn", text = $"*IP:* `{request.ClientIp ?? "unknown"}` · *Origen:* {request.Referrer ?? "/"} · *Recibido:* {request.ReceivedAtUtc:yyyy-MM-dd HH:mm} UTC" },
                    },
                },
                new
                {
                    type = "section",
                    fields = fieldBlocks,
                },
            },
        };

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        await SlackWebhookSender.SendAsync(
            httpClient,
            settings.SlackWebhookUrl,
            payload,
            cancellationToken,
            _logger,
            $"form.submitted formKey={request.FormKey}");
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..(max - 3)] + "...";
}
