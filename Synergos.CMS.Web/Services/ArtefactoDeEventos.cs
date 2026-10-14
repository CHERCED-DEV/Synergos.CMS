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

/// <summary>Una compra del miembro sin acuse —su comprador no leyó sus entradas— y en qué quedó en el orquestador.</summary>
/// <param name="Id">El id de la saga: la referencia que ve quien compra.</param>
/// <param name="Estado"><c>Running</c>, <c>Completed</c> o <see cref="ArtefactoDeEventos.SinLeer"/>.</param>
public sealed record CompraSinAcuse(string Id, string Estado);

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
    private readonly TimeSpan _presupuesto;

    /// <param name="hayDestino">Si el despliegue dice dónde vive el orquestador.</param>
    /// <param name="presupuesto">El techo de una reconciliación; por defecto <see cref="PresupuestoDeReconciliacion"/>.</param>
    public ArtefactoDeEventos(
        IHttpClientFactory clientes, EventTicketLedger ledger, IOptionsMonitor<PuertaSettings> puerta,
        ILogger<ArtefactoDeEventos> log, bool hayDestino, Func<DateTimeOffset>? ahora = null, TimeSpan? presupuesto = null)
    {
        _clientes = clientes;
        _ledger = ledger;
        _puerta = puerta;
        _log = log;
        _hayDestino = hayDestino;
        _ahora = ahora ?? (() => DateTimeOffset.UtcNow);
        _presupuesto = presupuesto ?? PresupuestoDeReconciliacion;
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

        // Volver a anotar reemplaza la lista: mientras no se cierre, el comprador puede corregirla. La
        // orden es UNA por saga y el registro la anota bajo el cerrojo de la saga: dos anotaciones a la
        // vez dejan la última lista, no dos órdenes, y una orden ya confirmada no vuelve a pendiente.
        var unidades = Emparejar(compra, asistentes);
        var orden = await _ledger.AnotarPendienteAsync(compra.Id,
            actual => actual is not null && !DelComprador(actual, sujeto) ? null : Orden(compra, sujeto, correo, nombre, unidades, actual),
            ct).ConfigureAwait(false);
        if (orden is null)
        {
            return new ResultadoDelArtefacto(StatusCodes.Status409Conflict, "eventos.compra_ya_cerrada",
                "Las entradas ya se emitieron.", "Completed");
        }
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
            // La confirmó una reconciliación sin que él la viera: ésta es su lectura, el acuse.
            await AcusarAsync(previa, ct).ConfigureAwait(false);
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

        // Sin asistentes anotados, el comprador es el portador de todas: nunca hay cobro sin entradas. Lo
        // anotado se relee bajo el cerrojo de la saga, así que dos peticiones a la vez confirman UNA orden
        // —la que haya, con sus asistentes— y la segunda recibe esa misma.
        var confirmada = await _ledger.ConfirmarAsync(compra.Id,
            actual => actual is not null && DelComprador(actual, sujeto)
                ? actual
                : Orden(compra, sujeto, correo, nombre, Emparejar(compra, Portador(compra, correo, nombre)), null),
            ct).ConfigureAwait(false);
        if (!DelComprador(confirmada, sujeto))
        {
            return new ResultadoDelArtefacto(StatusCodes.Status409Conflict, "eventos.compra_ya_cerrada",
                "Esta compra ya emitió sus entradas con otra orden.", "Completed");
        }
        // La pudo confirmar entre medias una reconciliación: quien compró la está leyendo ahora.
        await AcusarAsync(confirmada, ct).ConfigureAwait(false);
        return new ResultadoDelArtefacto(StatusCodes.Status200OK, Entradas: _ledger.ConfirmationOf(confirmada));
    }

    /// <summary>
    /// Quien compró está leyendo en qué quedó su compra: deja de estar sin acuse (ADR 0140, el cupón
    /// endurecido). Es la única lectura que da el acuse; las reconciliaciones no.
    /// </summary>
    private async Task AcusarAsync(PersistedEventOrder orden, CancellationToken ct)
    {
        if (orden.Unacknowledged)
        {
            await _ledger.AcusarAsync(orden.PaymentSessionId, orden.OrderRef, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Cuántas órdenes pendientes mira, como mucho, una reconciliación: las más recientes primero.</summary>
    public const int TopeDeReconciliacion = 10;

    /// <summary>Cuánto puede tardar, como mucho, una reconciliación entera: la espera de quien lee la lista.</summary>
    public static readonly TimeSpan PresupuestoDeReconciliacion = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Confirma de este lado las compras PENDIENTES que <paramref name="lasQueTocan"/> elige y que el
    /// orquestador ya completó, y descarta las que no se completarán. Lo confirmado no se mira: no toca la red.
    /// </summary>
    /// <remarks>
    /// <para>Es lo que cubre al comprador que cerró la compra y no volvió a pedir sus entradas: «mis entradas»
    /// y la consola del organizador las ven igual.</para>
    ///
    /// <para><b>Corre en la ruta de LECTURA, y por eso tiene techo</b> (ADR 0140 F3): mira como mucho
    /// <see cref="TopeDeReconciliacion"/> órdenes, de a varias a la vez, y deja de esperar al orquestador a
    /// los <see cref="PresupuestoDeReconciliacion"/>: lo que falte se mira en la próxima lectura. Antes las
    /// consultaba en serie con el techo de diez segundos de cada una, y con el orquestador colgado la lista
    /// no llegaba nunca, aunque las entradas confirmadas estaban en el registro.</para>
    ///
    /// <para><b>Lo que no se completará deja de mirarse</b>: una compra deshecha (<c>Compensated</c>,
    /// <c>CompensationFailed</c>), una que el orquestador no tiene para ese comprador (404), o la gemela de
    /// una orden que ya confirmó su saga, quedan <see cref="EventOrderStatus.Discarded"/>. Sin eso se
    /// volvían a consultar en cada carga, para siempre.</para>
    /// </remarks>
    public async Task ReconciliarAsync(Func<PersistedEventOrder, bool> lasQueTocan, CancellationToken ct)
    {
        if (!_hayDestino) return;

        var pendientes = (await _ledger.LoadAllAsync(ct).ConfigureAwait(false))
            .Where(o => o.Status == EventOrderStatus.Pending
                        && !string.IsNullOrWhiteSpace(o.BuyerKind) && !string.IsNullOrWhiteSpace(o.BuyerId))
            .Where(lasQueTocan)
            .OrderByDescending(o => o.CreatedAt)
            .Take(TopeDeReconciliacion)
            .ToList();
        if (pendientes.Count == 0) return;

        using var presupuesto = CancellationTokenSource.CreateLinkedTokenSource(ct);
        presupuesto.CancelAfter(_presupuesto);
        try
        {
            await Parallel.ForEachAsync(pendientes,
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = presupuesto.Token },
                async (orden, t) => await ReconciliarUnaAsync(orden, t).ConfigureAwait(false)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning(
                "La reconciliación de compras pendientes llegó a su techo de {Techo}: lo que falta se mira en la próxima lectura.",
                _presupuesto);
        }
    }

    private async Task ReconciliarUnaAsync(PersistedEventOrder orden, CancellationToken ct)
    {
        var (compra, fallo) = await LeerAsync(orden.PaymentSessionId, (orden.BuyerKind!, orden.BuyerId!), ct).ConfigureAwait(false);
        if (compra is { Status: "Completed" })
        {
            // Por el registro, que confirma UNA orden por saga: la gemela de una saga ya confirmada no
            // emite las mismas butacas otra vez, y no se vuelve a mirar. Y SIN ACUSE: la confirma una
            // lectura que no es la de su comprador —la consola del organizador, o «mis entradas», que sólo
            // enseña las entradas cuyo portador es quien pide—, así que la lectura de las compras sin acuse
            // la sigue informando hasta que él lea las suyas.
            var confirmada = await _ledger.ConfirmarAsync(orden.PaymentSessionId,
                actual => (actual is not null && actual.OrderRef == orden.OrderRef ? actual : orden) with { Unacknowledged = true },
                ct).ConfigureAwait(false);
            if (!string.Equals(confirmada.OrderRef, orden.OrderRef, StringComparison.Ordinal))
            {
                _log.LogWarning("La orden {Orden} es gemela de la que ya confirmó su compra: se descarta.", orden.OrderRef);
                await _ledger.DescartarAsync(orden.PaymentSessionId, orden.OrderRef, ct).ConfigureAwait(false);
            }
            return;
        }

        if (compra is { Status: "Compensated" or "CompensationFailed" } || fallo is { Codigo: "eventos.purchase_not_found" })
        {
            await _ledger.DescartarAsync(orden.PaymentSessionId, orden.OrderRef, ct).ConfigureAwait(false);
        }
    }

    /// <summary>El estado de una compra sin acuse que no se pudo leer: ilegible, caído o fuera del techo.</summary>
    public const string SinLeer = "Unknown";

    /// <summary>
    /// Las compras del miembro para un evento que no tienen acuse —su orden sigue
    /// <see cref="EventOrderStatus.Pending"/>, o la confirmó una reconciliación y su comprador no la ha
    /// leído (<see cref="PersistedEventOrder.Unacknowledged"/>)— y que todavía cuentan: en curso, cobradas,
    /// o sin leer. No emite nada.
    /// </summary>
    /// <remarks>
    /// <para><b>Para qué</b> (ADR 0140, D2, fase D). Antes de abrir una compra con una llave NUEVA, el
    /// elemento pregunta si quien compra ya tiene otra del mismo evento sin acuse: la que se cobró y cuya
    /// respuesta se perdió, o la que sigue en curso. La guarda del navegador vive en su almacenamiento;
    /// ésta la ve desde otro dispositivo, con los datos del sitio borrados o pasado su plazo, porque el
    /// acuse lo sabe este lado, no el orquestador. <b>Y el acuse no es <c>Confirmed</c></b>: lo da sólo
    /// la lectura de sus entradas por su comprador (<see cref="EntradasAsync"/>); la consola del
    /// organizador y «mis entradas» confirman sin que él vea nada, y la confirmada así se sigue informando
    /// como <c>Completed</c>, sin preguntar al orquestador. Es consultiva: comprar dos veces a propósito
    /// sigue igual, porque la primera ya tiene acuse.</para>
    ///
    /// <para><b>Siempre del miembro</b>: se filtra por su sujeto y cada lectura al orquestador lo lleva,
    /// así que una orden ajena ni se lista ni se consulta.</para>
    ///
    /// <para><b>Con el techo de la reconciliación</b>: mira como mucho <see cref="TopeDeReconciliacion"/>
    /// órdenes, las más recientes primero, y deja de esperar a los <see cref="PresupuestoDeReconciliacion"/>.
    /// Lo que no alcanzó a leer, o no se pudo leer, sale como <see cref="SinLeer"/>: no saber no es
    /// «no hay». Lo deshecho o inexistente se descarta como en la reconciliación, y lo que se está
    /// deshaciendo no se informa: no se cobrará.</para>
    ///
    /// <para><b>No confirma ni emite</b>: una cobrada sin acuse se informa y sus entradas salen por
    /// <c>…/compras/{id}/entradas</c>, con su portador.</para>
    /// </remarks>
    public async Task<(IReadOnlyList<CompraSinAcuse> Compras, ResultadoDelArtefacto? Fallo)> PendientesAsync(
        Guid miembro, string? eventId, CancellationToken ct)
    {
        if (NoDisponible() is { } cerrado) return ([], cerrado);
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return ([], new ResultadoDelArtefacto(StatusCodes.Status400BadRequest, "eventos.evento_requerido",
                "Falta el evento de las compras."));
        }

        var sujeto = (Kind: Kind!, Id: miembro.ToString("n"));
        var evento = eventId.Trim();
        var delMiembro = (await _ledger.LoadAllAsync(ct).ConfigureAwait(false))
            .Where(o => DelComprador(o, sujeto) && string.Equals(o.EventId, evento, StringComparison.Ordinal))
            .ToList();
        var pendientes = delMiembro
            .Where(o => o.Status == EventOrderStatus.Pending)
            .OrderByDescending(o => o.CreatedAt)
            .ToList();
        // Cobradas que confirmó una reconciliación y que su comprador no ha leído: no hace falta preguntar.
        var confirmadasSinAcuse = delMiembro
            .Where(o => o.Status == EventOrderStatus.Confirmed && o.Unacknowledged)
            .ToList();
        if (pendientes.Count == 0 && confirmadasSinAcuse.Count == 0) return ([], null);

        var leidas = new System.Collections.Concurrent.ConcurrentDictionary<string, string?>(StringComparer.Ordinal);
        using var presupuesto = CancellationTokenSource.CreateLinkedTokenSource(ct);
        presupuesto.CancelAfter(_presupuesto);
        try
        {
            await Parallel.ForEachAsync(pendientes.Take(TopeDeReconciliacion),
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = presupuesto.Token },
                async (orden, t) => leidas[orden.OrderRef] = await EnQueQuedoAsync(orden, sujeto, t).ConfigureAwait(false))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning(
                "La lectura de compras sin acuse llegó a su techo de {Techo}: lo que falta sale sin leer.", _presupuesto);
        }

        var compras = pendientes
            .Select(o => (o.CreatedAt, o.PaymentSessionId, Estado: leidas.TryGetValue(o.OrderRef, out var e) ? e : SinLeer))
            .Concat(confirmadasSinAcuse.Select(o => (o.CreatedAt, o.PaymentSessionId, Estado: (string?)"Completed")))
            .Where(c => c.Estado is not null)
            .OrderByDescending(c => c.CreatedAt)
            .DistinctBy(c => c.PaymentSessionId, StringComparer.Ordinal)
            .Select(c => new CompraSinAcuse(c.PaymentSessionId, c.Estado!))
            .ToList();
        return (compras, null);
    }

    /// <summary>
    /// En qué quedó una compra sin acuse, o nulo si ya no cuenta: deshecha o inexistente (se descarta, como
    /// en <see cref="ReconciliarUnaAsync"/>) o deshaciéndose.
    /// </summary>
    private async Task<string?> EnQueQuedoAsync(PersistedEventOrder orden, (string Kind, string Id) sujeto, CancellationToken ct)
    {
        var (compra, fallo) = await LeerAsync(orden.PaymentSessionId, sujeto, ct).ConfigureAwait(false);
        var estado = compra?.Status;
        if (estado is "Running" or "Completed") return estado;
        if (estado is "Compensating") return null;

        if (estado is "Compensated" or "CompensationFailed" || fallo is { Codigo: "eventos.purchase_not_found" })
        {
            await _ledger.DescartarAsync(orden.PaymentSessionId, orden.OrderRef, ct).ConfigureAwait(false);
            return null;
        }
        return SinLeer;
    }

    private ResultadoDelArtefacto? NoDisponible()
        => _hayDestino && !string.IsNullOrWhiteSpace(Kind)
            ? null
            : new ResultadoDelArtefacto(StatusCodes.Status503ServiceUnavailable, "eventos.artefacto_no_disponible",
                "La compra por la puerta no está abierta en este sitio.");

    /// <summary>La orden de este lado de una saga y un comprador: nunca por la saga sola.</summary>
    private async Task<PersistedEventOrder?> OrdenDeAsync(string compraId, (string Kind, string Id) sujeto, CancellationToken ct)
        => (await _ledger.LoadAllAsync(ct).ConfigureAwait(false)).FirstOrDefault(o =>
            string.Equals(o.PaymentSessionId, compraId, StringComparison.Ordinal) && DelComprador(o, sujeto));

    private static bool DelComprador(PersistedEventOrder orden, (string Kind, string Id) sujeto)
        => string.Equals(orden.BuyerKind, sujeto.Kind, StringComparison.Ordinal)
           && string.Equals(orden.BuyerId, sujeto.Id, StringComparison.Ordinal);

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
            BuyerName = string.IsNullOrWhiteSpace(nombre) ? null : nombre.Trim(),
            BuyerEmail = correo?.Trim(),
            ViaGate = true,
        };

    /// <summary>El comprador repetido tantas veces como entradas apartadas: el portador de todas.</summary>
    private static List<EventAttendeeInfo> Portador(PurchaseDto compra, string? correo, string? nombre)
    {
        // Sin nombre, la entrada va sin nombre: el correo no es el nombre de nadie, y sale impreso en ella.
        var quien = new EventAttendeeInfo(nombre?.Trim() ?? string.Empty, correo ?? string.Empty, null);
        return Enumerable.Repeat(quien, (compra.Held ?? []).Sum(h => Math.Max(1, h.Quantity))).ToList();
    }

    /// <summary>La compra tal como la ve su comprador, o el motivo por el que no se pudo leer.</summary>
    private async Task<(PurchaseDto? Compra, ResultadoDelArtefacto? Fallo)> LeerAsync(
        string compraId, (string Kind, string Id) sujeto, CancellationToken ct)
    {
        // Un segmento de punto no es una compra: escaparlo no lo cambia y movería la lectura a otra ruta.
        if (Puerta.ReenvioDeLaPuerta.EsSegmentoDePunto(compraId))
        {
            return (null, new ResultadoDelArtefacto(StatusCodes.Status404NotFound, "eventos.purchase_not_found", "La compra no existe."));
        }

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
