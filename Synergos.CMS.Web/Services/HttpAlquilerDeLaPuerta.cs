using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Declarar y confirmar la devolución de un alquiler de la PUERTA en <c>Synergos.Bff.Alquiler</c> (línea 005, T018):
/// las operaciones posteriores al cierre, <c>POST v1/alquileres/{id}/declarar-devolucion</c> y
/// <c>…/confirmar-devolucion</c>.
/// </summary>
/// <remarks>
/// <para><b>El dueño viaja en <c>X-Synergos-Sujeto</c></b>, como lo pone la puerta al abrir: la clase del inquilino
/// de la configuración (<see cref="AlquilerSettings.RenterKind"/>) y el seudónimo que quedó en el contrato. El
/// orquestador no deja declarar un alquiler ajeno.</para>
///
/// <para><b>Lo que contesta el orquestador es la liquidación</b>, no el alquiler entero: el resto —el equipo, las
/// fechas, lo cobrado— sale del CONTRATO que este CMS emitió al reservar, que es la foto de lo pactado.</para>
///
/// <para><b>Sin destino no sale a la red</b>, como el artefacto de Eventos: un despliegue sin orquestador de alquiler
/// no tiene alquileres de la puerta, y salir a buscarlo sería esperar a la red para nada.</para>
///
/// <para><b>Cancelar lo confirmado tiene dos caminos y el CMS elige</b> (NN-8, aclaración D-005-NN8): el inquilino
/// cancela por el flujo posterior <c>alquiler.cancelacion</c> o por la liquidación propia
/// (<c>cancelar-confirmado</c>), según <see cref="AlquilerSettings.Cancellation"/>; el personal, siempre por la propia,
/// que es la que fija la penalidad.</para>
/// </remarks>
public sealed class HttpAlquilerDeLaPuerta : IAlquilerDeLaPuerta
{
    /// <summary>El nombre del <c>HttpClient</c> que el composer registra cuando hay orquestador.</summary>
    public const string ClientName = "synergos-bff-alquiler-liquidacion";

    /// <summary>El flujo posterior por el que cancela el inquilino con el interruptor en <c>Posterior</c>.</summary>
    public const string FlujoDeCancelacion = "alquiler.cancelacion";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _factory;
    private readonly IOptionsMonitor<AlquilerSettings> _settings;
    private readonly ILogger<HttpAlquilerDeLaPuerta> _log;
    private readonly bool _hayOrquestador;
    private readonly bool _cancelaPorElFlujo;

    /// <summary>Construye el cliente.</summary>
    /// <param name="factory">De donde sale el cliente con su llave y su correlación.</param>
    /// <param name="settings">La sección <c>Synergos:Alquiler</c>: la clase del inquilino.</param>
    /// <param name="log">Para decir qué contestó el orquestador.</param>
    /// <param name="hayOrquestador">Si el despliegue dice dónde vive el orquestador.</param>
    /// <param name="cancelaPorElFlujo">Si el inquilino cancela por <c>alquiler.cancelacion</c>; si no, por la liquidación
    /// propia. Lo decide el composer con el interruptor.</param>
    public HttpAlquilerDeLaPuerta(
        IHttpClientFactory factory, IOptionsMonitor<AlquilerSettings> settings, ILogger<HttpAlquilerDeLaPuerta> log,
        bool hayOrquestador, bool cancelaPorElFlujo)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _hayOrquestador = hayOrquestador;
        _cancelaPorElFlujo = cancelaPorElFlujo;
    }

    /// <summary>
    /// La llave con que el CMS abre la cancelación de <paramref name="rentalId"/> por el flujo posterior: sale del alquiler
    /// y no del navegador. Una cancelación por alquiler, y repetirla tras un fallo a medias vuelve a pasar por las mismas
    /// llaves del orquestador (la devolución no se duplica).
    /// </summary>
    public static string LlaveDeLaCancelacion(string rentalId) => $"cancelar-{rentalId}";

    /// <inheritdoc />
    public Task<RentalResult?> DeclareReturnAsync(
        RentalAgreement agreement, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agreement);
        return LlamarAsync(agreement, "declarar-devolucion", null, idempotencyKey,
            sujeto: $"{_settings.CurrentValue.RenterKind}:{agreement.RenterId}", "declarar la devolución", cancellationToken);
    }

    /// <inheritdoc />
    public Task<RentalResult?> ConfirmReturnAsync(
        RentalAgreement agreement, decimal damageAmount, string staffId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agreement);
        var cuerpo = new { damage = new { amount = damageAmount, currency = agreement.Currency }, staffId };
        return LlamarAsync(agreement, "confirmar-devolucion", cuerpo, idempotencyKey, sujeto: null,
            "confirmar la devolución", cancellationToken);
    }

    /// <inheritdoc />
    public Task<RentalResult?> CancelAsync(
        RentalAgreement agreement, RentalCanceller canceller, decimal penaltyAmount, string? staffId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agreement);
        var porPersonal = canceller == RentalCanceller.Staff;
        if (!porPersonal && _cancelaPorElFlujo) return CancelarPorElFlujoAsync(agreement, cancellationToken);

        var cuerpo = new
        {
            actor = porPersonal ? "staff" : "renter",
            penalty = new { amount = porPersonal ? penaltyAmount : 0m, currency = agreement.Currency },
            staffId = porPersonal ? staffId : null,
        };
        return LlamarAsync(agreement, "cancelar-confirmado", cuerpo, idempotencyKey,
            sujeto: porPersonal ? null : $"{_settings.CurrentValue.RenterKind}:{agreement.RenterId}", "cancelar", cancellationToken);
    }

    /// <summary>
    /// El inquilino cancela por el flujo posterior (<c>POST v1/flujos/alquiler.cancelacion</c>): la saga posterior lee la
    /// renta original, aplica la regla del día de inicio, suelta las unidades, devuelve lo cobrado y anula la garantía si
    /// la hay. Completada, el alquiler está cancelado: no queda nada retenido.
    /// </summary>
    private Task<RentalResult?> CancelarPorElFlujoAsync(RentalAgreement contrato, CancellationToken ct)
        => LlamarAsync(contrato, $"v1/flujos/{FlujoDeCancelacion}", new { alquiler = contrato.RentalId },
            LlaveDeLaCancelacion(contrato.RentalId), $"{_settings.CurrentValue.RenterKind}:{contrato.RenterId}",
            async (res, c) => await LeerAsync<CancelacionWire>(res, c).ConfigureAwait(false) is { } leida
                && leida.Cancelo(contrato.RentalId)
                    ? new LiquidacionWire(contrato.RentalId, "cancelled", 0m, 0m).ToRental(contrato)
                    : null,
            "cancelar", ct);

    private Task<RentalResult?> LlamarAsync(
        RentalAgreement contrato, string operacion, object? cuerpo, string idempotencyKey, string? sujeto,
        string quePasaba, CancellationToken ct)
        => LlamarAsync(contrato, $"v1/alquileres/{Uri.EscapeDataString(contrato.RentalId)}/{operacion}", cuerpo, idempotencyKey,
            sujeto, async (res, c) => (await LeerAsync<LiquidacionWire>(res, c).ConfigureAwait(false))?.ToRental(contrato),
            quePasaba, ct);

    private async Task<RentalResult?> LlamarAsync(
        RentalAgreement contrato, string ruta, object? cuerpo, string idempotencyKey, string? sujeto,
        Func<HttpResponseMessage, CancellationToken, Task<Rental?>> leer, string quePasaba, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contrato);
        if (!_hayOrquestador)
        {
            return new RentalResult(RentalOutcome.Unavailable, null, "alquiler.orchestrator_unavailable",
                "Este alquiler salió por la puerta y el despliegue no dice dónde vive su orquestador.");
        }

        using var http = _factory.CreateClient(ClientName);
        using var req = new HttpRequestMessage(HttpMethod.Post, ruta)
        {
            Content = cuerpo is null ? null : JsonContent.Create(cuerpo, options: Json),
        };
        req.Headers.Add("Idempotency-Key", idempotencyKey);
        if (sujeto is not null) req.Headers.TryAddWithoutValidation("X-Synergos-Sujeto", sujeto);

        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (res.IsSuccessStatusCode)
        {
            return await leer(res, ct).ConfigureAwait(false) is { } alquiler
                ? new RentalResult(RentalOutcome.Ok, alquiler, null, null)
                : new RentalResult(RentalOutcome.Unavailable, null, null, "El orquestador contestó algo que no se pudo leer.");
        }

        var rechazo = await RechazoDelArbolDeServicios.LeerAsync(res, Json, ct).ConfigureAwait(false);
        _log.LogWarning("Alquiler (puerta): no se pudo {Que} ({Estado}): {Codigo}.", quePasaba, (int)res.StatusCode, rechazo?.Codigo);
        return new RentalResult(
            rechazo?.EsFirme == true ? RentalOutcome.Rejected : RentalOutcome.Unavailable,
            null, rechazo?.Codigo, rechazo?.Detalle);
    }

    private static async Task<T?> LeerAsync<T>(HttpResponseMessage res, CancellationToken ct) where T : class
    {
        try
        {
            return await res.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Lo que contesta <c>alquiler.cancelacion</c>: la saga posterior, en qué quedó y sobre qué alquiler.</summary>
    private sealed record CancelacionWire(
        [property: JsonPropertyName("estado")] string? Estado,
        [property: JsonPropertyName("datos")] CancelacionDatosWire? Datos)
    {
        /// <summary>Si es la cancelación COMPLETADA de ese alquiler: lo demás no se da por cancelado.</summary>
        internal bool Cancelo(string rentalId)
            => string.Equals(Estado, "Completed", StringComparison.Ordinal)
               && string.Equals(Datos?.Alquiler, rentalId, StringComparison.Ordinal);
    }

    /// <summary>La salida de <c>alquiler.cancelacion</c> que el CMS lee: el alquiler que canceló.</summary>
    private sealed record CancelacionDatosWire([property: JsonPropertyName("alquiler")] string? Alquiler);

    /// <summary>Lo que <c>Synergos.Bff.Alquiler</c> contesta: su <c>RentalSettlementResponse</c>.</summary>
    private sealed record LiquidacionWire(
        [property: JsonPropertyName("rentalId")] string? RentalId,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("depositHeld")] decimal? DepositHeld,
        [property: JsonPropertyName("damageCharged")] decimal? DamageCharged)
    {
        /// <summary>El alquiler: lo pactado, del contrato; dónde está y cuánto sigue retenido, de la liquidación.</summary>
        internal Rental? ToRental(RentalAgreement contrato)
        {
            // El estado se PARSEA y no se adivina, como en el cliente de la ruta directa.
            if (!string.Equals(RentalId, contrato.RentalId, StringComparison.Ordinal)
                || DepositHeld is not { } retenido
                || !Enum.TryParse<RentalState>(State, ignoreCase: true, out var estado))
            {
                return null;
            }

            var dias = contrato.End.DayNumber - contrato.Start.DayNumber;
            var cotizacion = new RentalQuote(
                contrato.EquipmentId, contrato.Quantity, dias,
                dias > 0 && contrato.Quantity > 0 ? contrato.RentalTotal / (dias * contrato.Quantity) : 0m,
                contrato.RentalTotal, contrato.DepositHeld, contrato.Currency);

            return new Rental(
                contrato.RentalId, contrato.EquipmentId, contrato.Quantity, contrato.Start, contrato.End,
                contrato.RenterId, estado, cotizacion, retenido, DamageCharged ?? 0m);
        }
    }
}
