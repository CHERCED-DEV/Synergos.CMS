using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using static Synergos.CMS.Web.Services.CompraDeEventosEnElOrquestador;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Lo que el CMS hace con una compra de entradas del orquestador y que el orquestador no lleva: quién
/// va a sentarse, y la forma de la compra tal como la contesta <c>Bff.Eventos</c>.
/// </summary>
/// <remarks>
/// Lo comparten los dos caminos que hablan con el orquestador —la ruta vieja
/// (<see cref="HttpEventTicketingService"/>) y el artefacto de la puerta (<see cref="ArtefactoDeEventos"/>)—
/// hasta el retiro de la primera (ADR 0140 F3, paso 19). Dos copias de cómo se emparejan los asistentes
/// con lo apartado darían dos entradas distintas para la misma butaca.
/// </remarks>
internal static class CompraDeEventosEnElOrquestador
{
    /// <summary>Una orden nueva: 128 bits de azar criptográfico, la misma forma que la del motor en proceso.</summary>
    internal static string NuevaOrden()
        => "evord_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>
    /// Empareja lo que el orquestador apartó con los asistentes que solo conoce el CMS.
    /// </summary>
    /// <remarks>
    /// <para><b>Por orden, y el orden está garantizado</b>: el orquestador aparta un cupo por
    /// línea y las devuelve en el mismo orden en que se mandaron. Una línea de tres entradas es
    /// UN apartado de cantidad tres, así que se expande antes de emparejar — exactamente como
    /// hace el motor en proceso.</para>
    ///
    /// <para><b>El identificador de lo apartado se DERIVA, no se pide.</b> El orquestador no
    /// expone los identificadores internos de <c>Api.Inventory</c> y hace bien: sacarlos solo
    /// invita a que alguien los cablee río arriba. Sirve cualquier cosa estable y única dentro de
    /// la compra, así que se usa la saga más el ordinal — y como la llave de idempotencia es
    /// determinista, un reintento reproduce los mismos identificadores y por tanto las mismas
    /// entradas.</para>
    /// </remarks>
    internal static List<PersistedEventUnit> Emparejar(PurchaseDto compra, IReadOnlyList<EventAttendeeInfo> attendees)
    {
        var unidades = new List<PersistedEventUnit>(attendees.Count);
        var n = 0;

        foreach (var apartado in compra.Held ?? Array.Empty<HeldDto>())
        {
            for (var i = 0; i < Math.Max(1, apartado.Quantity); i++)
            {
                if (n >= attendees.Count)
                {
                    // El orquestador apartó más de lo que se pidió. No se inventa un asistente:
                    // una entrada sin portador no se puede emitir ni escanear.
                    break;
                }
                var quien = attendees[n];
                unidades.Add(new PersistedEventUnit(
                    TierCode: apartado.Tier,
                    TierName: apartado.Tier,
                    Seat: apartado.Seat,
                    // El desglose por butaca no vuelve del orquestador —su respuesta lleva el
                    // total— y repartirlo a ojo sería inventar. Cero es honesto; el total de la
                    // compra, que es lo que se cobró, sí está y va en la orden.
                    Price: 0m,
                    Currency: compra.Total.Currency,
                    AttendeeName: (quien.Name ?? string.Empty).Trim(),
                    AttendeeEmail: (quien.Email ?? string.Empty).Trim(),
                    AttendeeDocument: quien.DocumentId?.Trim(),
                    ReservationId: SeatRef(compra.Id, n)));
                n++;
            }
        }

        return unidades;
    }

    /// <summary>El identificador de lo apartado, determinista dentro de la compra.</summary>
    /// <remarks>
    /// <b>Sin guiones, y lo descubrió un test</b>: el payload del token es
    /// <c>SYN-TKT-{evento}-{entrada}-v{n}</c> y al deshacerlo se corta por el último guion, así
    /// que un id de entrada con guiones verifica bien y devuelve OTRA entrada — QR firmado,
    /// puerta cerrada, cero errores en el log. El identificador de la saga sí los lleva
    /// (<c>evt-…</c>), así que se quitan acá. <c>EventTicketIssuer.TicketIdOf</c> lo exige de
    /// todos modos: esto es cumplir el contrato, no esquivarlo.
    /// </remarks>
    internal static string SeatRef(string sagaId, int ordinal)
    {
        var limpio = new string(sagaId.Where(char.IsAsciiLetterOrDigit).ToArray());
        return $"{limpio}{ordinal:D2}";
    }

    // Los DTO viven acá y NO en Synergos.CMS.Interfaces: son la forma del contrato HTTP con otro
    // servicio, no vocabulario del dominio del CMS. Sólo los campos que se leen: los cruza
    // ConsumidorCmsDeBffEventosTests contra el contrato comiteado del orquestador.

    internal sealed record MoneyDto(decimal Amount, string Currency);

    internal sealed record HeldDto(string Tier, string? Seat, int Quantity);

    internal sealed record PurchaseDto(
        string Id, string? BuyerKind, string? BuyerId, string? EventId, string? Status,
        MoneyDto Total, IReadOnlyList<HeldDto>? Held, int PendingCompensations, string? LastError);
}

/// <summary>Cómo terminó una operación del artefacto: lo que se emitió, o el motivo con su código.</summary>
/// <param name="Estado">El estado HTTP.</param>
/// <param name="Codigo">El código del motivo (<c>eventos.*</c>), nulo si salió bien.</param>
/// <param name="Mensaje">Para el log y la consola, no para la persona.</param>
/// <param name="EstadoDeLaCompra">En qué quedó la saga, cuando eso es el motivo.</param>
/// <param name="Entradas">Las entradas emitidas.</param>
/// <param name="Anotados">Cuántos asistentes quedaron anotados.</param>
/// <param name="Transitorio">Si reintentar puede salir distinto.</param>
public sealed record ResultadoDelArtefacto(
    int Estado, string? Codigo = null, string? Mensaje = null, string? EstadoDeLaCompra = null,
    EventConfirmationResult? Entradas = null, int Anotados = 0, bool Transitorio = false)
{
    /// <summary>Si salió bien.</summary>
    public bool Bien => Codigo is null;
}

/// <summary>
/// El artefacto de una compra de entradas hecha por la puerta (ADR 0140 F3): quién se sienta —antes de
/// cerrar— y las entradas con su QR —después—. El orquestador mueve aforo y plata; esto se queda en el CMS.
/// </summary>
/// <remarks>
/// <para><b>Por qué vive acá y no en la puerta.</b> La respuesta del orquestador no trae entradas, y no
/// debe: el firmante del QR y el registro de entradas son del CMS (<see cref="EventTicketLedger"/>), y
/// la lista de asistentes son datos personales que el orquestador no tiene por qué guardar. La puerta
/// devuelve lo del orquestador sin reinterpretarlo; esto es lo que el CMS agrega, con sus propias rutas
/// (<c>/api/eventos/compras/{id}/asistentes</c> y <c>…/entradas</c>).</para>
///
/// <para><b>Siempre de quien compró.</b> Cada consulta al orquestador lleva el sujeto del miembro de la
/// sesión —el mismo que puso la puerta al abrir—, así que una compra ajena es un 404 del orquestador. Y
/// lo ya confirmado se busca en el registro por saga Y comprador: nunca por la saga sola.</para>
///
/// <para><b>Nunca hay cobro sin entradas.</b> Una saga <c>Completed</c> emite al leerla aunque nadie
/// anotara asistentes: el comprador queda como portador de todas, y las puede transferir.</para>
///
/// <para><b>Lo confirmado no toca la red</b>: emitido una vez, se lee del registro, y un orquestador
/// caído no deja a nadie sin sus entradas. Sólo lo <c>Pending</c> se reconcilia contra el orquestador.</para>
/// </remarks>
public sealed class ArtefactoDeEventos
{
    /// <summary>Cliente nombrado hacia el orquestador, por la pieza del árbol.</summary>
    public const string ClientName = "synergos-bff-eventos-artefacto";

    /// <summary>El flujo de la puerta cuyas compras atiende: de su configuración sale el <c>Kind</c> del sujeto.</summary>
    public const string Flujo = "eventos.compra";

    private const string Cabecera = "X-Synergos-Sujeto";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _clientes;
    private readonly EventTicketLedger _ledger;
    private readonly IOptionsMonitor<PuertaSettings> _puerta;
    private readonly ILogger<ArtefactoDeEventos> _log;
    private readonly bool _hayDestino;
    private readonly Func<DateTimeOffset> _ahora;

    /// <param name="hayDestino">Si el despliegue dice dónde vive el orquestador.</param>
    public ArtefactoDeEventos(
        IHttpClientFactory clientes, EventTicketLedger ledger, IOptionsMonitor<PuertaSettings> puerta,
        ILogger<ArtefactoDeEventos> log, bool hayDestino, Func<DateTimeOffset>? ahora = null)
    {
        _clientes = clientes;
        _ledger = ledger;
        _puerta = puerta;
        _log = log;
        _hayDestino = hayDestino;
        _ahora = ahora ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>El <c>Kind</c> con que la puerta nombra al comprador, o nulo si el flujo no está abierto.</summary>
    private string? Kind => _puerta.CurrentValue.Flujos.TryGetValue(Flujo, out var f) ? f.SujetoKind : null;

    /// <summary>Anota quién va a sentarse, antes de cerrar: sólo sobre una compra en curso del miembro.</summary>
    public async Task<ResultadoDelArtefacto> AnotarAsistentesAsync(
        string compraId, Guid miembro, string? correo, string? nombre,
        IReadOnlyList<EventAttendeeInfo> asistentes, CancellationToken ct)
    {
        if (NoDisponible() is { } cerrado) return cerrado;
        if (asistentes.Count == 0 || asistentes.Any(a => string.IsNullOrWhiteSpace(a.Name)
                                                         || string.IsNullOrWhiteSpace(a.Email) || !a.Email.Contains('@')))
        {
            return new ResultadoDelArtefacto(StatusCodes.Status400BadRequest, "eventos.asistentes_invalidos",
                "Cada asistente necesita nombre y correo.");
        }

        var sujeto = (Kind: Kind!, Id: miembro.ToString("n"));
        var (compra, fallo) = await LeerAsync(compraId, sujeto, ct).ConfigureAwait(false);
        if (fallo is not null) return fallo;

        if (!string.Equals(compra!.Status, "Running", StringComparison.Ordinal))
        {
            return new ResultadoDelArtefacto(StatusCodes.Status409Conflict, "eventos.compra_ya_cerrada",
                "Los asistentes se anotan antes de cerrar la compra.", compra.Status);
        }

        var apartadas = (compra.Held ?? []).Sum(h => Math.Max(1, h.Quantity));
        if (asistentes.Count != apartadas)
        {
            return new ResultadoDelArtefacto(StatusCodes.Status400BadRequest, "eventos.asistentes_no_cuadran",
                $"La compra apartó {apartadas} entradas y llegaron {asistentes.Count} asistentes.");
        }

        var previa = await OrdenDeAsync(compra.Id, sujeto, ct).ConfigureAwait(false);
        if (previa is { Status: EventOrderStatus.Confirmed })
        {
            return new ResultadoDelArtefacto(StatusCodes.Status409Conflict, "eventos.compra_ya_cerrada",
                "Las entradas ya se emitieron.", "Completed");
        }

        // Volver a anotar reemplaza la lista: mientras no se cierre, el comprador puede corregirla.
        var orden = Orden(compra, sujeto, correo, nombre, Emparejar(compra, asistentes), previa);
        await _ledger.SaveAsync(orden, ct).ConfigureAwait(false);
        return new ResultadoDelArtefacto(StatusCodes.Status200OK, Anotados: orden.Units.Count);
    }

    /// <summary>Las entradas de una compra cerrada del miembro: las emite al leerla, una sola vez.</summary>
    public async Task<ResultadoDelArtefacto> EntradasAsync(
        string compraId, Guid miembro, string? correo, string? nombre, CancellationToken ct)
    {
        if (NoDisponible() is { } cerrado) return cerrado;
        var sujeto = (Kind: Kind!, Id: miembro.ToString("n"));

        var previa = await OrdenDeAsync(compraId, sujeto, ct).ConfigureAwait(false);
        if (previa is { Status: EventOrderStatus.Confirmed })
        {
            return new ResultadoDelArtefacto(StatusCodes.Status200OK, Entradas: _ledger.ConfirmationOf(previa));
        }

        var (compra, fallo) = await LeerAsync(compraId, sujeto, ct).ConfigureAwait(false);
        if (fallo is not null) return fallo;

        if (string.Equals(compra!.Status, "Running", StringComparison.Ordinal))
        {
            return new ResultadoDelArtefacto(StatusCodes.Status409Conflict, "eventos.compra_en_curso",
                "La compra todavía no se cerró: las entradas salen después de cobrar.", compra.Status);
        }
        if (!string.Equals(compra.Status, "Completed", StringComparison.Ordinal))
        {
            return new ResultadoDelArtefacto(StatusCodes.Status409Conflict, "eventos.compra_no_completada",
                compra.LastError ?? "La compra no se completó: no hay entradas que emitir.", compra.Status);
        }

        // Sin asistentes anotados, el comprador es el portador de todas: nunca hay cobro sin entradas.
        var orden = previa ?? Orden(compra, sujeto, correo, nombre, Emparejar(compra, Portador(compra, correo, nombre)), null);
        var confirmada = orden with { Status = EventOrderStatus.Confirmed };
        await _ledger.SaveAsync(confirmada, ct).ConfigureAwait(false);
        return new ResultadoDelArtefacto(StatusCodes.Status200OK, Entradas: _ledger.ConfirmationOf(confirmada));
    }

    /// <summary>
    /// Confirma de este lado las compras PENDIENTES que <paramref name="lasQueTocan"/> elige y que el
    /// orquestador ya completó. Lo confirmado no se mira: no toca la red.
    /// </summary>
    /// <remarks>
    /// Es lo que cubre al comprador que cerró la compra y no volvió a pedir sus entradas: «mis entradas»
    /// y la consola del organizador las ven igual. Un orquestador caído deja el log y no tumba la lista.
    /// </remarks>
    public async Task ReconciliarAsync(Func<PersistedEventOrder, bool> lasQueTocan, CancellationToken ct)
    {
        if (!_hayDestino) return;

        var pendientes = (await _ledger.LoadAllAsync(ct).ConfigureAwait(false))
            .Where(o => o.Status == EventOrderStatus.Pending
                        && !string.IsNullOrWhiteSpace(o.BuyerKind) && !string.IsNullOrWhiteSpace(o.BuyerId))
            .Where(lasQueTocan)
            .ToList();

        foreach (var orden in pendientes)
        {
            var (compra, _) = await LeerAsync(orden.PaymentSessionId, (orden.BuyerKind!, orden.BuyerId!), ct).ConfigureAwait(false);
            if (compra is not null && string.Equals(compra.Status, "Completed", StringComparison.Ordinal))
            {
                await _ledger.SaveAsync(orden with { Status = EventOrderStatus.Confirmed }, ct).ConfigureAwait(false);
            }
        }
    }

    private ResultadoDelArtefacto? NoDisponible()
        => _hayDestino && !string.IsNullOrWhiteSpace(Kind)
            ? null
            : new ResultadoDelArtefacto(StatusCodes.Status503ServiceUnavailable, "eventos.artefacto_no_disponible",
                "La compra por la puerta no está abierta en este sitio.");

    /// <summary>La orden de este lado de una saga y un comprador: nunca por la saga sola.</summary>
    private async Task<PersistedEventOrder?> OrdenDeAsync(string compraId, (string Kind, string Id) sujeto, CancellationToken ct)
        => (await _ledger.LoadAllAsync(ct).ConfigureAwait(false)).FirstOrDefault(o =>
            string.Equals(o.PaymentSessionId, compraId, StringComparison.Ordinal)
            && string.Equals(o.BuyerKind, sujeto.Kind, StringComparison.Ordinal)
            && string.Equals(o.BuyerId, sujeto.Id, StringComparison.Ordinal));

    private PersistedEventOrder Orden(
        PurchaseDto compra, (string Kind, string Id) sujeto, string? correo, string? nombre,
        List<PersistedEventUnit> unidades, PersistedEventOrder? previa)
        => new(
            OrderRef: previa?.OrderRef ?? NuevaOrden(),
            EventId: compra.EventId ?? string.Empty,
            PaymentSessionId: compra.Id,
            Total: compra.Total.Amount,
            Currency: compra.Total.Currency,
            Units: unidades,
            CreatedAt: previa?.CreatedAt ?? _ahora())
        {
            BuyerKind = sujeto.Kind,
            BuyerId = sujeto.Id,
            BuyerName = nombre?.Trim(),
            BuyerEmail = correo?.Trim(),
        };

    /// <summary>El comprador repetido tantas veces como entradas apartadas: el portador de todas.</summary>
    private static List<EventAttendeeInfo> Portador(PurchaseDto compra, string? correo, string? nombre)
    {
        var quien = new EventAttendeeInfo(
            string.IsNullOrWhiteSpace(nombre) ? correo ?? string.Empty : nombre.Trim(), correo ?? string.Empty, null);
        return Enumerable.Repeat(quien, (compra.Held ?? []).Sum(h => Math.Max(1, h.Quantity))).ToList();
    }

    /// <summary>La compra tal como la ve su comprador, o el motivo por el que no se pudo leer.</summary>
    private async Task<(PurchaseDto? Compra, ResultadoDelArtefacto? Fallo)> LeerAsync(
        string compraId, (string Kind, string Id) sujeto, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"v1/ticket-purchases/{Uri.EscapeDataString(compraId)}");
        req.Headers.TryAddWithoutValidation(Cabecera, $"{sujeto.Kind}:{sujeto.Id}");
        try
        {
            using var res = await _clientes.CreateClient(ClientName).SendAsync(req, ct).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                return (await res.Content.ReadFromJsonAsync<PurchaseDto>(Json, ct).ConfigureAwait(false), null);
            }
            if (res.StatusCode == HttpStatusCode.NotFound)
            {
                return (null, new ResultadoDelArtefacto(StatusCodes.Status404NotFound, "eventos.purchase_not_found",
                    "La compra no existe."));
            }

            var rechazo = await RechazoDelArbolDeServicios.LeerAsync(res, Json, ct).ConfigureAwait(false);
            _log.LogWarning("Eventos contestó {Estado} ({Codigo}) al leer una compra para su artefacto.",
                (int)res.StatusCode, rechazo?.Codigo ?? "-");
            return (null, new ResultadoDelArtefacto(StatusCodes.Status502BadGateway, "eventos.respuesta_invalida",
                "El orquestador no pudo contestar por la compra."));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _log.LogWarning("Eventos no respondió al leer una compra para su artefacto: {Motivo}", ex.Message);
            return (null, new ResultadoDelArtefacto(StatusCodes.Status503ServiceUnavailable, "eventos.orquestador_no_disponible",
                "El orquestador no está disponible.", Transitorio: true));
        }
    }
}
