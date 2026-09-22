using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El eje 2 de Alquiler contra <c>Synergos.Bff.Alquiler</c>: apartar la ventana y retener la
/// garantía las hace el orquestador, no este proceso.
/// </summary>
/// <remarks>
/// <para>Lo elige <c>Synergos:Alquiler:Mode=Bff</c>; el default sigue siendo el motor en proceso.
/// El orquestador es quien tiene la plata, así que es quien ordena capturar, anular y devolver
/// — la tercera pregunta del repo (#57).</para>
///
/// <para><b>La transitoriedad la dice la CAPACIDAD, no el código de estado</b>: se lee
/// <see cref="RechazoDelArbolDeServicios"/>, que es el único sitio del CMS que nombra
/// <c>transient</c> (#129). Repetir aquí una tabla de códigos sería la segunda verdad que ese
/// ticket existió para quitar.</para>
///
/// <para><b>Quien alquila viaja como SEUDÓNIMO</b> y nunca como su correo: lo que este cliente
/// manda queda escrito en el disco del orquestador (#47).</para>
/// </remarks>
public sealed class HttpEquipmentRentalService : IEquipmentRentalService
{
    /// <summary>Nombre del <c>HttpClient</c> con nombre que el composer registra.</summary>
    public const string ClientName = "alquiler-bff";

    /// <summary>La llave compartida servicio↔servicio.</summary>
    public const string ApiKeyHeader = "X-Synergos-Key";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _factory;
    private readonly IOptionsMonitor<AlquilerSettings> _settings;
    private readonly ILogger<HttpEquipmentRentalService> _log;

    /// <summary>Construye el cliente.</summary>
    /// <param name="factory">De donde sale el <c>HttpClient</c> con su llave y su correlación.</param>
    /// <param name="settings">La sección <c>Synergos:Alquiler</c>, enlazada.</param>
    /// <param name="log">Para decir qué contestó el orquestador.</param>
    public HttpEquipmentRentalService(
        IHttpClientFactory factory,
        IOptionsMonitor<AlquilerSettings> settings,
        ILogger<HttpEquipmentRentalService> log)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public async Task<RentalQuote?> QuoteAsync(
        RentalRequest request, CancellationToken cancellationToken = default)
    {
        using var http = _factory.CreateClient(ClientName);
        using var res = await http.PostAsJsonAsync(
            "v1/rentals/quote", Cuerpo(request), Json, cancellationToken).ConfigureAwait(false);

        if (res.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!res.IsSuccessStatusCode)
        {
            var rechazo = await RechazoDelArbolDeServicios.LeerAsync(res, cancellationToken).ConfigureAwait(false);
            _log.LogWarning(
                "Alquiler: el orquestador no cotizó ({Estado}): {Codigo}.", (int)res.StatusCode, rechazo.Codigo);
            return null;
        }

        var wire = await res.Content.ReadFromJsonAsync<QuoteWire>(Json, cancellationToken).ConfigureAwait(false);
        return wire?.ToQuote();
    }

    /// <inheritdoc />
    public Task<RentalResult> ReserveAsync(
        RentalRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
        => LlamarAsync("v1/rentals", Cuerpo(request), idempotencyKey, "reservar", cancellationToken)!;

    /// <inheritdoc />
    public Task<RentalResult?> ReturnAsync(
        string rentalId, decimal damageAmount, string idempotencyKey,
        CancellationToken cancellationToken = default)
        => LlamarAsync($"v1/rentals/{Uri.EscapeDataString(rentalId)}/return",
            new { damageAmount }, idempotencyKey, "devolver", cancellationToken);

    /// <inheritdoc />
    public Task<RentalResult?> CancelAsync(
        string rentalId, decimal penaltyAmount, string idempotencyKey,
        CancellationToken cancellationToken = default)
        => LlamarAsync($"v1/rentals/{Uri.EscapeDataString(rentalId)}/cancel",
            new { penaltyAmount }, idempotencyKey, "cancelar", cancellationToken);

    /// <summary>
    /// El único sitio que habla con el orquestador: una llamada, su llave y la lectura del
    /// rechazo.
    /// </summary>
    /// <remarks>
    /// <b>Un 404 devuelve <c>null</c> y no un rechazo</b>, que es la distinción del seam: null es
    /// «no existe tal alquiler» y un rechazo dice que existe y que la regla dijo que no.
    /// </remarks>
    private async Task<RentalResult?> LlamarAsync(
        string ruta, object cuerpo, string idempotencyKey, string quePasaba, CancellationToken ct)
    {
        using var http = _factory.CreateClient(ClientName);
        using var req = new HttpRequestMessage(HttpMethod.Post, ruta)
        {
            Content = JsonContent.Create(cuerpo, options: Json),
        };

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            req.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);

        if (res.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (res.IsSuccessStatusCode)
        {
            var wire = await res.Content.ReadFromJsonAsync<RentalWire>(Json, ct).ConfigureAwait(false);
            var alquiler = wire?.ToRental();
            return alquiler is null
                ? new RentalResult(RentalOutcome.Unavailable, null, null,
                    "El orquestador contestó algo que no se pudo leer.")
                : new RentalResult(
                    res.StatusCode == HttpStatusCode.OK ? RentalOutcome.AlreadyDone : RentalOutcome.Ok,
                    alquiler, null, null);
        }

        var rechazo = await RechazoDelArbolDeServicios.LeerAsync(res, ct).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Se grita: un 401 acá es la llave compartida mal puesta, no una regla del negocio,
            // y confundirlo manda a alguien a mirar el dominio equivocado.
            _log.LogError(
                "Alquiler: el orquestador rechazó la LLAVE COMPARTIDA al {Que}. Revisa Synergos:Alquiler:ApiKey.",
                quePasaba);
        }

        _log.LogWarning("Alquiler: no se pudo {Que} ({Estado}): {Codigo}.",
            quePasaba, (int)res.StatusCode, rechazo.Codigo);

        return new RentalResult(
            rechazo.EsTransitorio ? RentalOutcome.Unavailable : RentalOutcome.Rejected,
            null, rechazo.Codigo, rechazo.Motivo);
    }

    private object Cuerpo(RentalRequest r) => new
    {
        equipmentId = r.EquipmentId,
        quantity = r.Quantity,
        start = r.Start.ToString("yyyy-MM-dd"),
        end = r.End.ToString("yyyy-MM-dd"),
        renterKind = _settings.CurrentValue.RenterKind,
        renterId = r.RenterId,
    };

    private sealed record QuoteWire(
        [property: JsonPropertyName("equipmentId")] string? EquipmentId,
        [property: JsonPropertyName("quantity")] int Quantity,
        [property: JsonPropertyName("days")] int Days,
        [property: JsonPropertyName("perDay")] decimal PerDay,
        [property: JsonPropertyName("rentalTotal")] decimal RentalTotal,
        [property: JsonPropertyName("deposit")] decimal Deposit)
    {
        internal RentalQuote ToQuote()
            => new(EquipmentId ?? string.Empty, Quantity, Days, PerDay, RentalTotal, Deposit);
    }

    private sealed record RentalWire(
        [property: JsonPropertyName("rentalId")] string? RentalId,
        [property: JsonPropertyName("equipmentId")] string? EquipmentId,
        [property: JsonPropertyName("quantity")] int Quantity,
        [property: JsonPropertyName("start")] DateOnly Start,
        [property: JsonPropertyName("end")] DateOnly End,
        [property: JsonPropertyName("renterId")] string? RenterId,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("quote")] QuoteWire? Quote,
        [property: JsonPropertyName("depositHeld")] decimal DepositHeld,
        [property: JsonPropertyName("damageCharged")] decimal DamageCharged)
    {
        internal Rental? ToRental()
        {
            if (string.IsNullOrWhiteSpace(RentalId) || Quote is null)
            {
                return null;
            }

            // El estado se PARSEA y no se adivina: uno que no se reconozca deja el alquiler
            // fuera, que es mejor que decir «reservado» de algo que el orquestador cerró.
            if (!Enum.TryParse<RentalState>(State, ignoreCase: true, out var estado))
            {
                return null;
            }

            return new Rental(
                RentalId, EquipmentId ?? string.Empty, Quantity, Start, End,
                RenterId ?? string.Empty, estado, Quote.ToQuote(), DepositHeld, DamageCharged);
        }
    }
}
