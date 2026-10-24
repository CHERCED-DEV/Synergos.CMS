using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El <see cref="IEquipmentOfferPublisher"/> que publica la oferta en <c>Synergos.Bff.Alquiler</c>
/// (<c>POST v1/ofertas</c> y <c>POST v1/ofertas/{equipmentId}/retirar</c>; línea 005, research R2, T029).
/// </summary>
/// <remarks>
/// <para><b>Manda lo que el contenido ya decidió y nada más</b>: la tarifa, los tramos, la garantía por unidad y los
/// límites son los que <c>EquipmentContentRules</c> dejó en la ficha (con el tope de días del despliegue ya
/// aplicado). Lo que cotiza la renta por la puerta es esto; el navegador no manda ninguna cifra.</para>
///
/// <para><b>Sin destino no intenta</b>: un clon limpio no tiene orquestador, y salir a buscarlo en cada publicar del
/// editor sería esperar a la red para nada. Con destino y el orquestador caído deja el motivo en el log y devuelve
/// <see cref="EquipmentOfferOutcome.Failed"/>: nunca lanza, porque quien llama es el publicar del editor.</para>
///
/// <para><b>Bajar las unidades no cancela nada</b> (NN-6): el orquestador revisa el recurso en la agenda y contesta qué
/// reservas vigentes quedaron por encima de los cupos. Siguen confirmadas; aquí quedan en el registro, con nombre, para
/// que el personal decida qué hacer con ellas.</para>
/// </remarks>
public sealed class HttpEquipmentOfferPublisher : IEquipmentOfferPublisher
{
    /// <summary>Cliente nombrado que registra el composer, hacia el mismo destino que la renta.</summary>
    public const string ClientName = "synergos-bff-alquiler-oferta";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _clients;
    private readonly ILogger<HttpEquipmentOfferPublisher> _log;
    private readonly bool _hayDestino;

    /// <summary>Construye el publicador.</summary>
    /// <param name="clients">La fábrica de clientes; el de <see cref="ClientName"/> lo arma la pieza del árbol.</param>
    /// <param name="log">Dónde queda lo que no se pudo publicar.</param>
    /// <param name="hayDestino">Si el despliegue configuró dónde vive el orquestador.</param>
    public HttpEquipmentOfferPublisher(IHttpClientFactory clients, ILogger<HttpEquipmentOfferPublisher> log, bool hayDestino)
    {
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _hayDestino = hayDestino;
    }

    /// <inheritdoc />
    public Task<EquipmentOfferOutcome> PublishAsync(RentalEquipment equipo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(equipo);

        // Sin unidades no hay nada que alquilar: lo que se hubiera publicado antes se retira entero.
        if (equipo.Units <= 0) return RetireAsync(equipo.Id, cancellationToken);

        return EnviarAsync(equipo.Id, "publicar", () => new HttpRequestMessage(HttpMethod.Post, "v1/ofertas")
        {
            Content = JsonContent.Create(Oferta(equipo)),
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<EquipmentOfferOutcome> RetireAsync(string equipmentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(equipmentId);
        return EnviarAsync(equipmentId, "retirar", () => new HttpRequestMessage(
            HttpMethod.Post, $"v1/ofertas/{Uri.EscapeDataString(equipmentId)}/retirar"), cancellationToken);
    }

    private async Task<EquipmentOfferOutcome> EnviarAsync(
        string equipo, string que, Func<HttpRequestMessage> peticion, CancellationToken cancellationToken)
    {
        if (!_hayDestino)
        {
            _log.LogDebug("La oferta de {Equipo} no se {Que}: no hay orquestador de Alquiler configurado.", equipo, que);
            return EquipmentOfferOutcome.NoDestination;
        }

        using var req = peticion();
        // Una llave por publicación: los reintentos de la cadena repiten ESTA petición, y una publicación nueva
        // —otro publicar, o republicar— es otra llave, que el orquestador espera.
        req.Headers.Add("Idempotency-Key", "oferta-" + Guid.NewGuid().ToString("n"));

        try
        {
            using var res = await _clients.CreateClient(ClientName).SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                _log.LogInformation("Oferta del equipo {Equipo}: {Que} en el orquestador, hecho.", equipo, que);
                if (await FueraAsync(res, cancellationToken).ConfigureAwait(false) is { Count: > 0 } fuera)
                {
                    _log.LogWarning(
                        "El equipo {Equipo} quedó con menos unidades y {Cuantas} reserva(s) vigente(s) ya no caben ({Reservas}): "
                        + "siguen confirmadas; qué hacer con ellas lo decide el personal.",
                        equipo, fuera.Count, string.Join(", ", fuera));
                }
                return EquipmentOfferOutcome.Published;
            }

            var rechazo = await RechazoDelArbolDeServicios.LeerAsync(res, Json, cancellationToken).ConfigureAwait(false);
            _log.LogWarning(
                "El orquestador no pudo {Que} la oferta del equipo {Equipo}: {Estado} {Codigo} {Detalle}. Se repara republicando.",
                que, equipo, (int)res.StatusCode, rechazo?.Codigo, rechazo?.Detalle);
            return EquipmentOfferOutcome.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _log.LogWarning(ex, "El orquestador de Alquiler no respondió al {Que} la oferta del equipo {Equipo}. Se repara republicando.",
                que, equipo);
            return EquipmentOfferOutcome.Failed;
        }
    }

    /// <summary>
    /// Las reservas que la revisión de unidades dejó fuera, de la respuesta de la oferta; vacío si no trae ninguna o no
    /// se puede leer: quien publica ya publicó, y el registro de la oferta en el orquestador las conserva.
    /// </summary>
    private static async Task<IReadOnlyList<string>> FueraAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try
        {
            var oferta = await res.Content.ReadFromJsonAsync<OfertaWire>(Json, ct).ConfigureAwait(false);
            return oferta?.OutsideReservations ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Lo que se lee de la respuesta de la oferta; lo cruza <c>ConsumidorCmsDeBffAlquilerTests</c>.</summary>
    /// <param name="OutsideReservations">Las reservas vigentes por encima de los cupos tras bajar las unidades.</param>
    internal sealed record OfertaWire(IReadOnlyList<string>? OutsideReservations);

    /// <summary>El cuerpo de <c>PublishEquipmentOffer</c>, con los nombres del contrato del orquestador.</summary>
    private static object Oferta(RentalEquipment e) => new
    {
        equipmentId = e.Id,
        currency = e.Currency,
        dailyRate = e.DailyRate,
        rates = e.Rates.Select(r => new { minDays = r.MinDays, perDay = r.PerDay }).ToList(),
        depositPerUnit = e.Deposit,
        minDays = e.MinDays,
        maxDays = e.MaxDays,
        units = e.Units,
    };
}
