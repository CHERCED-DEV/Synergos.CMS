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
/// </remarks>
public sealed class HttpAlquilerDeLaPuerta : IAlquilerDeLaPuerta
{
    /// <summary>El nombre del <c>HttpClient</c> que el composer registra cuando hay orquestador.</summary>
    public const string ClientName = "synergos-bff-alquiler-liquidacion";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _factory;
    private readonly IOptionsMonitor<AlquilerSettings> _settings;
    private readonly ILogger<HttpAlquilerDeLaPuerta> _log;
    private readonly bool _hayOrquestador;

    /// <summary>Construye el cliente.</summary>
    /// <param name="factory">De donde sale el cliente con su llave y su correlación.</param>
    /// <param name="settings">La sección <c>Synergos:Alquiler</c>: la clase del inquilino.</param>
    /// <param name="log">Para decir qué contestó el orquestador.</param>
    /// <param name="hayOrquestador">Si el despliegue dice dónde vive el orquestador.</param>
    public HttpAlquilerDeLaPuerta(
        IHttpClientFactory factory, IOptionsMonitor<AlquilerSettings> settings, ILogger<HttpAlquilerDeLaPuerta> log,
        bool hayOrquestador)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _hayOrquestador = hayOrquestador;
    }

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

    private async Task<RentalResult?> LlamarAsync(
        RentalAgreement contrato, string operacion, object? cuerpo, string idempotencyKey, string? sujeto,
        string quePasaba, CancellationToken ct)
    {
        if (!_hayOrquestador)
        {
            return new RentalResult(RentalOutcome.Unavailable, null, "alquiler.orchestrator_unavailable",
                "Este alquiler salió por la puerta y el despliegue no dice dónde vive su orquestador.");
        }

        using var http = _factory.CreateClient(ClientName);
        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"v1/alquileres/{Uri.EscapeDataString(contrato.RentalId)}/{operacion}")
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
            var leida = await LeerAsync(res, ct).ConfigureAwait(false);
            return leida?.ToRental(contrato) is { } alquiler
                ? new RentalResult(RentalOutcome.Ok, alquiler, null, null)
                : new RentalResult(RentalOutcome.Unavailable, null, null, "El orquestador contestó algo que no se pudo leer.");
        }

        var rechazo = await RechazoDelArbolDeServicios.LeerAsync(res, Json, ct).ConfigureAwait(false);
        _log.LogWarning("Alquiler (puerta): no se pudo {Que} ({Estado}): {Codigo}.", quePasaba, (int)res.StatusCode, rechazo?.Codigo);
        return new RentalResult(
            rechazo?.EsFirme == true ? RentalOutcome.Rejected : RentalOutcome.Unavailable,
            null, rechazo?.Codigo, rechazo?.Detalle);
    }

    private static async Task<LiquidacionWire?> LeerAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try
        {
            return await res.Content.ReadFromJsonAsync<LiquidacionWire>(Json, ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

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
