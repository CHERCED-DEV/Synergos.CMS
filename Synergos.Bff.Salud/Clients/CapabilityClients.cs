using System.Net;
using Synergos.Core;

namespace Synergos.Bff.Salud.Clients;

/// <summary>
/// Lo que un cliente de capacidad devuelve: el resultado o el rechazo que vino del otro lado.
/// </summary>
/// <remarks>
/// <b>El rechazo de la capacidad se PRESERVA</b> en vez de aplanarse a "falló". Es lo que
/// permite que el orquestador decida distinto ante <c>booking.at_capacity</c> (avisar y ofrecer
/// otra hora) que ante <c>payments.declined</c> (pedir otro medio de pago) — y sobre todo, que
/// distinga un <c>Unavailable</c> —reintentable— de un <c>Conflict</c>, que no lo es.
/// </remarks>
public static class CapabilityHttp
{
    /// <summary>Cabecera de idempotencia, la misma que exigen las veinte capacidades.</summary>
    public const string IdempotencyHeader = "Idempotency-Key";

    /// <summary>Hace la llamada y traduce lo que venga a un <see cref="Result{T}"/>.</summary>
    public static async Task<Result<T>> SendAsync<T>(
        HttpClient http, HttpMethod method, string path, object? body, IdempotencyKey? key,
        string capability, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path);
        if (body is not null) req.Content = JsonContent.Create(body);
        if (key is { } k) req.Headers.TryAddWithoutValidation(IdempotencyHeader, k.Value);

        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(req, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Unavailable y no un error genérico: es la ÚNICA clase de rechazo que el
            // orquestador puede reintentar, y confundirla con las demás convierte una caída
            // pasajera en una compensación innecesaria — que en Payments es plata que va y
            // vuelve sin motivo.
            return Rejection.Unavailable($"{capability}.unreachable", $"{capability} no respondió: {ex.Message}");
        }

        if (res.IsSuccessStatusCode)
        {
            var valor = await res.Content.ReadFromJsonAsync<T>(ct);
            return valor is null
                ? Rejection.Unavailable($"{capability}.empty_response", $"{capability} respondió {(int)res.StatusCode} sin cuerpo.")
                : Result.Ok(valor);
        }

        return await LeerRechazoAsync(res, capability, ct);
    }

    /// <summary>Reconstruye el <see cref="Rejection"/> desde el ProblemDetails de la capacidad.</summary>
    private static async Task<Rejection> LeerRechazoAsync(HttpResponseMessage res, string capability, CancellationToken ct)
    {
        var kind = res.StatusCode switch
        {
            HttpStatusCode.BadRequest => RejectionKind.Invalid,
            HttpStatusCode.NotFound => RejectionKind.NotFound,
            HttpStatusCode.Conflict => RejectionKind.Conflict,
            HttpStatusCode.Forbidden => RejectionKind.Forbidden,
            HttpStatusCode.Gone => RejectionKind.Expired,
            _ => RejectionKind.Unavailable,
        };

        try
        {
            var problema = await res.Content.ReadFromJsonAsync<ProblemaDto>(ct);
            // El `code` de la capacidad viaja tal cual: es lo que el orquestador compara, y
            // reemplazarlo por uno propio perdería la causa concreta.
            return new Rejection(kind, problema?.Code ?? $"{capability}.unknown",
                problema?.Detail ?? $"{capability} respondió {(int)res.StatusCode}.");
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            return new Rejection(kind, $"{capability}.unparseable",
                $"{capability} respondió {(int)res.StatusCode} con un cuerpo que no se pudo leer.");
        }
    }

    private sealed record ProblemaDto(string? Code, string? Detail);
}

// ── Las formas mínimas que este BFF consume de cada capacidad ────────────────
// Solo los campos que usa. Un DTO que copie la respuesta entera obligaría a tocar el
// orquestador cada vez que una capacidad agrega un campo que no le importa.

public sealed record ConsentDto(string Id, bool Active);
public sealed record HoldDto(string Id, string ResourceId, DateTimeOffset ExpiresAt);
public sealed record ReservationDto(string Id, string Status);
public sealed record MoneyDto(decimal Amount, string Currency);
public sealed record QuoteDto(MoneyDto Total);
public sealed record PaymentDto(string Id, string Status, MoneyDto Amount, MoneyDto Refundable);
public sealed record ResourceDto(string Id);
public sealed record DeliveryDto(string Id, string Status);
