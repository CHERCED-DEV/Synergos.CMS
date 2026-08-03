using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.Api.Notifications.Domain;
using Synergos.Core;

namespace Synergos.Api.Notifications.Transport;

/// <summary>
/// El transporte de correo de verdad, sobre Resend.
/// </summary>
/// <remarks>
/// <para><b>Todo lo que este fichero hace es traducir.</b> De un canal, una dirección y dos
/// textos, a una petición HTTP; y del resultado de esa petición, a un <see cref="Rejection"/> de
/// los que ya existen. No decide a quién avisar, ni cuándo, ni con qué plantilla — eso ya está
/// decidido cuando lo llaman. Si algún día aparece una regla de negocio acá, está en el fichero
/// equivocado.</para>
///
/// <para><b>La clasificación transitorio ↔ definitivo es lo único delicado</b>, y no es
/// cosmética: <see cref="Rejection.IsTransient"/> es lo que decide en <c>Bff.Core</c> si algo se
/// reintenta o se grita una vez. Un 5xx clasificado como definitivo es un aviso que nunca sale;
/// un 4xx clasificado como transitorio es una tormenta de peticiones contra un proveedor que ya
/// dijo que no.</para>
/// </remarks>
public sealed class ResendNotificationSender : INotificationSender
{
    private readonly HttpClient _http;
    private readonly ResendOptions _options;
    private readonly ILogger<ResendNotificationSender> _log;

    public ResendNotificationSender(HttpClient http, IOptions<ResendOptions> options, ILogger<ResendNotificationSender> log)
    {
        _options = options.Value;
        _log = log;
        _http = http;
        _http.BaseAddress ??= new Uri(_options.BaseUrl);
        _http.Timeout = _options.Timeout;
    }

    /// <summary>Solo correo. SMS y WhatsApp salen por su propio adapter, detrás de esta misma costura.</summary>
    public bool Supports(Channel channel) => channel == Channel.Email;

    public async Task<Result<string>> SendAsync(
        Channel channel, string address, string subject, string body,
        string idempotencyKey, CancellationToken ct = default)
    {
        if (channel != Channel.Email) return Result.Rejected<string>(NotificationRules.ChannelUnsupported(channel));
        if (!_options.IsConfigured) return Result.Rejected<string>(NotificationRules.TransportNotConfigured(channel));

        // El cuerpo va como HTML porque es lo que un correo transaccional es en la práctica. La
        // plantilla la escribe el dominio (doc 07): acá no se decora ni se envuelve nada.
        var carga = JsonSerializer.Serialize(new
        {
            from = _options.From,
            to = new[] { address },
            subject,
            html = body,
        });

        using var peticion = new HttpRequestMessage(HttpMethod.Post, "/emails")
        {
            Content = new StringContent(carga, Encoding.UTF8, "application/json"),
        };
        peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        // La idempotencia del PROVEEDOR, que no es la nuestra. La nuestra evita el reintento que
        // vemos; ésta cubre el que no podemos ver — la petición que sí llegó y cuya respuesta se
        // perdió en el camino de vuelta.
        peticion.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await _http.SendAsync(peticion, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // Se agotó el tiempo, NO nos cancelaron. Transitorio: el correo pudo haber salido o
            // no, y por eso el reintento lleva la misma llave de idempotencia del proveedor.
            _log.LogWarning("Resend no respondió en {Timeout}.", _options.Timeout);
            return Result.Rejected<string>(NotificationRules.TransportUnavailable("se agotó el tiempo de espera"));
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "No se pudo hablar con Resend.");
            return Result.Rejected<string>(NotificationRules.TransportUnavailable(ex.Message));
        }

        using (respuesta)
        {
            var texto = await respuesta.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!respuesta.IsSuccessStatusCode)
            {
                // 429 y 5xx se reintentan; el resto no. Un 401 reintentado mil veces no arregla
                // una credencial mala — solo la convierte en una alerta de abuso.
                var transitorio = respuesta.StatusCode == HttpStatusCode.TooManyRequests
                    || (int)respuesta.StatusCode >= 500;

                var detalle = $"{(int)respuesta.StatusCode} {Recortar(texto)}";
                _log.LogWarning("Resend rechazó el envío: {Detalle}", detalle);

                return Result.Rejected<string>(transitorio
                    ? NotificationRules.TransportUnavailable(detalle)
                    : NotificationRules.TransportRejected(detalle));
            }

            var id = LeerId(texto);
            if (id is null)
            {
                // Aceptó pero no dijo con qué id. Sin id no hay correlación posible, y un envío
                // que se da por aceptado sin poder saber nunca si llegó es justo lo que esta HU
                // existe para dejar de hacer.
                _log.LogError("Resend aceptó el envío sin devolver un id. Respuesta: {Cuerpo}", Recortar(texto));
                return Result.Rejected<string>(
                    NotificationRules.TransportUnavailable("el proveedor aceptó sin devolver un identificador"));
            }

            return Result.Ok(id);
        }
    }

    private static string? LeerId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Lo que va al log se recorta: la respuesta de un proveedor puede traer el cuerpo entero.</summary>
    private static string Recortar(string s)
        => string.IsNullOrWhiteSpace(s) ? "(sin cuerpo)" : s.Length <= 300 ? s : s[..300] + "…";
}
