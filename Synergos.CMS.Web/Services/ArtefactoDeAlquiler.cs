using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>Cómo terminó pedir el contrato de un alquiler de la puerta: el contrato, o el motivo con su código.</summary>
/// <param name="Estado">El estado HTTP.</param>
/// <param name="Contrato">El contrato emitido (o el que ya estaba), si salió bien.</param>
/// <param name="Codigo">El código del motivo (<c>alquiler.*</c>), nulo si salió bien.</param>
/// <param name="Mensaje">Para el log y la consola, no para la persona.</param>
/// <param name="Transitorio">Si reintentar puede salir distinto.</param>
public sealed record ContratoDeLaPuerta(
    int Estado, RentalAgreement? Contrato = null, string? Codigo = null, string? Mensaje = null, bool Transitorio = false);

/// <summary>
/// El artefacto de un alquiler hecho por la puerta (línea 005, T024; research R10): el contrato sellado. El orquestador
/// mueve unidades y plata; el contrato, su sello y su registro son del CMS.
/// </summary>
/// <remarks>
/// <para><b>Se emite al LEERLO</b>, como las entradas de Eventos: el elemento lo pide en cuanto <c>cerrar</c> contesta, y
/// el enlace del aviso lo abre. Si la saga está <c>Completed</c> y es del miembro, se arma con lo que contestó el
/// orquestador —equipo, unidades, ventana, total y garantía, la salida del flujo— y se sella y anota con
/// <c>Camino = puerta</c>, así lo posterior (declarar, confirmar, cancelar) va a ese orquestador.</para>
///
/// <para><b>Siempre del miembro.</b> Cada consulta lleva el sujeto de la sesión —el mismo que puso la puerta al abrir—,
/// así que uno ajeno es un 404 del orquestador. <b>Una sola vez</b>: lo emitido se lee del registro, y dos lecturas a la
/// vez del mismo alquiler emiten UN contrato.</para>
///
/// <para><b>«Mis alquileres» reconcilia</b> (FR-020): los alquileres de la puerta del miembro que el orquestador tiene
/// completados y el registro no, se emiten al listar. Corre en la ruta de lectura, así que tiene techo —como mucho
/// <see cref="TopeDeReconciliacion"/> y <see cref="PresupuestoDeReconciliacion"/>—: lo que falte sale en la próxima. La
/// lista es la genérica del host, «listar» (<c>GET /v1/flujos/alquiler.renta?estado=Completed</c>, NN-7), por páginas y
/// siempre del sujeto de la sesión.</para>
/// </remarks>
public sealed class ArtefactoDeAlquiler
{
    /// <summary>Cliente nombrado hacia el orquestador, por la pieza del árbol.</summary>
    public const string ClientName = "synergos-bff-alquiler-artefacto";

    /// <summary>El flujo de la puerta cuyos alquileres atiende: de su configuración sale el <c>Kind</c> del sujeto.</summary>
    public const string Flujo = "alquiler.renta";

    /// <summary>Cuántos alquileres emite, como mucho, una reconciliación: los más recientes primero.</summary>
    public const int TopeDeReconciliacion = 10;

    /// <summary>Cuánto puede tardar, como mucho, una reconciliación entera: la espera de quien lee la lista.</summary>
    public static readonly TimeSpan PresupuestoDeReconciliacion = TimeSpan.FromSeconds(3);

    /// <summary>Cuántas sagas pide cada página de «listar».</summary>
    public const int TamanoDePagina = 50;

    /// <summary>Cuántas páginas lee, como mucho, una reconciliación: el techo si el presupuesto no llegara antes.</summary>
    public const int PaginasDeReconciliacion = 10;

    private const string Cabecera = "X-Synergos-Sujeto";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _clientes;
    private readonly EquipmentAgreementLedger _ledger;
    private readonly EquipmentAgreementIssuer _emisor;
    private readonly IEquipmentCatalogProvider _catalogo;
    private readonly IOptionsMonitor<PuertaSettings> _puerta;
    private readonly ILogger<ArtefactoDeAlquiler> _log;
    private readonly bool _hayDestino;
    private readonly TimeSpan _presupuesto;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _cerrojos = new(StringComparer.Ordinal);

    /// <summary>Construye el artefacto.</summary>
    /// <param name="clientes">De dónde sale el cliente hacia el orquestador.</param>
    /// <param name="ledger">El registro de contratos.</param>
    /// <param name="emisor">Quien sella y anota.</param>
    /// <param name="catalogo">Cómo se llama el equipo, para que el papel se lea.</param>
    /// <param name="puerta">La configuración de la puerta: el <c>Kind</c> con que abrió el alquiler.</param>
    /// <param name="log">Dónde se dice lo que no se pudo.</param>
    /// <param name="hayDestino">Si el despliegue dice dónde vive el orquestador.</param>
    /// <param name="presupuesto">El techo de una reconciliación; por defecto <see cref="PresupuestoDeReconciliacion"/>.</param>
    public ArtefactoDeAlquiler(
        IHttpClientFactory clientes, EquipmentAgreementLedger ledger, EquipmentAgreementIssuer emisor,
        IEquipmentCatalogProvider catalogo, IOptionsMonitor<PuertaSettings> puerta, ILogger<ArtefactoDeAlquiler> log,
        bool hayDestino, TimeSpan? presupuesto = null)
    {
        _clientes = clientes ?? throw new ArgumentNullException(nameof(clientes));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _emisor = emisor ?? throw new ArgumentNullException(nameof(emisor));
        _catalogo = catalogo ?? throw new ArgumentNullException(nameof(catalogo));
        _puerta = puerta ?? throw new ArgumentNullException(nameof(puerta));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _hayDestino = hayDestino;
        _presupuesto = presupuesto ?? PresupuestoDeReconciliacion;
    }

    /// <summary>El <c>Kind</c> con que la puerta nombra al inquilino, o nulo si el flujo no está abierto.</summary>
    private string? Kind => _puerta.CurrentValue.Flujos.TryGetValue(Flujo, out var f) ? f.SujetoKind : null;

    /// <summary>Si este sitio alquila por la puerta y sabe dónde vive el orquestador.</summary>
    public bool Disponible => _hayDestino && !string.IsNullOrWhiteSpace(Kind);

    /// <summary>El contrato de un alquiler de la puerta del miembro: lo emite al leerlo, una sola vez.</summary>
    /// <param name="rentalId">La saga de la renta.</param>
    /// <param name="miembro">Quién está en sesión.</param>
    /// <param name="ct">Cancelación del request.</param>
    /// <returns>El contrato, o por qué no hay.</returns>
    public async Task<ContratoDeLaPuerta> ContratoAsync(string rentalId, Guid miembro, CancellationToken ct)
    {
        if (!Disponible || string.IsNullOrWhiteSpace(rentalId) || Puerta.ReenvioDeLaPuerta.EsSegmentoDePunto(rentalId))
        {
            return NoExiste();
        }

        var cerrojo = _cerrojos.GetOrAdd(rentalId, _ => new SemaphoreSlim(1, 1));
        await cerrojo.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Lo pudo emitir otra lectura mientras ésta esperaba: se devuelve ése, nunca un segundo contrato.
            if (await _ledger.GetAsync(rentalId, ct).ConfigureAwait(false) is { } emitido)
            {
                return string.Equals(emitido.RenterId, miembro.ToString("N"), StringComparison.Ordinal)
                    ? new ContratoDeLaPuerta(StatusCodes.Status200OK, emitido)
                    : NoExiste();
            }

            var (renta, fallo) = await LeerAsync(rentalId, Sujeto(miembro), ct).ConfigureAwait(false);
            if (fallo is not null)
            {
                return fallo;
            }

            if (!string.Equals(renta!.Estado, "Completed", StringComparison.Ordinal))
            {
                return string.Equals(renta.Estado, "Running", StringComparison.Ordinal)
                    ? new ContratoDeLaPuerta(StatusCodes.Status409Conflict, Codigo: "alquiler.rental_in_progress",
                        Mensaje: "El alquiler todavía no se confirmó: el contrato sale después de cobrar.")
                    : new ContratoDeLaPuerta(StatusCodes.Status409Conflict, Codigo: "alquiler.rental_not_completed",
                        Mensaje: "El alquiler no se completó: no hay contrato que emitir.");
            }

            if (await Borrador(renta, miembro, ct).ConfigureAwait(false) is not { } borrador)
            {
                _log.LogWarning("Alquiler: la renta {Id} se completó y su salida no trae lo que el contrato lleva.", rentalId);
                return new ContratoDeLaPuerta(StatusCodes.Status502BadGateway, Codigo: "alquiler.respuesta_invalida",
                    Mensaje: "El orquestador no contestó lo que el contrato necesita.");
            }

            return new ContratoDeLaPuerta(StatusCodes.Status200OK, await _emisor.EmitirAsync(borrador, ct).ConfigureAwait(false));
        }
        finally
        {
            cerrojo.Release();
        }
    }

    /// <summary>
    /// Emite los contratos de los alquileres de la puerta del miembro que el orquestador completó y el registro no tiene.
    /// </summary>
    /// <param name="miembro">Quién está en sesión.</param>
    /// <param name="ct">Cancelación del request.</param>
    /// <returns>Una tarea que termina cuando reconcilió lo que el techo dejó.</returns>
    public async Task ReconciliarAsync(Guid miembro, CancellationToken ct)
    {
        if (!Disponible)
        {
            return;
        }

        using var presupuesto = CancellationTokenSource.CreateLinkedTokenSource(ct);
        presupuesto.CancelAfter(_presupuesto);
        try
        {
            var tiene = (await _ledger.ListForAsync(miembro.ToString("N"), presupuesto.Token).ConfigureAwait(false))
                .Select(a => a.RentalId)
                .ToHashSet(StringComparer.Ordinal);
            var faltan = await FaltanAsync(Sujeto(miembro), tiene, presupuesto.Token).ConfigureAwait(false);

            await Parallel.ForEachAsync(faltan,
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = presupuesto.Token },
                async (a, t) => await ContratoAsync(a.Id, miembro, t).ConfigureAwait(false)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning(
                "La reconciliación de alquileres de la puerta llegó a su techo de {Techo}: lo que falta sale en la próxima lectura.",
                _presupuesto);
        }
        catch (HttpRequestException ex)
        {
            // La bandeja se sirve igual del registro: el orquestador caído no deja a nadie sin sus contratos emitidos.
            _log.LogWarning("Alquiler: el orquestador no respondió al reconciliar «mis alquileres»: {Motivo}", ex.Message);
        }
    }

    private string Sujeto(Guid miembro) => $"{Kind}:{miembro:N}";

    private static ContratoDeLaPuerta NoExiste()
        => new(StatusCodes.Status404NotFound, Codigo: "alquiler.rental_not_found", Mensaje: "Ese alquiler no existe.");

    /// <summary>El contrato sin sello, con lo que contestó el orquestador; null si falta algo de lo que lleva.</summary>
    private async Task<RentalAgreement?> Borrador(RentaDto renta, Guid miembro, CancellationToken ct)
    {
        if (renta.Datos is not { Total: { } total, Garantia: { } garantia, Ventana: { } ventana, Cantidad: > 0 } datos
            || string.IsNullOrWhiteSpace(datos.Equipo))
        {
            return null;
        }

        var equipo = await _catalogo.GetAsync(datos.Equipo, ct).ConfigureAwait(false);
        return new RentalAgreement(
            RentalId: renta.Id,
            RenterId: miembro.ToString("N"),
            EquipmentId: datos.Equipo,
            EquipmentName: equipo?.Name ?? datos.Equipo,
            Quantity: datos.Cantidad.Value,
            Start: DateOnly.FromDateTime(ventana.Start.UtcDateTime),
            End: DateOnly.FromDateTime(ventana.End.UtcDateTime),
            RentalTotal: total.Amount,
            DepositHeld: garantia.Amount,
            Currency: total.Currency,
            IssuedUtc: default,
            Seal: string.Empty,
            Camino: CaminoDeCompra.Puerta);
    }

    /// <summary>La renta tal como la ve su inquilino, o el motivo por el que no se pudo leer.</summary>
    private async Task<(RentaDto? Renta, ContratoDeLaPuerta? Fallo)> LeerAsync(string id, string sujeto, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"v1/flujos/{Flujo}/{Uri.EscapeDataString(id)}");
        req.Headers.TryAddWithoutValidation(Cabecera, sujeto);
        try
        {
            using var res = await _clientes.CreateClient(ClientName).SendAsync(req, ct).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                return (await res.Content.ReadFromJsonAsync<RentaDto>(Json, ct).ConfigureAwait(false), null);
            }
            if (res.StatusCode == HttpStatusCode.NotFound)
            {
                return (null, NoExiste());
            }

            var rechazo = await RechazoDelArbolDeServicios.LeerAsync(res, Json, ct).ConfigureAwait(false);
            _log.LogWarning("Alquiler contestó {Estado} ({Codigo}) al leer una renta para su contrato.",
                (int)res.StatusCode, rechazo?.Codigo ?? "-");
            return (null, new ContratoDeLaPuerta(StatusCodes.Status502BadGateway, Codigo: "alquiler.respuesta_invalida",
                Mensaje: "El orquestador no pudo contestar por el alquiler."));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _log.LogWarning("Alquiler no respondió al leer una renta para su contrato: {Motivo}", ex.Message);
            return (null, new ContratoDeLaPuerta(StatusCodes.Status503ServiceUnavailable,
                Codigo: "alquiler.orchestrator_unavailable", Mensaje: "El orquestador no está disponible.", Transitorio: true));
        }
    }

    /// <summary>
    /// Las rentas completadas del miembro que el registro no tiene, de la más nueva a la más vieja y hasta
    /// <see cref="TopeDeReconciliacion"/>, leyendo «listar» página a página.
    /// </summary>
    private async Task<IReadOnlyList<RentaDto>> FaltanAsync(string sujeto, IReadOnlySet<string> tiene, CancellationToken ct)
    {
        var faltan = new List<RentaDto>(TopeDeReconciliacion);
        for (var pagina = 0; pagina < PaginasDeReconciliacion && faltan.Count < TopeDeReconciliacion; pagina++)
        {
            var ruta = $"v1/flujos/{Flujo}?estado=Completed&offset={pagina * TamanoDePagina}&limit={TamanoDePagina}";
            using var req = new HttpRequestMessage(HttpMethod.Get, ruta);
            req.Headers.TryAddWithoutValidation(Cabecera, sujeto);
            using var res = await _clientes.CreateClient(ClientName).SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                _log.LogWarning("Alquiler contestó {Estado} al listar los alquileres de la puerta de un miembro.", (int)res.StatusCode);
                break;
            }

            var leida = await res.Content.ReadFromJsonAsync<PaginaDto>(Json, ct).ConfigureAwait(false);
            faltan.AddRange((leida?.Items ?? [])
                .Where(r => string.Equals(r.Estado, "Completed", StringComparison.Ordinal) && !tiene.Contains(r.Id))
                .Take(TopeDeReconciliacion - faltan.Count));
            if (leida is not { HasMore: true }) break;
        }

        return faltan;
    }

    // Lo que se lee del orquestador: sólo los campos que se usan, con los nombres de su JSON. Los cruza
    // ConsumidorCmsDeBffAlquilerTests contra el contrato comiteado de Bff.Alquiler.

    internal sealed record MoneyDto(decimal Amount, string Currency);

    internal sealed record VentanaDto(DateTimeOffset Start, DateTimeOffset End);

    /// <param name="Equipo">El slug del equipo, de la entrada del flujo.</param>
    /// <param name="Cantidad">Cuántas unidades.</param>
    internal sealed record DatosDto(MoneyDto? Total, MoneyDto? Garantia, VentanaDto? Ventana, string? Equipo, int? Cantidad);

    internal sealed record RentaDto(string Id, string Estado, DatosDto? Datos);

    /// <summary>Una página de «listar»: las rentas del dueño y si hay más.</summary>
    internal sealed record PaginaDto(IReadOnlyList<RentaDto>? Items, bool HasMore);
}
