using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Synergos.CMS.Configuration;
using Synergos.CMS.Domain.Orchestration;

namespace Synergos.CMS.Infrastructure.Http;

/// <summary>
/// HTTP-backed <see cref="IFlowWebhookDispatcher"/> implementation.
///
/// Serializes a <c>FlowConfig</c>, signs it with HMAC-SHA256, and POSTs it to the
/// Flow Engine endpoint via the named <c>HttpClient</c> "FlowEngine" registered in
/// <c>Program.cs</c>. Transient — stateless, no shared mutable state.
///
/// Signature header: <c>X-Synergos-Signature: sha256=&lt;hex&gt;</c>.
/// Secret source: appsettings <c>Synergos:FlowEngine:WebhookSecret</c>.
/// </summary>
public sealed class HttpFlowWebhookDispatcher : IFlowWebhookDispatcher
{
    private readonly IHttpClientFactory              _httpClientFactory;
    private readonly IOptions<FlowEngineSettings>   _settings;
    private readonly ILogger<HttpFlowWebhookDispatcher> _logger;

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public HttpFlowWebhookDispatcher(
        IHttpClientFactory              httpClientFactory,
        IOptions<FlowEngineSettings>   settings,
        ILogger<HttpFlowWebhookDispatcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings          = settings;
        _logger            = logger;
    }

    // ─── Publish ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Serializes <paramref name="config"/> and POSTs it to <paramref name="config.WebhookTargetUrl"/>.
    /// Returns a result indicating success or failure with the engine HTTP status.
    /// Does not throw — all exceptions are caught and reported in the result.
    /// </summary>
    public async Task<FlowPublicationResult> PublishAsync(
        FlowConfig        config,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(config.WebhookTargetUrl))
        {
            _logger.LogWarning(
                "HttpFlowWebhookDispatcher: flow '{Alias}' has no webhookTargetUrl configured — skipping.",
                config.FlowAlias);
            return new FlowPublicationResult(false, config.FlowAlias,
                Error: "webhookTargetUrl is not configured on this FlowDefinition.");
        }

        try
        {
            var payload = JsonSerializer.Serialize(config, _serializerOptions);
            var secret  = _settings.Value.WebhookSecret;
            var signature = ComputeHmac(payload, secret);

            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var client = _httpClientFactory.CreateClient("FlowEngine");
            client.Timeout = TimeSpan.FromMilliseconds(_settings.Value.WebhookTimeoutMs);

            using var request = new HttpRequestMessage(HttpMethod.Post, config.WebhookTargetUrl)
            {
                Content = content
            };
            request.Headers.Add("X-Synergos-Signature",  $"sha256={signature}");
            request.Headers.Add("X-Synergos-FlowAlias",  config.FlowAlias);
            request.Headers.Add("X-Synergos-PublishedAt", DateTime.UtcNow.ToString("O"));

            using var response = await client.SendAsync(request, ct);
            var statusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "HttpFlowWebhookDispatcher: flow '{Alias}' published → {Status}.",
                    config.FlowAlias, statusCode);
                return new FlowPublicationResult(true, config.FlowAlias, EngineStatusCode: statusCode);
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "HttpFlowWebhookDispatcher: engine returned {Status} for flow '{Alias}'. Body: {Body}",
                statusCode, config.FlowAlias, body);

            return new FlowPublicationResult(false, config.FlowAlias,
                Error:            $"Engine returned HTTP {statusCode}: {TruncateError(body)}",
                EngineStatusCode: statusCode);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning(
                "HttpFlowWebhookDispatcher: timeout publishing flow '{Alias}' to {Url}.",
                config.FlowAlias, config.WebhookTargetUrl);
            return new FlowPublicationResult(false, config.FlowAlias,
                Error: $"Request timed out after {_settings.Value.WebhookTimeoutMs} ms.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "HttpFlowWebhookDispatcher: HTTP error publishing flow '{Alias}' to {Url}.",
                config.FlowAlias, config.WebhookTargetUrl);
            return new FlowPublicationResult(false, config.FlowAlias,
                Error: $"HTTP request failed: {ex.Message}. Is Synergos.API running?");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "HttpFlowWebhookDispatcher: unexpected error publishing flow '{Alias}'.",
                config.FlowAlias);
            return new FlowPublicationResult(false, config.FlowAlias,
                Error: $"Unexpected error: {ex.Message}");
        }
    }

    // ─── Trigger (relay to engine execute endpoint) ───────────────────────────

    /// <summary>
    /// Relays a flow execution request to Synergos.API.
    /// The execute endpoint is derived from the webhookTargetUrl by replacing
    /// "/register" with "/{alias}/execute", or by using the executeUrl directly.
    /// </summary>
    public async Task<(bool Success, string? Body, int? StatusCode)> TriggerAsync(
        FlowConfig                         config,
        string                             caseId,
        Dictionary<string, object?>        input,
        CancellationToken                  ct = default)
    {
        // Derive execute URL from register URL:
        // http://localhost:5002/api/engine/flows/register → http://localhost:5002/api/engine/flows/{alias}/execute
        var registerUrl = config.WebhookTargetUrl.TrimEnd('/');
        string executeUrl;

        if (registerUrl.EndsWith("/register", StringComparison.OrdinalIgnoreCase))
        {
            var baseUrl = registerUrl[..^"/register".Length];
            executeUrl = $"{baseUrl}/{config.FlowAlias}/execute";
        }
        else
        {
            // Fallback: append /{alias}/execute to the configured base
            executeUrl = $"{registerUrl}/{config.FlowAlias}/execute";
        }

        try
        {
            var payload = JsonSerializer.Serialize(
                new { caseId, input },
                _serializerOptions);

            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var client  = _httpClientFactory.CreateClient("FlowEngine");
            client.Timeout = TimeSpan.FromMilliseconds(_settings.Value.WebhookTimeoutMs);

            using var response = await client.PostAsync(executeUrl, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            return (response.IsSuccessStatusCode, body, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "HttpFlowWebhookDispatcher: error triggering flow '{Alias}' execution.",
                config.FlowAlias);
            return (false, ex.Message, null);
        }
    }

    // ─── HMAC ─────────────────────────────────────────────────────────────────

    private static string ComputeHmac(string payload, string secret)
    {
        var key  = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(key, data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string TruncateError(string body)
        => body.Length > 300 ? body[..300] + "…" : body;
}
