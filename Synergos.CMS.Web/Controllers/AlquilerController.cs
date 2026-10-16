using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// El borde del vertical Alquiler (#147): el catálogo de equipos, la reserva con su garantía
/// retenida, la devolución y el comprobante.
/// </summary>
/// <remarks>
/// <para><b>El emisor del contrato lo llama ESTE borde y no el seam</b>, y es lo que mantiene el
/// artefacto fuera del eje 2 (doc 12 §3.2, #153): con el motor en proceso o con
/// <c>Synergos.Bff.Alquiler</c> detrás, el comprobante se emite igual y se anota en el mismo
/// registro. Eventos aprendió lo contrario por las malas — cambiar por dónde se compra dejó la
/// cara de organizador leyendo un almacén vacío.</para>
///
/// <para><b>Quien alquila viaja como SEUDÓNIMO</b>: lo que este borde mande queda escrito en el
/// disco del orquestador, y un correo ahí es un correo fuera de esta máquina (#47).</para>
///
/// <para><b>Las bandejas se LEEN del registro local</b>, así que con el orquestador caído quien
/// alquiló sigue viendo su contrato y cuánto se le retuvo. Lo que se para es reservar, devolver
/// y cancelar — y no se dan por hechos en silencio.</para>
/// </remarks>
[ApiController]
[Route("api/alquiler")]
public sealed class AlquilerController : ControllerBase
{
    private readonly IEquipmentCatalogProvider _catalog;
    private readonly IEquipmentRentalService _rentals;
    private readonly IAlquilerDeLaPuerta _puerta;
    private readonly EquipmentAgreementIssuer _issuer;
    private readonly EquipmentAgreementLedger _ledger;
    private readonly ArtefactoDeAlquiler _artefacto;
    private readonly IMemberAccessGate _gate;
    private readonly ILogger<AlquilerController> _log;

    /// <summary>Construye el borde.</summary>
    /// <param name="catalog">El eje 1.</param>
    /// <param name="rentals">El eje 2, por la ruta directa.</param>
    /// <param name="puerta">Lo que pasa después del cierre con un alquiler que salió por la puerta.</param>
    /// <param name="issuer">El emisor del contrato (eje 3), fuera del seam.</param>
    /// <param name="ledger">El registro de contratos, que es el modelo de lectura.</param>
    /// <param name="artefacto">El contrato de un alquiler de la puerta, que se emite al leerlo (T024).</param>
    /// <param name="gate">Quién está en sesión.</param>
    /// <param name="log">Para decir qué se rechazó.</param>
    public AlquilerController(
        IEquipmentCatalogProvider catalog,
        IEquipmentRentalService rentals,
        IAlquilerDeLaPuerta puerta,
        EquipmentAgreementIssuer issuer,
        EquipmentAgreementLedger ledger,
        ArtefactoDeAlquiler artefacto,
        IMemberAccessGate gate,
        ILogger<AlquilerController> log)
    {
        _catalog = catalog;
        _rentals = rentals;
        _puerta = puerta;
        _issuer = issuer;
        _ledger = ledger;
        _artefacto = artefacto;
        _gate = gate;
        _log = log;
    }

    // ── EJE 1 · el catálogo ─────────────────────────────────────────────────

    /// <summary>Los equipos publicados.</summary>
    /// <param name="category">Familia por la que filtrar; vacío los trae todos.</param>
    /// <param name="ct">Cancelación del request.</param>
    /// <returns>Las fichas de catálogo.</returns>
    [HttpGet("equipment")]
    public async Task<ActionResult<IReadOnlyList<EquipmentCardDto>>> Equipment(
        [FromQuery] string? category, CancellationToken ct)
    {
        var equipos = await _catalog.ListAsync(category, ct).ConfigureAwait(false);
        return Ok(equipos.Select(EquipmentCardDto.From).ToList());
    }

    /// <summary>La ficha de un equipo.</summary>
    /// <param name="equipmentId">Su slug.</param>
    /// <param name="ct">Cancelación del request.</param>
    /// <returns>La ficha, o 404.</returns>
    [HttpGet("equipment/{equipmentId}")]
    public async Task<ActionResult<EquipmentDetailDto>> EquipmentDetail(string equipmentId, CancellationToken ct)
    {
        var equipo = await _catalog.GetAsync(equipmentId, ct).ConfigureAwait(false);
        return equipo is null ? NotFound() : Ok(EquipmentDetailDto.From(equipo));
    }

    // ── EJE 2 · la transacción ──────────────────────────────────────────────

    /// <summary>Cuánto costaría, sin comprometer nada.</summary>
    /// <param name="body">Qué, cuántos y cuándo.</param>
    /// <param name="ct">Cancelación del request.</param>
    /// <returns>La cotización, o 404 si el equipo no existe.</returns>
    [HttpPost("quote")]
    public async Task<ActionResult<QuoteDto>> Quote([FromBody] QuoteRequest body, CancellationToken ct)
    {
        if (!TryLeer(body?.EquipmentId, body?.Quantity, body?.Start, body?.End, out var pedido, out var porQue))
        {
            return Problema(StatusCodes.Status400BadRequest, "alquiler.bad_request", porQue);
        }

        var quote = await _rentals.QuoteAsync(pedido, ct).ConfigureAwait(false);
        return quote is null ? NotFound() : Ok(QuoteDto.From(quote));
    }

    /// <summary>Aparta la ventana, retiene la garantía y emite el contrato.</summary>
    /// <param name="body">Qué, cuántos y cuándo.</param>
    /// <param name="ct">Cancelación del request.</param>
    /// <returns>El alquiler con su comprobante, o el rechazo con su código.</returns>
    [HttpPost("rentals")]
    public async Task<ActionResult<RentalDto>> Reserve([FromBody] ReserveRequest body, CancellationToken ct)
    {
        if (!TryLeer(body?.EquipmentId, body?.Quantity, body?.Start, body?.End, out var pedido, out var porQue))
        {
            return Problema(StatusCodes.Status400BadRequest, "alquiler.bad_request", porQue);
        }

        if (string.IsNullOrWhiteSpace(body!.IdempotencyKey))
        {
            return Problema(StatusCodes.Status400BadRequest, "alquiler.idempotency_key_required",
                "Reservar retiene plata: sin llave, un reintento la retiene dos veces.");
        }

        var resultado = await _rentals
            .ReserveAsync(pedido, body.IdempotencyKey, ct).ConfigureAwait(false);

        if (resultado.Rental is null)
        {
            return Rechazar(resultado, "reservar");
        }

        // El contrato se emite acá y no dentro del seam: así lo comparten el motor en proceso y
        // el orquestador, que es la invariante del eje 3.
        var equipo = await _catalog.GetAsync(resultado.Rental.EquipmentId, ct).ConfigureAwait(false);
        var contrato = await _issuer
            .IssueAsync(resultado.Rental, equipo?.Name ?? resultado.Rental.EquipmentId, ct)
            .ConfigureAwait(false);

        return Ok(RentalDto.From(resultado.Rental, contrato, await _issuer.ComprobarAsync(contrato, ct).ConfigureAwait(false)));
    }

    /// <summary>
    /// El inquilino declara que devolvió el equipo (decisión 5 del lote 1, #203): la garantía SIGUE retenida
    /// hasta que el personal confirme.
    /// </summary>
    /// <param name="rentalId">Cuál alquiler.</param>
    /// <param name="body">La llave de la operación.</param>
    /// <param name="ct">Cancelación del request.</param>
    /// <returns>El alquiler declarado, o el rechazo. Uno ajeno no existe (404).</returns>
    [HttpPost("rentals/{rentalId}/declare-return")]
    public async Task<ActionResult<RentalDto>> DeclareReturn(
        string rentalId, [FromBody] DeclareReturnRequest body, CancellationToken ct)
    {
        if (!_gate.IsAuthenticated)
        {
            return Problema(StatusCodes.Status401Unauthorized, "alquiler.session_required",
                "Hay que iniciar sesión para declarar una devolución.");
        }

        // Declarar es del dueño: el de otro no existe, como en la puerta. El dueño se lee del contrato.
        var contrato = await _ledger.GetAsync(rentalId, ct).ConfigureAwait(false);
        if (contrato is null || !string.Equals(contrato.RenterId, QuienAlquila(), StringComparison.Ordinal))
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(body?.IdempotencyKey))
        {
            return Problema(StatusCodes.Status400BadRequest, "alquiler.idempotency_key_required",
                "Declarar la devolución es una escritura: hace falta la llave.");
        }

        // Por donde salió el alquiler, no por donde el sitio venda hoy.
        var resultado = contrato.Camino == CaminoDeCompra.Puerta
            ? await _puerta.DeclareReturnAsync(contrato, body.IdempotencyKey, ct).ConfigureAwait(false)
            : await _rentals.DeclareReturnAsync(rentalId, body.IdempotencyKey, ct).ConfigureAwait(false);

        return await ResponderAsync(resultado, contrato, "declarar la devolución", ct).ConfigureAwait(false);
    }

    /// <summary>
    /// El personal confirma la devolución con el daño: libera la garantía o cobra de ella. SÓLO el personal
    /// (decisión 5 del lote 1, #203): el inquilino declara, no confirma, aunque sea el dueño y aunque diga cero.
    /// </summary>
    /// <param name="rentalId">Cuál alquiler.</param>
    /// <param name="body">El monto del daño, ya calculado por quien recibió.</param>
    /// <param name="ct">Cancelación del request.</param>
    /// <returns>El alquiler devuelto, o el rechazo.</returns>
    [HttpPost("rentals/{rentalId}/return")]
    public async Task<ActionResult<RentalDto>> Return(
        string rentalId, [FromBody] SettleRequest body, CancellationToken ct)
    {
        if (NegarSiNoEsPersonal("confirmar la devolución de") is { } negado)
        {
            return negado;
        }

        var contrato = await _ledger.GetAsync(rentalId, ct).ConfigureAwait(false);
        if (contrato is null)
        {
            return NotFound();
        }

        var llave = body?.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(llave))
        {
            return Problema(StatusCodes.Status400BadRequest, "alquiler.idempotency_key_required",
                "Cobrar de la garantía es un movimiento relativo: sin llave, un reintento cobra dos veces.");
        }

        var monto = body?.Amount ?? 0m;
        var resultado = contrato.Camino == CaminoDeCompra.Puerta
            ? await _puerta.ConfirmReturnAsync(contrato, monto, QuienAlquila()!, llave, ct).ConfigureAwait(false)
            : await _rentals.ReturnAsync(rentalId, monto, llave, ct).ConfigureAwait(false);

        return await ResponderAsync(resultado, contrato, "confirmar la devolución", ct, confirmaElPersonal: QuienAlquila())
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Las devoluciones declaradas que esperan al personal, de todo el despliegue y de la más vieja a la más
    /// nueva. Sólo el personal.
    /// </summary>
    /// <param name="ct">Cancelación del request.</param>
    /// <returns>Los contratos declarados.</returns>
    [HttpGet("staff/returns")]
    public async Task<ActionResult<IReadOnlyList<AgreementDto>>> StaffReturns(CancellationToken ct)
    {
        if (NegarSiNoEsPersonal("ver las devoluciones declaradas de") is { } negado)
        {
            return negado;
        }

        var declaradas = await _ledger.ListByStateAsync(RentalState.Declared, ct).ConfigureAwait(false);
        return Ok(await ProyectarAsync(declaradas, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Cancela un alquiler confirmado: devuelve lo cobrado entero, suelta las unidades y anula la garantía (research
    /// R7 de la línea 005). El dueño, antes del día de inicio y SIN penalidad —la del cuerpo se ignora—; el personal,
    /// con la penalidad que fije.
    /// </summary>
    /// <param name="rentalId">Cuál alquiler.</param>
    /// <param name="body">La penalidad (sólo la del personal cuenta) y la llave.</param>
    /// <param name="ct">Cancelación del request.</param>
    /// <returns>El alquiler cancelado, o el rechazo.</returns>
    [HttpPost("rentals/{rentalId}/cancel")]
    public async Task<ActionResult<RentalDto>> Cancel(
        string rentalId, [FromBody] SettleRequest body, CancellationToken ct)
    {
        if (await NegarSiNoEsSuyo(rentalId, "cancelar", ct).ConfigureAwait(false) is { } negado)
        {
            return negado;
        }

        var llave = body?.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(llave))
        {
            return Problema(StatusCodes.Status400BadRequest, "alquiler.idempotency_key_required",
                "Cobrar de la garantía es un movimiento relativo: sin llave, un reintento cobra dos veces.");
        }

        // Quién cancela lo dice la sesión, no el cuerpo: el rol del personal, o el dueño.
        var quien = _gate.HasAnyRole(PersonalCsv) ? RentalCanceller.Staff : RentalCanceller.Renter;
        var penalidad = quien == RentalCanceller.Staff ? body?.Amount ?? 0m : 0m;

        var contrato = await _ledger.GetAsync(rentalId, ct).ConfigureAwait(false);
        var resultado = contrato is { Camino: CaminoDeCompra.Puerta } deLaPuerta
            ? await _puerta.CancelAsync(deLaPuerta, quien, penalidad,
                quien == RentalCanceller.Staff ? QuienAlquila() : null, llave, ct).ConfigureAwait(false)
            : await _rentals.CancelAsync(rentalId, penalidad, quien, llave, ct).ConfigureAwait(false);

        return await ResponderAsync(resultado, contrato, "cancelar", ct).ConfigureAwait(false);
    }

    // ── EJE 3 · el comprobante, que se lee de este lado ─────────────────────

    /// <summary>Mis contratos.</summary>
    /// <param name="ct">Cancelación del request.</param>
    /// <returns>Los contratos de quien está en sesión.</returns>
    [HttpGet("rentals")]
    public async Task<ActionResult<IReadOnlyList<AgreementDto>>> Mine(CancellationToken ct)
    {
        var quien = QuienAlquila();
        if (quien is null)
        {
            // Sin sesión no hay bandeja que servir, y NO se devuelve `[]`: la lista vacía diría
            // «todavía ninguno» sobre alguien que ni siquiera se identificó.
            return Problema(StatusCodes.Status401Unauthorized, "alquiler.session_required",
                "Hay que iniciar sesión para ver los alquileres.");
        }

        // Los alquileres de la puerta que se completaron y el registro no tiene —la pestaña se cerró justo después de
        // pagar— se emiten antes de leer (FR-020). Con techo: la bandeja sale igual del registro.
        if (_gate.CurrentMemberKey is { } miembro)
        {
            await _artefacto.ReconciliarAsync(miembro, ct).ConfigureAwait(false);
        }

        var contratos = await _ledger.ListForAsync(quien, ct).ConfigureAwait(false);
        return Ok(await ProyectarAsync(contratos, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// El comprobante de un alquiler, con su sello comprobado. El de un alquiler de la PUERTA se emite al leerlo, si
    /// el alquiler se completó (T024). Sólo de quien alquiló o del personal: uno ajeno no existe.
    /// </summary>
    /// <param name="rentalId">Cuál alquiler.</param>
    /// <param name="ct">Cancelación del request.</param>
    /// <returns>El comprobante; 404 si no consta o es ajeno; 409 si el alquiler no se completó.</returns>
    [HttpGet("rentals/{rentalId}/agreement")]
    public async Task<ActionResult<AgreementDto>> Agreement(string rentalId, CancellationToken ct)
    {
        if (!_gate.IsAuthenticated)
        {
            return Problema(StatusCodes.Status401Unauthorized, "alquiler.session_required",
                "Hay que iniciar sesión para ver un contrato.");
        }

        var contrato = await _ledger.GetAsync(rentalId, ct).ConfigureAwait(false);
        if (contrato is null && _gate.CurrentMemberKey is { } miembro)
        {
            var dePuerta = await _artefacto.ContratoAsync(rentalId, miembro, ct).ConfigureAwait(false);
            if (dePuerta.Contrato is null)
            {
                return dePuerta.Estado == StatusCodes.Status404NotFound
                    ? NotFound()
                    : Problema(dePuerta.Estado, dePuerta.Codigo ?? "alquiler.rejected", dePuerta.Mensaje ?? string.Empty,
                        dePuerta.Transitorio);
            }

            contrato = dePuerta.Contrato;
        }

        if (contrato is null
            || (!_gate.HasAnyRole(PersonalCsv)
                && !string.Equals(contrato.RenterId, QuienAlquila(), StringComparison.Ordinal)))
        {
            return NotFound();
        }

        var leido = await _issuer.LeerAsync(contrato, ct).ConfigureAwait(false);
        return Ok(AgreementDto.From(leido.Contrato, leido.Comprobado, leido.DesenlaceComprobado));
    }

    // ── Fontanería ──────────────────────────────────────────────────────────

    /// <summary>
    /// La respuesta de una operación sobre un alquiler, con su estado ANOTADO en el registro: la bandeja lo pinta
    /// sin preguntarle a nadie, y la del personal lo busca ahí.
    /// </summary>
    /// <remarks>
    /// El sello del contrato no cubre el estado (sólo lo pactado al salir), así que anotarlo no lo rompe.
    /// </remarks>
    private async Task<ActionResult<RentalDto>> ResponderAsync(
        RentalResult? resultado, RentalAgreement? contrato, string quePasaba, CancellationToken ct,
        string? confirmaElPersonal = null)
    {
        if (resultado is null)
        {
            return NotFound();
        }

        if (resultado.Rental is null)
        {
            return Rechazar(resultado, quePasaba);
        }

        if (contrato is not null && contrato.Estado != resultado.Rental.State)
        {
            contrato = contrato with { Estado = resultado.Rental.State };
            await _ledger.RecordAsync(contrato, ct).ConfigureAwait(false);
        }

        // La devolución que confirmó el personal queda con su desenlace SELLADO aparte (005-P6): lo cobrado de la
        // garantía y lo liberado, que es lo retenido al salir menos el daño. Una vez: repetir no lo vuelve a sellar.
        if (contrato is { Desenlace: null } && confirmaElPersonal is not null && resultado.Rental.State == RentalState.Returned)
        {
            var danio = resultado.Rental.DamageCharged;
            contrato = await _issuer.SellarDesenlaceAsync(contrato, danio,
                danio <= contrato.DepositHeld ? contrato.DepositHeld - danio : null, confirmaElPersonal, ct).ConfigureAwait(false);
        }

        if (contrato is null)
        {
            return Ok(RentalDto.From(resultado.Rental, null, false));
        }

        var leido = await _issuer.LeerAsync(contrato, ct).ConfigureAwait(false);
        return Ok(RentalDto.From(resultado.Rental, leido.Contrato, leido.Comprobado, leido.DesenlaceComprobado));
    }

    /// <summary>
    /// Los contratos de una bandeja, cada uno con su sello comprobado por quien lo selló (y sellado, si quedó pendiente).
    /// </summary>
    private async Task<List<AgreementDto>> ProyectarAsync(
        IEnumerable<RentalAgreement> contratos, CancellationToken ct)
    {
        var dtos = new List<AgreementDto>();
        foreach (var contrato in contratos)
        {
            var leido = await _issuer.LeerAsync(contrato, ct).ConfigureAwait(false);
            dtos.Add(AgreementDto.From(leido.Contrato, leido.Comprobado, leido.DesenlaceComprobado));
        }

        return dtos;
    }

    /// <summary>Lo que sólo hace el personal: sin sesión 401, sin el rol 403 (<c>alquiler.staff_required</c>).</summary>
    /// <remarks>
    /// 403 aunque sea el dueño: confirmar la devolución con el daño es lo que la decisión 5 le quitó al inquilino,
    /// que declara y espera.
    /// </remarks>
    private ActionResult? NegarSiNoEsPersonal(string quePasaba)
    {
        if (!_gate.IsAuthenticated)
        {
            return Problema(StatusCodes.Status401Unauthorized, "alquiler.session_required",
                $"Hay que iniciar sesión para {quePasaba} un alquiler.");
        }

        return _gate.HasAnyRole(PersonalCsv)
            ? null
            : Problema(StatusCodes.Status403Forbidden, "alquiler.staff_required",
                $"Sólo el personal puede {quePasaba} un alquiler: quien alquila declara la devolución.");
    }

    /// <summary>
    /// Un «no» de este borde como problem+json, con <c>code</c> y <c>transient</c>: la misma forma que
    /// la puerta de Eventos (ADR 0140) y la que el cliente del UI ya lee (<c>code</c>, <c>detail</c>).
    /// </summary>
    /// <remarks>
    /// Un objeto anónimo y no un record con <c>Code</c> y <c>Detail</c>: ese tipo es la forma de un
    /// LECTOR de problem+json, y en el CMS hay uno solo (<c>RechazoDelArbolDeServicios</c>, #178).
    /// </remarks>
    private static ContentResult Problema(int estado, string codigo, string mensaje, bool transitorio = false) => new()
    {
        StatusCode = estado,
        ContentType = "application/problem+json",
        Content = JsonSerializer.Serialize(new
        {
            type = "about:blank",
            title = ReasonPhrases.GetReasonPhrase(estado),
            status = estado,
            detail = mensaje,
            code = codigo,
            transient = transitorio,
        }, LecturaWeb),
    };

    private static readonly JsonSerializerOptions LecturaWeb = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Cancelar lo hace quien alquiló o el personal — nadie más. (Confirmar la devolución ya es SÓLO del
    /// personal, <see cref="NegarSiNoEsPersonal"/>: la decisión 5 contestó la pregunta de producto de abajo.)
    /// </summary>
    /// <remarks>
    /// <para><b>Sin esto, cualquiera que supiera un <c>rentalId</c> cerraba el alquiler
    /// ajeno</b>, anónimo, y el cuerpo lleva el MONTO que se cobra de la garantía: devolver con
    /// el monto entero era capturar la garantía de otra persona. Es la clase IDOR que T2 cerró en
    /// los otros siete controllers (ADR 0112), y el piloto 2 la reabrió porque nadie le preguntó
    /// a este borde quién llama.</para>
    ///
    /// <para><b>El dueño se lee del CONTRATO que este mismo borde emitió al reservar</b>, no de
    /// algo que mande el cliente: el cuerpo no trae quién es, y un campo así sería la
    /// fabricación escrita en la firma. Un alquiler sin contrato en este CMS no consta: 404.</para>
    ///
    /// <para><b>El personal es <c>admin</c></b>, el único rol de staff sembrado, como en la tienda
    /// (<c>ShopCatalogController.RequireShopStaff</c>). Quién FIJA el daño —el spec dice «quien
    /// recibe el equipo» y la app deja que el inquilino declare cero— es una pregunta de producto
    /// que esto NO contesta: sólo impide que la conteste un tercero.</para>
    /// </remarks>
    private async Task<ActionResult?> NegarSiNoEsSuyo(
        string rentalId, string quePasaba, CancellationToken ct)
    {
        if (!_gate.IsAuthenticated)
        {
            return Problema(StatusCodes.Status401Unauthorized, "alquiler.session_required",
                $"Hay que iniciar sesión para {quePasaba} un alquiler.");
        }

        if (_gate.HasAnyRole(PersonalCsv))
        {
            return null;
        }

        var contrato = await _ledger.GetAsync(rentalId, ct).ConfigureAwait(false);
        if (contrato is null)
        {
            return NotFound();
        }

        // 403 directo y no Forbid(), por lo mismo que la tienda: Forbid() pasa por el esquema de
        // autenticación de Umbraco y contesta otra cosa.
        return string.Equals(contrato.RenterId, QuienAlquila(), StringComparison.Ordinal)
            ? null
            : Problema(StatusCodes.Status403Forbidden, "alquiler.not_yours",
                "Ese alquiler no es tuyo.");
    }

    /// <summary>El rol que puede cerrar cualquier alquiler.</summary>
    private const string PersonalCsv = "admin";

    /// <summary>
    /// Un rechazo del eje 2 sale con el código que dio quien decidió, y su transitoriedad manda
    /// el estado.
    /// </summary>
    /// <remarks>
    /// Un <c>Unavailable</c> sale <b>503</b> y no 409: no es que no se pueda, es que hay que
    /// volver a intentarlo, y confundirlos hace que alguien deshaga algo sano (#112).
    /// </remarks>
    private ActionResult<RentalDto> Rechazar(RentalResult resultado, string quePasaba)
    {
        _log.LogWarning("Alquiler: no se pudo {Que}: {Codigo}.", quePasaba, resultado.RejectionCode);
        var unavailable = resultado.Outcome == RentalOutcome.Unavailable;
        return Problema(
            unavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status409Conflict,
            resultado.RejectionCode ?? "alquiler.rejected",
            resultado.Reason ?? string.Empty,
            transitorio: unavailable);
    }

    /// <summary>
    /// Quién alquila, como seudónimo — el <c>MemberKey</c> si hay sesión, y null si no.
    /// </summary>
    /// <remarks>
    /// El <c>MemberKey</c> y no la huella del correo, por lo mismo que la tienda (#120): es el
    /// identificador que este árbol ya firma, y el correo no sale de esta máquina.
    /// </remarks>
    private string? QuienAlquila()
        => _gate.IsAuthenticated && _gate.CurrentMemberKey is { } key
            ? key.ToString("N")
            : null;

    private bool TryLeer(
        string? equipmentId, int? quantity, DateOnly? start, DateOnly? end,
        out RentalRequest pedido, out string porQue)
    {
        pedido = null!;
        var quien = QuienAlquila();

        if (quien is null)
        {
            porQue = "Hay que iniciar sesión para alquilar.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(equipmentId) || start is null || end is null)
        {
            porQue = "Faltan el equipo o las fechas.";
            return false;
        }

        pedido = new RentalRequest(equipmentId, quantity ?? 1, start.Value, end.Value, quien);
        porQue = string.Empty;
        return true;
    }
}

/// <summary>Lo que se pide para cotizar.</summary>
/// <param name="EquipmentId">El slug del equipo.</param>
/// <param name="Quantity">Cuántas unidades.</param>
/// <param name="Start">Primer día.</param>
/// <param name="End">Día de devolución, exclusive.</param>
public sealed record QuoteRequest(string? EquipmentId, int? Quantity, DateOnly? Start, DateOnly? End);

/// <summary>Lo que se pide para reservar.</summary>
/// <param name="EquipmentId">El slug del equipo.</param>
/// <param name="Quantity">Cuántas unidades.</param>
/// <param name="Start">Primer día.</param>
/// <param name="End">Día de devolución, exclusive.</param>
/// <param name="IdempotencyKey">Obligatoria: reservar retiene plata.</param>
public sealed record ReserveRequest(
    string? EquipmentId, int? Quantity, DateOnly? Start, DateOnly? End, string? IdempotencyKey);

/// <summary>Lo que se pide para cerrar un alquiler.</summary>
/// <param name="Amount">
/// El daño o la penalidad, YA CALCULADO por quien recibió el equipo: el motor cotiza el alquiler
/// entero y no sabe cuánto vale un rayón.
/// </param>
/// <param name="IdempotencyKey">Obligatoria: cobrar de la garantía es relativo.</param>
public sealed record SettleRequest(decimal? Amount, string? IdempotencyKey);

/// <summary>Lo que se pide para declarar la devolución: sólo la llave, porque declarar no lleva monto.</summary>
/// <param name="IdempotencyKey">Obligatoria, como en las demás escrituras.</param>
public sealed record DeclareReturnRequest(string? IdempotencyKey);


/// <summary>La tarjeta de un equipo en el listado.</summary>
/// <param name="EquipmentId">Su slug.</param>
/// <param name="Name">Su nombre.</param>
/// <param name="Category">Su familia.</param>
/// <param name="Summary">Una o dos líneas.</param>
/// <param name="CoverUrl">La foto principal, o null.</param>
/// <param name="DailyRate">La tarifa base por día.</param>
/// <param name="Deposit">La garantía que se retiene.</param>
/// <param name="Units">Cuántas unidades hay.</param>
public sealed record EquipmentCardDto(
    string EquipmentId, string Name, string Category, string Summary,
    string? CoverUrl, decimal DailyRate, decimal Deposit, int Units, string Currency)
{
    /// <summary>Proyecta un equipo a su tarjeta.</summary>
    /// <param name="e">El equipo.</param>
    /// <returns>La tarjeta.</returns>
    public static EquipmentCardDto From(RentalEquipment e)
        => new(e.Id, e.Name, e.Category, e.Summary, e.CoverUrl, e.DailyRate, e.Deposit, e.Units, e.Currency);
}

/// <summary>La ficha completa de un equipo.</summary>
/// <param name="EquipmentId">Su slug.</param>
/// <param name="Name">Su nombre.</param>
/// <param name="Category">Su familia.</param>
/// <param name="Summary">Una o dos líneas.</param>
/// <param name="Description">El cuerpo de la ficha.</param>
/// <param name="CoverUrl">La foto principal.</param>
/// <param name="GalleryUrls">Las fotos del estado real.</param>
/// <param name="DailyRate">La tarifa base por día.</param>
/// <param name="Deposit">La garantía.</param>
/// <param name="Units">Cuántas unidades hay.</param>
/// <param name="MinDays">Alquiler mínimo.</param>
/// <param name="MaxDays">Alquiler máximo.</param>
/// <param name="Includes">Lo que va con el equipo.</param>
/// <param name="Requirements">Lo que tiene que cumplir quien alquila.</param>
/// <param name="Rates">Los tramos de tarifa.</param>
/// <param name="Specs">La ficha técnica.</param>
public sealed record EquipmentDetailDto(
    string EquipmentId, string Name, string Category, string Summary, string Description,
    string? CoverUrl, IReadOnlyList<string> GalleryUrls, decimal DailyRate, decimal Deposit,
    int Units, int MinDays, int MaxDays,
    IReadOnlyList<string> Includes, IReadOnlyList<string> Requirements,
    IReadOnlyList<RateDto> Rates, IReadOnlyList<SpecDto> Specs, string Currency)
{
    /// <summary>Proyecta un equipo a su ficha.</summary>
    /// <param name="e">El equipo.</param>
    /// <returns>La ficha.</returns>
    public static EquipmentDetailDto From(RentalEquipment e)
        => new(e.Id, e.Name, e.Category, e.Summary, e.Description, e.CoverUrl, e.GalleryUrls,
            e.DailyRate, e.Deposit, e.Units, e.MinDays, e.MaxDays, e.Includes, e.Requirements,
            e.Rates.Select(r => new RateDto(r.Code, r.Label, r.MinDays, r.PerDay, r.Description)).ToList(),
            e.Specs.Select(s => new SpecDto(s.Label, s.Value)).ToList(),
            e.Currency);
}

/// <summary>Un tramo de tarifa.</summary>
/// <param name="Code">Su código.</param>
/// <param name="Label">Su nombre visible.</param>
/// <param name="MinDays">Desde cuántos días aplica.</param>
/// <param name="PerDay">Cuánto vale el día ahí.</param>
/// <param name="Description">Qué cubre.</param>
public sealed record RateDto(string Code, string Label, int MinDays, decimal PerDay, string Description);

/// <summary>Una fila de la ficha técnica.</summary>
/// <param name="Label">Qué se mide.</param>
/// <param name="Value">El dato con su unidad.</param>
public sealed record SpecDto(string Label, string Value);

/// <summary>Una cotización.</summary>
/// <param name="EquipmentId">El equipo.</param>
/// <param name="Quantity">Cuántas unidades.</param>
/// <param name="Days">Cuántos días.</param>
/// <param name="PerDay">El valor del día que aplicó.</param>
/// <param name="RentalTotal">Lo que se cobra.</param>
/// <param name="Deposit">Lo que se RETIENE. No se suma al total.</param>
public sealed record QuoteDto(
    string EquipmentId, int Quantity, int Days, decimal PerDay, decimal RentalTotal, decimal Deposit,
    string Currency)
{
    /// <summary>Proyecta una cotización.</summary>
    /// <param name="q">La cotización.</param>
    /// <returns>El DTO.</returns>
    public static QuoteDto From(RentalQuote q)
        => new(q.EquipmentId, q.Quantity, q.Days, q.PerDay, q.RentalTotal, q.Deposit, q.Currency);
}

/// <summary>Un alquiler tal como lo lee el otro árbol.</summary>
/// <param name="RentalId">Su identificador.</param>
/// <param name="EquipmentId">Qué equipo.</param>
/// <param name="Quantity">Cuántas unidades.</param>
/// <param name="Start">Desde cuándo.</param>
/// <param name="End">Hasta cuándo.</param>
/// <param name="State">En qué punto está.</param>
/// <param name="Quote">Lo cotizado, congelado al reservar.</param>
/// <param name="DepositHeld">Cuánto sigue retenido.</param>
/// <param name="DamageCharged">Cuánto se cobró de la garantía.</param>
/// <param name="Agreement">Su comprobante, si ya se emitió.</param>
public sealed record RentalDto(
    string RentalId, string EquipmentId, int Quantity, DateOnly Start, DateOnly End,
    string State, QuoteDto Quote, decimal DepositHeld, decimal DamageCharged,
    AgreementDto? Agreement)
{
    /// <summary>Proyecta un alquiler con su comprobante.</summary>
    /// <param name="r">El alquiler.</param>
    /// <param name="a">Su comprobante, o null.</param>
    /// <param name="comprobado">
    /// Si el sello de ese comprobante cuadra. <b>Se PASA y no se asume</b>: escribir
    /// <c>false</c> aquí afirmaría que el sello no comprueba sin haberlo mirado, que es
    /// <c>an_omitted_key_can_be_an_assertion</c> con la clave presente.
    /// </param>
    /// <returns>El DTO.</returns>
    /// <param name="desenlaceComprobado">Lo mismo para el sello del desenlace de la devolución, si lo hay.</param>
    public static RentalDto From(Rental r, RentalAgreement? a, bool? comprobado, bool? desenlaceComprobado = null)
        => new(r.RentalId, r.EquipmentId, r.Quantity, r.Start, r.End,
            r.State.ToString().ToLowerInvariant(), QuoteDto.From(r.Quote),
            r.DepositHeld, r.DamageCharged,
            a is null ? null : AgreementDto.From(a, comprobado, desenlaceComprobado));
}

/// <summary>El comprobante de un alquiler.</summary>
/// <param name="RentalId">A qué alquiler corresponde.</param>
/// <param name="EquipmentName">Qué se llevó.</param>
/// <param name="Quantity">Cuántas unidades.</param>
/// <param name="Start">Desde cuándo.</param>
/// <param name="End">Hasta cuándo.</param>
/// <param name="RentalTotal">Lo que se cobró.</param>
/// <param name="DepositHeld">Lo que se retuvo, tal como estaba al emitirlo.</param>
/// <param name="IssuedUtc">Cuándo se emitió.</param>
/// <param name="Seal">El sello, o vacío si no hubo sellador.</param>
/// <param name="Verified">
/// Si el sello COMPRUEBA contra lo que el comprobante afirma. <c>false</c> con sello vacío es la
/// verdad: sin sellador no se da por bueno, porque comprobar es lo único que impide que quien
/// escriba en el almacén fabrique un comprobante con la cifra que quiera (#45). <c>null</c> es «no se pudo
/// comprobar» (el sello está pendiente, o quien lo selló no contestó): el elemento lo pinta distinto de «no cuadra».
/// </param>
/// <param name="SealPending">El servicio de firma no contestó al emitirlo: se sella al volver.</param>
/// <param name="Outcome">Cómo se cerró la devolución, con su propio sello; null mientras no se cierre.</param>
public sealed record AgreementDto(
    string RentalId, string EquipmentName, int Quantity, DateOnly Start, DateOnly End,
    decimal RentalTotal, decimal DepositHeld, string Currency, DateTimeOffset IssuedUtc, string Seal, bool? Verified,
    bool SealPending = false, AgreementOutcomeDto? Outcome = null)
{
    /// <summary>Proyecta un comprobante.</summary>
    /// <param name="a">El comprobante.</param>
    /// <param name="comprobado">Si su sello comprobó; null si no se pudo comprobar.</param>
    /// <param name="desenlaceComprobado">Si el sello de su desenlace comprobó; null si no se pudo o no hay.</param>
    /// <returns>El DTO.</returns>
    public static AgreementDto From(RentalAgreement a, bool? comprobado, bool? desenlaceComprobado = null)
        => new(a.RentalId, a.EquipmentName, a.Quantity, a.Start, a.End,
            a.RentalTotal, a.DepositHeld, a.Currency, a.IssuedUtc, a.Seal, comprobado, a.SelloPendiente,
            a.Desenlace is not { } d
                ? null
                : new AgreementOutcomeDto(d.ConfirmadoUtc, d.Danio, d.GarantiaLiberada, d.Sello, desenlaceComprobado,
                    d.SelloPendiente));
}

/// <summary>Cómo se cerró la devolución de un alquiler (decisión 5 del lote 1; 005-P6): lo que confirmó el personal.</summary>
/// <remarks>Quién lo confirmó no sale: es una referencia del personal, y a quien alquiló no le hace falta.</remarks>
/// <param name="ConfirmedUtc">Cuándo lo confirmó el personal.</param>
/// <param name="Damage">Cuánto se cobró de la garantía; cero sin daño.</param>
/// <param name="DepositReleased">Cuánto se liberó; null es «no consta».</param>
/// <param name="Seal">El sello del desenlace, o vacío si quedó pendiente.</param>
/// <param name="Verified">Si ese sello comprueba; null si no se pudo comprobar.</param>
/// <param name="SealPending">El servicio de firma no contestó al sellarlo.</param>
public sealed record AgreementOutcomeDto(
    DateTimeOffset ConfirmedUtc, decimal Damage, decimal? DepositReleased, string Seal, bool? Verified, bool SealPending);
