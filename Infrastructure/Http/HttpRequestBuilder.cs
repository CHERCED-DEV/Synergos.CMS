using System.Text;

namespace Synergos.CMS.Infrastructure.Http;

/// <summary>
/// Fluent builder for <see cref="HttpRequestMessage"/>. Inspired by the
/// RestSharpClient pattern in NS.Booking.CMS — chainable construction
/// gives a readable DSL plus easy unit-testing (build a request, assert
/// its headers/body without executing it).
///
/// Usage:
/// <code>
/// using var request = HttpRequestBuilder
///     .Post(targetUrl)
///     .WithJson(payload, jsonOptions)
///     .WithHeader("X-Synergos-Signature", signature)
///     .WithHeader("X-Synergos-FlowAlias", alias)
///     .WithHeader("X-Synergos-PublishedAt", DateTime.UtcNow.ToString("O"))
///     .Build();
///
/// var response = await client.SendAsync(request, ct);
/// </code>
///
/// Design notes:
/// - Build returns a fresh <see cref="HttpRequestMessage"/> each call, so
///   the same builder can be re-used safely.
/// - <see cref="WithJson"/> serializes once and stores the string; the
///   builder owns no <see cref="HttpClient"/> — the caller chooses the
///   named/typed client to send through.
/// </summary>
public sealed class HttpRequestBuilder
{
    private readonly HttpMethod                              _method;
    private readonly string                                  _url;
    private readonly Dictionary<string, string>              _headers   = new(StringComparer.OrdinalIgnoreCase);
    private string?                                          _body;
    private string                                           _contentType = "application/json";

    private HttpRequestBuilder(HttpMethod method, string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        _method = method;
        _url    = url;
    }

    // ── Verbs ────────────────────────────────────────────────────────────────

    public static HttpRequestBuilder Get(string url)    => new(HttpMethod.Get,    url);
    public static HttpRequestBuilder Post(string url)   => new(HttpMethod.Post,   url);
    public static HttpRequestBuilder Put(string url)    => new(HttpMethod.Put,    url);
    public static HttpRequestBuilder Patch(string url)  => new(HttpMethod.Patch,  url);
    public static HttpRequestBuilder Delete(string url) => new(HttpMethod.Delete, url);

    // ── Body ─────────────────────────────────────────────────────────────────

    /// <summary>Sets a pre-serialized JSON body.</summary>
    public HttpRequestBuilder WithJson(string serializedJson)
    {
        _body        = serializedJson;
        _contentType = "application/json";
        return this;
    }

    /// <summary>Serializes <paramref name="payload"/> and uses it as JSON body.</summary>
    public HttpRequestBuilder WithJson<T>(T payload, System.Text.Json.JsonSerializerOptions? options = null)
    {
        _body        = System.Text.Json.JsonSerializer.Serialize(payload, options);
        _contentType = "application/json";
        return this;
    }

    /// <summary>Sets a raw text body with a custom content type.</summary>
    public HttpRequestBuilder WithBody(string body, string contentType)
    {
        _body        = body;
        _contentType = contentType;
        return this;
    }

    // ── Headers ──────────────────────────────────────────────────────────────

    /// <summary>Adds (or replaces) a request header.</summary>
    public HttpRequestBuilder WithHeader(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _headers[name] = value;
        return this;
    }

    /// <summary>Adds a header only when the value is non-empty — convenient for optional headers.</summary>
    public HttpRequestBuilder WithHeaderIfPresent(string name, string? value)
    {
        if (!string.IsNullOrEmpty(value)) _headers[name] = value;
        return this;
    }

    // ── Build ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Materializes a fresh <see cref="HttpRequestMessage"/>. Caller is
    /// responsible for disposing it. Builder remains reusable.
    /// </summary>
    public HttpRequestMessage Build()
    {
        var request = new HttpRequestMessage(_method, _url);
        if (_body is not null)
            request.Content = new StringContent(_body, Encoding.UTF8, _contentType);
        foreach (var (name, value) in _headers)
            request.Headers.TryAddWithoutValidation(name, value);
        return request;
    }
}
