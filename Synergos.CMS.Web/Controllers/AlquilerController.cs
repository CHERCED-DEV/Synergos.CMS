using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

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
    private readonly EquipmentAgreementIssuer _issuer;
    private readonly EquipmentAgreementLedger _ledger;
    private readonly IMemberAccessGate _gate;
    private readonly ILogger<AlquilerController> _log;

    /// <summary>Construye el borde.</summary>
    /// <param name="catalog">El eje 1.</param>
    /// <param name="rentals">El eje 2.</param>
    /// <param name="issuer">El emisor del contrato (eje 3), fuera del seam.</param>
    /// <param name="ledger">El registro de contratos, que es el modelo de lectura.</param>
    /// <param name="gate">Quién está en sesión.</param>
    /// <param name="log">Para decir qué se rechazó.</param>
    public AlquilerController(
        IEquipmentCatalogProvider catalog,
        IEquipmentRentalService rentals,
        EquipmentAgreementIssuer issuer,
        EquipmentAgreementLedger ledger,
        IMemberAccessGate gate,
        ILogger<AlquilerController> log)
    {
        _catalog = catalog;
        _rentals = rentals;
        _issuer = issuer;
        _ledger = ledger;
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
            return BadRequest(new RejectionDto("alquiler.bad_request", porQue));
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
            return BadRequest(new RejectionDto("alquiler.bad_request", porQue));
        }

        if (string.IsNullOrWhiteSpace(body!.IdempotencyKey))
        {
            return BadRequest(new RejectionDto("alquiler.idempotency_key_required",
                "Reservar retiene plata: sin llave, un reintento la retiene dos veces."));
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

        return Ok(RentalDto.From(resultado.Rental, contrato, _issuer.Verify(contrato)));
    }

    /// <summary>El equipo volvió: libera la garantía o cobra de ella el daño.</summary>
    /// <param name="rentalId">Cuál alquiler.</param>
    /// <param name="body">El monto del daño, ya calculado por quien recibió.</param>
    /// <param name="ct">Cancelación del request.</param>
    /// <returns>El alquiler cerrado, o el rechazo.</returns>
    [HttpPost("rentals/{rentalId}/return")]
    public Task<ActionResult<RentalDto>> Return(
        string rentalId, [FromBody] SettleRequest body, CancellationToken ct)
        => CerrarAsync(rentalId, body, "devolver", ct);

    /// <summary>Cancela antes de que el equipo salga.</summary>
    /// <param name="rentalId">Cuál alquiler.</param>
    /// <param name="body">La penalidad a retener, ya calculada.</param>
    /// <param name="ct">Cancelación del request.</param>
    /// <returns>El alquiler cancelado, o el rechazo.</returns>
    [HttpPost("rentals/{rentalId}/cancel")]
    public Task<ActionResult<RentalDto>> Cancel(
        string rentalId, [FromBody] SettleRequest body, CancellationToken ct)
        => CerrarAsync(rentalId, body, "cancelar", ct);

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
            return Unauthorized(new RejectionDto("alquiler.session_required",
                "Hay que iniciar sesión para ver los alquileres."));
        }

        var contratos = await _ledger.ListForAsync(quien, ct).ConfigureAwait(false);
        return Ok(contratos.Select(a => AgreementDto.From(a, _issuer.Verify(a))).ToList());
    }

    /// <summary>El comprobante de un alquiler, con su sello comprobado.</summary>
    /// <param name="rentalId">Cuál alquiler.</param>
    /// <param name="ct">Cancelación del request.</param>
    /// <returns>El comprobante, o 404.</returns>
    [HttpGet("rentals/{rentalId}/agreement")]
    public async Task<ActionResult<AgreementDto>> Agreement(string rentalId, CancellationToken ct)
    {
        var contrato = await _ledger.GetAsync(rentalId, ct).ConfigureAwait(false);
        return contrato is null ? NotFound() : Ok(AgreementDto.From(contrato, _issuer.Verify(contrato)));
    }

    // ── Fontanería ──────────────────────────────────────────────────────────

    private async Task<ActionResult<RentalDto>> CerrarAsync(
        string rentalId, SettleRequest? body, string quePasaba, CancellationToken ct)
    {
        var monto = body?.Amount ?? 0m;
        var llave = body?.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(llave))
        {
            return BadRequest(new RejectionDto("alquiler.idempotency_key_required",
                "Cobrar de la garantía es un movimiento relativo: sin llave, un reintento cobra dos veces."));
        }

        var resultado = quePasaba == "devolver"
            ? await _rentals.ReturnAsync(rentalId, monto, llave, ct).ConfigureAwait(false)
            : await _rentals.CancelAsync(rentalId, monto, llave, ct).ConfigureAwait(false);

        if (resultado is null)
        {
            return NotFound();
        }

        if (resultado.Rental is null)
        {
            return Rechazar(resultado, quePasaba);
        }

        var contrato = await _ledger.GetAsync(rentalId, ct).ConfigureAwait(false);
        return Ok(RentalDto.From(resultado.Rental, contrato, _issuer.Verify(contrato)));
    }

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
        var dto = new RejectionDto(resultado.RejectionCode ?? "alquiler.rejected", resultado.Reason);

        return resultado.Outcome == RentalOutcome.Unavailable
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, dto)
            : Conflict(dto);
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

/// <summary>Un rechazo, tal como lo lee el otro árbol.</summary>
/// <param name="Code">El código; es contrato.</param>
/// <param name="Detail">Qué decirle a quien está delante.</param>
public sealed record RejectionDto(string Code, string? Detail);

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
    string? CoverUrl, decimal DailyRate, decimal Deposit, int Units)
{
    /// <summary>Proyecta un equipo a su tarjeta.</summary>
    /// <param name="e">El equipo.</param>
    /// <returns>La tarjeta.</returns>
    public static EquipmentCardDto From(RentalEquipment e)
        => new(e.Id, e.Name, e.Category, e.Summary, e.CoverUrl, e.DailyRate, e.Deposit, e.Units);
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
    IReadOnlyList<RateDto> Rates, IReadOnlyList<SpecDto> Specs)
{
    /// <summary>Proyecta un equipo a su ficha.</summary>
    /// <param name="e">El equipo.</param>
    /// <returns>La ficha.</returns>
    public static EquipmentDetailDto From(RentalEquipment e)
        => new(e.Id, e.Name, e.Category, e.Summary, e.Description, e.CoverUrl, e.GalleryUrls,
            e.DailyRate, e.Deposit, e.Units, e.MinDays, e.MaxDays, e.Includes, e.Requirements,
            e.Rates.Select(r => new RateDto(r.Code, r.Label, r.MinDays, r.PerDay, r.Description)).ToList(),
            e.Specs.Select(s => new SpecDto(s.Label, s.Value)).ToList());
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
    string EquipmentId, int Quantity, int Days, decimal PerDay, decimal RentalTotal, decimal Deposit)
{
    /// <summary>Proyecta una cotización.</summary>
    /// <param name="q">La cotización.</param>
    /// <returns>El DTO.</returns>
    public static QuoteDto From(RentalQuote q)
        => new(q.EquipmentId, q.Quantity, q.Days, q.PerDay, q.RentalTotal, q.Deposit);
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
    public static RentalDto From(Rental r, RentalAgreement? a, bool comprobado)
        => new(r.RentalId, r.EquipmentId, r.Quantity, r.Start, r.End,
            r.State.ToString().ToLowerInvariant(), QuoteDto.From(r.Quote),
            r.DepositHeld, r.DamageCharged,
            a is null ? null : AgreementDto.From(a, comprobado));
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
/// escriba en el almacén fabrique un comprobante con la cifra que quiera (#45).
/// </param>
public sealed record AgreementDto(
    string RentalId, string EquipmentName, int Quantity, DateOnly Start, DateOnly End,
    decimal RentalTotal, decimal DepositHeld, DateTimeOffset IssuedUtc, string Seal, bool Verified)
{
    /// <summary>Proyecta un comprobante.</summary>
    /// <param name="a">El comprobante.</param>
    /// <param name="comprobado">Si su sello comprobó.</param>
    /// <returns>El DTO.</returns>
    public static AgreementDto From(RentalAgreement a, bool comprobado)
        => new(a.RentalId, a.EquipmentName, a.Quantity, a.Start, a.End,
            a.RentalTotal, a.DepositHeld, a.IssuedUtc, a.Seal, comprobado);
}
