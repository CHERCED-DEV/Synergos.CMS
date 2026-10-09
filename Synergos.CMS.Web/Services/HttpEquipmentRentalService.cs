using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
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
/// <para><b>Cotizar es del CMS, en los dos modos</b> (#204). El catálogo con sus tarifas es el eje
/// 1 y vive acá; el orquestador nunca supo cotizar —recibe el total y la garantía ya calculados—.
/// Este cliente pedía <c>POST v1/rentals/quote</c>, una ruta que el orquestador no tiene, y las
/// otras tres operaciones mandaban cuerpos y esperaban respuestas que tampoco eran las suyas: con el
/// modo <c>Bff</c> no funcionaba nada. Las reglas y el precio son las de
/// <see cref="ReglasDeAlquiler"/>, las mismas que el motor en proceso.</para>
///
/// <para><b>Las fechas viajan como medianoche UTC</b>: el recurso de un equipo no tiene horario
/// (una ventana de varios días cruza la medianoche), así que a <c>Api.Booking</c> sólo le importa
/// el solape, y el orquestador devuelve la fecha tomada en UTC. Ida y vuelta, el día no se corre
/// en ningún huso.</para>
///
/// <para><b>La transitoriedad la dice la CAPACIDAD, no el código de estado</b>: se lee
/// <see cref="RechazoDelArbolDeServicios"/>, que es el único sitio del CMS que nombra
/// <c>transient</c> (#129).</para>
///
/// <para><b>Quien alquila viaja como SEUDÓNIMO</b> y nunca como su correo: lo que este cliente
/// manda queda escrito en el disco del orquestador (#47).</para>
/// </remarks>
public sealed class HttpEquipmentRentalService : IEquipmentRentalService
{
    /// <summary>Nombre del <c>HttpClient</c> con nombre que el composer registra.</summary>
    public const string ClientName = "alquiler-bff";

    private const string CodePrefix = StubEquipmentRentalService.CodePrefix;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _factory;
    private readonly IEquipmentCatalogProvider _catalog;
    private readonly IOptionsMonitor<AlquilerSettings> _settings;
    private readonly ILogger<HttpEquipmentRentalService> _log;

    /// <summary>Construye el cliente.</summary>
    /// <param name="factory">De donde sale el <c>HttpClient</c> con su llave y su correlación.</param>
    /// <param name="catalog">El catálogo con las tarifas: cotizar es del CMS (#204).</param>
    /// <param name="settings">La sección <c>Synergos:Alquiler</c>, enlazada.</param>
    /// <param name="log">Para decir qué contestó el orquestador.</param>
    public HttpEquipmentRentalService(
        IHttpClientFactory factory,
        IEquipmentCatalogProvider catalog,
        IOptionsMonitor<AlquilerSettings> settings,
        ILogger<HttpEquipmentRentalService> log)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public async Task<RentalQuote?> QuoteAsync(
        RentalRequest request, CancellationToken cancellationToken = default)
    {
        var equipo = await _catalog.GetAsync(request.EquipmentId, cancellationToken).ConfigureAwait(false);
        if (equipo is null)
        {
            return null;
        }

        var dias = ReglasDeAlquiler.Dias(request);
        return dias <= 0 ? null : ReglasDeAlquiler.Cotizar(equipo, request.Quantity, dias);
    }

    /// <inheritdoc />
    public async Task<RentalResult> ReserveAsync(
        RentalRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.EquipmentId))
        {
            return Rechazo("equipment_required", "No se dijo qué equipo alquilar.");
        }

        var equipo = await _catalog.GetAsync(request.EquipmentId, cancellationToken).ConfigureAwait(false);
        if (equipo is null)
        {
            return Rechazo("equipment_not_found", $"No existe el equipo '{request.EquipmentId}'.");
        }

        // Las reglas del catálogo, las mismas que el motor en proceso: con el modo Bff no se
        // aplicaban, porque nadie del otro lado conoce los límites de cada equipo.
        if (ReglasDeAlquiler.Revisar(equipo, request, _settings.CurrentValue.MaxRentalDays) is { } no)
        {
            return Rechazo(no.Codigo, no.Motivo);
        }

        var cotizacion = ReglasDeAlquiler.Cotizar(equipo, request.Quantity, ReglasDeAlquiler.Dias(request));

        var cuerpo = new
        {
            equipmentId = equipo.Id,
            quantity = request.Quantity,
            start = MedianocheUtc(request.Start),
            end = MedianocheUtc(request.End),
            renterKind = _settings.CurrentValue.RenterKind,
            renterId = request.RenterId,
            rentalTotal = new { amount = cotizacion.RentalTotal, currency = cotizacion.Currency },
            deposit = new { amount = cotizacion.Deposit, currency = cotizacion.Currency },
        };

        return await LlamarAsync("v1/rentals", cuerpo, idempotencyKey, "reservar", cancellationToken)
            .ConfigureAwait(false)
            ?? Rechazo("equipment_not_found", $"El orquestador no conoce el equipo '{equipo.Id}'.");
    }

    /// <inheritdoc />
    public Task<RentalResult?> ReturnAsync(
        string rentalId, decimal damageAmount, string idempotencyKey,
        CancellationToken cancellationToken = default)
        => CerrarAsync(rentalId, damageAmount, idempotencyKey, "return", "devolver", cancellationToken);

    /// <inheritdoc />
    public Task<RentalResult?> CancelAsync(
        string rentalId, decimal penaltyAmount, string idempotencyKey,
        CancellationToken cancellationToken = default)
        => CerrarAsync(rentalId, penaltyAmount, idempotencyKey, "cancel", "cancelar", cancellationToken);

    /// <summary>
    /// Devolver y cancelar: el monto contra la garantía, en la moneda en que se retuvo.
    /// </summary>
    /// <remarks>
    /// La moneda no la sabe quien llama —el seam recibe sólo el número—, así que se lee del
    /// alquiler en el orquestador antes de cerrarlo. Un alquiler que el orquestador no conoce es
    /// <c>null</c>: «no existe tal alquiler», que es distinto de que la regla diga que no.
    /// </remarks>
    private async Task<RentalResult?> CerrarAsync(
        string rentalId, decimal monto, string idempotencyKey, string accion, string quePasaba,
        CancellationToken ct)
    {
        var ruta = $"v1/rentals/{Uri.EscapeDataString(rentalId)}";

        using var http = _factory.CreateClient(ClientName);
        using var leido = await http.GetAsync(ruta, ct).ConfigureAwait(false);
        if (leido.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!leido.IsSuccessStatusCode)
        {
            return await RechazoDeAsync(leido, quePasaba, ct).ConfigureAwait(false);
        }

        var actual = await LeerAsync(leido, ct).ConfigureAwait(false);
        if (actual is null)
        {
            return Ilegible();
        }

        var cuerpo = new { amount = new { amount = monto, currency = actual.Quote.Currency } };
        return await LlamarAsync($"{ruta}/{accion}", cuerpo, idempotencyKey, quePasaba, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// El único sitio que manda una escritura al orquestador: una llamada, su llave y la lectura
    /// del rechazo.
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
            // Ok y no AlreadyDone, aunque sea un reintento: el orquestador no lo distingue en su
            // código de estado —contesta 201 al repetir una reserva y 200 al devolver por primera
            // vez—, y deducirlo de ahí era falso en los dos sentidos. Lo que un reintento tiene que
            // garantizar lo garantiza la llave: devuelve el MISMO alquiler, no abre otro.
            var alquiler = await LeerAsync(res, ct).ConfigureAwait(false);
            return alquiler is null
                ? Ilegible()
                : new RentalResult(RentalOutcome.Ok, alquiler, null, null);
        }

        return await RechazoDeAsync(res, quePasaba, ct).ConfigureAwait(false);
    }

    private async Task<RentalResult> RechazoDeAsync(HttpResponseMessage res, string quePasaba, CancellationToken ct)
    {
        var rechazo = await RechazoDelArbolDeServicios.LeerAsync(res, Json, ct).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Se grita: un 401 acá es la llave compartida mal puesta, no una regla del negocio,
            // y confundirlo manda a alguien a mirar el dominio equivocado.
            _log.LogError(
                "Alquiler: el orquestador rechazó la LLAVE COMPARTIDA al {Que}. Revisa Synergos:Alquiler:ApiKey.",
                quePasaba);
        }

        _log.LogWarning("Alquiler: no se pudo {Que} ({Estado}): {Codigo}.",
            quePasaba, (int)res.StatusCode, rechazo?.Codigo);

        return new RentalResult(
            // Sólo es una regla del negocio lo que la capacidad dice FIRME (#129): un cuerpo
            // ilegible o sin «transient» es «no sé», y eso va por el camino de indisponible.
            rechazo?.EsFirme == true ? RentalOutcome.Rejected : RentalOutcome.Unavailable,
            null, rechazo?.Codigo, rechazo?.Detalle);
    }

    private static async Task<Rental?> LeerAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try
        {
            var wire = await res.Content.ReadFromJsonAsync<RentalWire>(Json, ct).ConfigureAwait(false);
            return wire?.ToRental();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static RentalResult Ilegible()
        => new(RentalOutcome.Unavailable, null, null, "El orquestador contestó algo que no se pudo leer.");

    private static RentalResult Rechazo(string codigo, string motivo)
        => new(RentalOutcome.Rejected, null, $"{CodePrefix}.{codigo}", motivo);

    private static DateTimeOffset MedianocheUtc(DateOnly dia)
        => new(dia.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private sealed record MoneyWire(
        [property: JsonPropertyName("amount")] decimal? Amount,
        [property: JsonPropertyName("currency")] string? Currency);

    /// <summary>Lo que <c>Synergos.Bff.Alquiler</c> contesta: su <c>RentalResponse</c>.</summary>
    private sealed record RentalWire(
        [property: JsonPropertyName("rentalId")] string? RentalId,
        [property: JsonPropertyName("equipmentId")] string? EquipmentId,
        [property: JsonPropertyName("quantity")] int Quantity,
        [property: JsonPropertyName("start")] DateOnly Start,
        [property: JsonPropertyName("end")] DateOnly End,
        [property: JsonPropertyName("renterId")] string? RenterId,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("rentalTotal")] MoneyWire? RentalTotal,
        [property: JsonPropertyName("deposit")] MoneyWire? Deposit,
        [property: JsonPropertyName("depositHeld")] decimal? DepositHeld,
        [property: JsonPropertyName("damageCharged")] decimal DamageCharged)
    {
        internal Rental? ToRental()
        {
            if (string.IsNullOrWhiteSpace(RentalId)
                || RentalTotal?.Amount is not { } total
                || Deposit?.Amount is not { } garantia
                || string.IsNullOrWhiteSpace(RentalTotal.Currency)
                || DepositHeld is not { } retenido)
            {
                // Sin total, sin garantía, sin moneda o sin saber cuánto sigue retenido no hay un
                // alquiler que pintar: rellenarlo con cero afirmaría lo que nadie dijo.
                return null;
            }

            // El estado se PARSEA y no se adivina: uno que no se reconozca —«failed», el intento
            // deshecho— deja el alquiler fuera, que es mejor que decir «reservado» de algo que no
            // existió.
            if (!Enum.TryParse<RentalState>(State, ignoreCase: true, out var estado))
            {
                return null;
            }

            var dias = End.DayNumber - Start.DayNumber;
            if (dias <= 0 || Quantity <= 0)
            {
                return null;
            }

            // El valor del día sale del total que el orquestador cobró, no del catálogo de hoy: si
            // la tarifa cambió después de reservar, el alquiler sigue valiendo lo que se pactó.
            var cotizacion = new RentalQuote(
                EquipmentId ?? string.Empty, Quantity, dias, total / (dias * Quantity), total, garantia,
                RentalTotal.Currency);

            return new Rental(
                RentalId, EquipmentId ?? string.Empty, Quantity, Start, End,
                RenterId ?? string.Empty, estado, cotizacion, retenido, DamageCharged);
        }
    }
}
