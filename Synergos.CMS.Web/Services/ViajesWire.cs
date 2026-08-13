using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Synergos.CMS.Web.Services;

/// <summary>Un rechazo del orquestador de Viajes que trae su código, para poder distinguirlo.</summary>
internal sealed class ViajesRejectedException : Exception
{
    public ViajesRejectedException(string? code, string message) : base(message) => Code = code;

    /// <summary>El código del árbol de servicios — <c>booking.hold_expired</c>, <c>viajes.bad_refund</c>.</summary>
    public string? Code { get; }
}

/// <summary>
/// El cable hacia <c>Synergos.Bff.Viajes</c>: manda, traduce el rechazo y <b>no se traga nada</b>.
/// </summary>
/// <remarks>
/// <para><b>Existe porque hay DOS consumidores</b> (HU #40): la vía hotel y el carrito
/// multi-producto. Con uno solo esto era código privado de <see cref="HttpHotelBookingService"/>
/// y estaba bien ahí; copiarlo para el segundo garantizaría que las dos copias divergieran —y la
/// que divergiera sería la del manejo de errores, que es donde menos se nota y más cuesta.</para>
///
/// <para><b>Lo que este cable decide, y que no es obvio:</b> un fallo de transporte NO se
/// convierte en «no se pudo, intentá de nuevo». Se convierte en una excepción que dice que no
/// sabemos qué pasó, porque un timeout no significa «no se cobró». Y un 401 se distingue de un
/// rechazo de negocio: es un defecto de despliegue —la llave compartida— y quien reserva no
/// puede hacer nada con ese detalle, así que se registra completo y se contesta genérico.</para>
/// </remarks>
internal sealed class ViajesWire
{
    /// <summary>Cabecera de la llave compartida. La misma que exige toda capacidad.</summary>
    public const string ApiKeyHeader = "X-Synergos-Key";

    /// <summary>Cliente nombrado que registra el composer. <b>Uno solo para los dos consumidores</b>:
    /// misma URL, misma llave, mismo timeout — dos registros idénticos serían dos sitios donde
    /// cambiar el timeout y uno donde olvidarlo.</summary>
    public const string ClientName = "synergos-bff-viajes";

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _clients;
    private readonly ILogger _log;
    private readonly string _mensajeDeCaida;

    /// <param name="clients">La fábrica de clientes nombrados.</param>
    /// <param name="log">Dónde queda lo que no se le puede contar a quien reserva.</param>
    /// <param name="mensajeDeCaida">
    /// Qué se le dice a quien reserva cuando el orquestador no contesta. Lo pone cada consumidor
    /// porque es texto de cara al usuario y depende de qué estaba haciendo.
    /// </param>
    public ViajesWire(IHttpClientFactory clients, ILogger log, string mensajeDeCaida)
    {
        _clients = clients;
        _log = log;
        _mensajeDeCaida = mensajeDeCaida;
    }

    /// <summary>Manda y devuelve el cuerpo, o lanza con el motivo puesto.</summary>
    /// <exception cref="ViajesRejectedException">El orquestador rechazó, con su código.</exception>
    /// <exception cref="InvalidOperationException">No contestó, o contestó algo inservible.</exception>
    public async Task<T> SendAsync<T>(HttpRequestMessage req, string queHacia, CancellationToken ct)
    {
        var http = _clients.CreateClient(ClientName);

        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // quien reserva cerró la pestaña; no es un fallo del servicio
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // NO se traga la excepción. Un fallo de red que devolviera «reserva confirmada» es
            // el peor defecto posible de este camino.
            _log.LogError(ex, "No se pudo {Que}: el orquestador de Viajes no respondió.", queHacia);
            throw new InvalidOperationException(_mensajeDeCaida, ex);
        }

        using (res)
        {
            if (res.IsSuccessStatusCode)
            {
                var cuerpo = await res.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false);
                return cuerpo ?? throw new InvalidOperationException($"No pudimos {queHacia}: la respuesta vino vacía.");
            }

            var problema = await LeerProblemaAsync(res, ct).ConfigureAwait(false);

            // SOLO 401. Es un defecto de DESPLIEGUE, no de quien reserva: la llave compartida está
            // mal o no está. El 403 NO entra acá — `SharedKeyAuth` responde 401 cuando la llave
            // falla, nunca 403, y un 403 del árbol de servicios es un rechazo de negocio cuyo
            // motivo sí le sirve a quien reserva.
            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                _log.LogError(
                    "Viajes respondió 401 al {Que}: la llave compartida es inválida o falta. "
                    + "Revisar Synergos:Viajes:ApiKey.", queHacia);
                throw new InvalidOperationException(_mensajeDeCaida);
            }

            _log.LogWarning("Viajes rechazó {Que} con {Status} ({Code}): {Detalle}",
                queHacia, (int)res.StatusCode, problema.Code ?? "-", problema.Detail ?? "-");

            var mensaje = string.IsNullOrWhiteSpace(problema.Detail) ? $"No pudimos {queHacia}." : problema.Detail!;
            throw new ViajesRejectedException(problema.Code, mensaje);
        }
    }

    private static async Task<ProblemDto> LeerProblemaAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try
        {
            return await res.Content.ReadFromJsonAsync<ProblemDto>(Json, ct).ConfigureAwait(false) ?? new ProblemDto();
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or NotSupportedException)
        {
            return new ProblemDto();
        }
    }

    /// <summary>Quién viaja, seudonimizado.</summary>
    /// <remarks>
    /// El orquestador necesita un viajero estable y no necesita saber quién es. Mandar el correo
    /// en crudo lo dejaría escrito en el disco de otro servicio — lo mismo que se corrigió en la
    /// HU #35 tras verlo con los procesos vivos, y lo que costó el defecto #47 en la tienda.
    /// </remarks>
    public static string TravellerId(string? guestEmail)
    {
        var correo = (guestEmail ?? string.Empty).Trim().ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(correo)))[..16].ToLowerInvariant();
    }

    private sealed record ProblemDto
    {
        public string? Title { get; init; }
        public string? Detail { get; init; }
        public string? Code { get; init; }
    }
}

// Las formas que los dos consumidores leen del orquestador. Viven acá y NO en
// Synergos.CMS.Interfaces: son la forma del contrato HTTP con otro servicio, no vocabulario del
// dominio del CMS.

internal sealed record TripMoneyDto(decimal Amount, string Currency);

/// <param name="Unfulfilled">
/// El ítem se intentó confirmar, falló y se soltó. <b>Sólo aparece en un viaje con confirmación
/// parcial</b>, y es lo que le dice a quien vendió qué tiene que devolver.
/// </param>
internal sealed record TripItemDto(
    string ProductRef, string? ProductLabel, DateTimeOffset Start, DateTimeOffset End,
    bool Confirmed, bool Unfulfilled = false);

internal sealed record TripDto(
    string Id, string? TravellerKind, string? TravellerId, string? Status,
    TripMoneyDto Total, int PendingCompensations, string? LastError,
    IReadOnlyList<TripItemDto>? Items = null,
    TripMoneyDto? Retained = null,
    TripMoneyDto? Refunded = null);
