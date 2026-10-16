using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Alquiler.Domain;

/// <summary>Lo que pasó con un alquiler confirmado por la puerta después del cierre (data-model §2).</summary>
/// <param name="SagaId">El alquiler: la saga de <c>alquiler.renta</c>, ya <c>Completed</c>.</param>
/// <param name="Estado">Uno de <see cref="LiquidacionDeAlquiler.Declarada"/>, <see cref="LiquidacionDeAlquiler.Devuelta"/>
/// o <see cref="LiquidacionDeAlquiler.Cancelada"/>. «Confirmada» es la AUSENCIA de registro.</param>
/// <param name="DeclaradaUtc">Cuándo la declaró el inquilino; nula si el personal confirmó sin declaración (S-2).</param>
/// <param name="ConfirmadaUtc">Cuándo la confirmó el personal.</param>
/// <param name="Danio">Lo que se cobró de la garantía; nulo mientras no se confirma.</param>
/// <param name="GarantiaLiberada">Lo que se le soltó: G − D. Nulo es «no consta», nunca cero.</param>
/// <param name="Personal">Quién confirmó o canceló, por referencia; nulo si canceló el inquilino.</param>
/// <param name="CanceladaUtc">Cuándo se canceló (T020).</param>
/// <param name="Penalidad">Lo que el personal retuvo de la garantía al cancelar; cero si canceló el inquilino.</param>
/// <param name="AlquilerDevuelto">Lo cobrado que se devolvió al cancelar: el total entero.</param>
public sealed record LiquidacionRegistrada(
    string SagaId,
    string Estado,
    DateTimeOffset? DeclaradaUtc,
    DateTimeOffset? ConfirmadaUtc,
    Money? Danio,
    Money? GarantiaLiberada,
    Ref? Personal,
    DateTimeOffset? CanceladaUtc = null,
    Money? Penalidad = null,
    Money? AlquilerDevuelto = null);

/// <summary>Un alquiler de la puerta con lo que se sabe de él después del cierre.</summary>
/// <param name="SagaId">El alquiler.</param>
/// <param name="Estado">En el cable de siempre: <c>reserved</c>, <c>declared</c>, <c>returned</c> o <c>cancelled</c>.</param>
/// <param name="Garantia">La garantía retenida al reservar (cero si no hubo).</param>
/// <param name="GarantiaRetenida">Cuánto sigue retenido: la garantía hasta que se confirme; cero después.</param>
/// <param name="Registro">Lo liquidado, o nulo si el alquiler sigue confirmado sin más.</param>
public sealed record VistaDeLiquidacion(string SagaId, string Estado, Money Garantia, Money GarantiaRetenida, LiquidacionRegistrada? Registro);

/// <summary>
/// Las operaciones del alquiler confirmado por la puerta: declarar la devolución (el inquilino) y confirmarla con
/// el daño (el personal). Fuera de la puerta (lote 3, 005-P9; research R5) hasta la forma genérica del núcleo
/// (T051, NN-7/NN-8).
/// </summary>
/// <remarks>
/// <para><b>Por qué un registro propio</b>: la saga genérica no admite escrituras después de <c>Completed</c>, y
/// lo que pasa después —días después— no es un paso suyo. Se guarda una entrada por saga, sólo si alguien declaró
/// o confirmó.</para>
///
/// <para><b>Cada operación toma el turno de la saga</b> (<see cref="ISagaLease"/>): de dos confirmaciones en
/// carrera, la segunda se rechaza como transitoria (<c>alquiler.settlement_in_flight</c>) y, al repetirla, ya
/// encuentra la devuelta. Y la plata se mueve con llaves derivadas de la saga (<c>{saga}|liquidar:capturar</c>,
/// <c>…:devolver</c>): repetir después de un fallo a medias no cobra de nuevo.</para>
///
/// <para><b>Una retención vencida</b>: <c>Api.Payments</c> no tiene vencimiento propio y contesta
/// <c>payments.voided</c> a capturar lo que ya no está autorizado. Sin daño, anular lo anulado es idempotente y
/// cuenta como liberada; con daño, se rechaza con <c>alquiler.retention_expired</c>, porque no hay de dónde
/// cobrarlo.</para>
/// </remarks>
public sealed class LiquidacionDeAlquiler
{
    /// <summary>El flujo cuyas sagas se liquidan aquí.</summary>
    public const string Flujo = "alquiler.renta";

    public const string Declarada = "declarada";
    public const string Devuelta = "devuelta";
    public const string Cancelada = "cancelada";

    /// <summary>Los códigos de la pasarela que dicen que la autorización ya no está.</summary>
    private static readonly string[] YaNoAutorizada = ["payments.voided"];

    private readonly SagaEngine<SagaDeFlujo> _motor;
    private readonly ISagaLease _turno;
    private readonly IPaymentsPort _pagos;
    private readonly IDeshacerCobros _cobros;
    private readonly IDeshacerAgenda _agenda;
    private readonly HusoDelAlquiler _huso;
    private readonly TimeProvider _reloj;
    private readonly ILogger<LiquidacionDeAlquiler> _log;
    private readonly JsonCollectionStore<LiquidacionRegistrada> _registro;

    public LiquidacionDeAlquiler(
        SagaEngine<SagaDeFlujo> motor, ISagaLease turno, IPaymentsPort pagos, IDeshacerCobros cobros, IDeshacerAgenda agenda,
        HusoDelAlquiler huso, IOptions<SagaStorageOptions> almacen, TimeProvider reloj, ILogger<LiquidacionDeAlquiler> log)
    {
        ArgumentNullException.ThrowIfNull(almacen);
        _motor = motor;
        _turno = turno;
        _pagos = pagos;
        _cobros = cobros;
        _agenda = agenda;
        _huso = huso;
        _reloj = reloj;
        _log = log;
        _registro = new JsonCollectionStore<LiquidacionRegistrada>(
            Path.Combine(almacen.Value.Root, "liquidaciones"), "alquileres", l => l.SagaId);
    }

    /// <summary>El inquilino declara que devolvió el equipo. No mueve plata.</summary>
    /// <param name="sagaId">El alquiler.</param>
    /// <param name="dueno">Quién lo pide, como lo pone la puerta: tiene que ser el dueño.</param>
    /// <param name="ct">Cancelación.</param>
    public Task<Result<VistaDeLiquidacion>> DeclararAsync(string sagaId, Ref dueno, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dueno);
        ct.ThrowIfCancellationRequested();

        using var turno = _turno.TryAcquire(sagaId);
        if (turno is null) return Task.FromResult(Ocupada(sagaId));

        if (Hallar(sagaId, dueno) is not { } saga) return Task.FromResult(NoExiste(sagaId));
        var previo = _registro.Find(sagaId);

        if (previo?.Estado == Declarada) return Task.FromResult(Result.Ok(Vista(saga, previo)));
        if (saga.Status != SagaStatus.Completed || previo is not null)
        {
            return Task.FromResult(Result.Rejected<VistaDeLiquidacion>(Rejection.Conflict("alquiler.not_declarable",
                $"El alquiler {sagaId} no está confirmado: no hay devolución que declarar.")));
        }

        var declarada = new LiquidacionRegistrada(sagaId, Declarada, _reloj.GetUtcNow(), null, null, null, null);
        _registro.Put(declarada);
        return Task.FromResult(Result.Ok(Vista(saga, declarada)));
    }

    /// <summary>
    /// El personal confirma la devolución con el daño: sin daño anula la garantía; con daño la captura y devuelve
    /// G − D. Con o sin declaración previa (S-2).
    /// </summary>
    /// <param name="sagaId">El alquiler.</param>
    /// <param name="danio">Lo que se cobra de la garantía.</param>
    /// <param name="personal">Quién confirma, por referencia.</param>
    /// <param name="ct">Cancelación.</param>
    public async Task<Result<VistaDeLiquidacion>> ConfirmarAsync(string sagaId, Money danio, Ref personal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(personal);

        using var turno = _turno.TryAcquire(sagaId);
        if (turno is null) return Ocupada(sagaId);

        if (Hallar(sagaId, dueno: null) is not { } saga) return NoExiste(sagaId);
        var previo = _registro.Find(sagaId);

        if (previo?.Estado == Devuelta) return Result.Ok(Vista(saga, previo));
        if (saga.Status != SagaStatus.Completed || previo?.Estado == Cancelada)
        {
            return Result.Rejected<VistaDeLiquidacion>(Rejection.Conflict("alquiler.not_returnable",
                $"El alquiler {sagaId} no está confirmado ni declarado: no hay devolución que confirmar."));
        }

        var garantia = Garantia(saga);
        if (danio.Amount < 0m || !string.Equals(danio.Currency, garantia.Currency, StringComparison.Ordinal))
        {
            return Result.Rejected<VistaDeLiquidacion>(Rejection.Invalid("alquiler.bad_amount",
                $"El daño tiene que ser cero o más, en {garantia.Currency}."));
        }
        if (danio.Amount > garantia.Amount)
        {
            return Result.Rejected<VistaDeLiquidacion>(Rejection.Invalid("alquiler.damage_exceeds_deposit",
                $"El daño ({danio.Amount}) supera la garantía retenida ({garantia.Amount})."));
        }

        if (Texto(saga, "garantiaId") is { } cobro)
        {
            var movida = await MoverGarantiaAsync(saga.Id, cobro, garantia, danio, ct);
            if (movida is { } no) return Result.Rejected<VistaDeLiquidacion>(no);
        }

        var devuelta = (previo ?? new LiquidacionRegistrada(sagaId, Devuelta, null, null, null, null, null)) with
        {
            Estado = Devuelta,
            ConfirmadaUtc = _reloj.GetUtcNow(),
            Danio = danio,
            GarantiaLiberada = Money.Of(garantia.Amount - danio.Amount, garantia.Currency),
            Personal = personal,
        };
        _registro.Put(devuelta);
        _log.LogInformation("Alquiler {Saga}: devolución confirmada; daño {Danio}.", sagaId, danio.Amount);
        return Result.Ok(Vista(saga, devuelta));
    }

    /// <summary>
    /// Cancela un alquiler confirmado (research R7, 005-P3): cancela las reservas, DEVUELVE lo cobrado entero y anula
    /// la garantía —o, si cancela el personal con penalidad, la captura y devuelve G − P—.
    /// </summary>
    /// <remarks>
    /// <para><b>El inquilino</b> (el dueño, por la puerta) cancela antes del día de inicio en el huso del despliegue y
    /// sin penalidad; con la devolución declarada, la resuelve el personal. <b>El personal</b>, siempre que no esté
    /// devuelto, con la penalidad que fije.</para>
    ///
    /// <para><b>Las reservas se leen de las compensaciones</b> que dejó el cierre (<c>booking.cancelar</c> sobre cada
    /// una; al completar quedan dadas por hechas): la ranura de la saga guarda los apartados, no las reservas, y una
    /// saga cerrada no se compensa —cancelar es una operación con su política, no un deshacer—. Es pegamento atribuido
    /// a NN-8 (T051 del núcleo).</para>
    /// </remarks>
    /// <param name="sagaId">El alquiler.</param>
    /// <param name="quien">Quién cancela.</param>
    /// <param name="penalidad">Lo que retiene el personal; la del inquilino se ignora.</param>
    /// <param name="dueno">El sujeto de la puerta, si cancela el inquilino: tiene que ser el dueño.</param>
    /// <param name="personal">Quién cancela, si es el personal.</param>
    /// <param name="ct">Cancelación.</param>
    public async Task<Result<VistaDeLiquidacion>> CancelarAsync(
        string sagaId, QuienCancela quien, Money penalidad, Ref? dueno, Ref? personal, CancellationToken ct)
    {
        using var turno = _turno.TryAcquire(sagaId);
        if (turno is null) return Ocupada(sagaId);

        var porElInquilino = quien == QuienCancela.Inquilino;
        if (porElInquilino && dueno is null) return NoExiste(sagaId);
        if (Hallar(sagaId, porElInquilino ? dueno : null) is not { } saga) return NoExiste(sagaId);
        var previo = _registro.Find(sagaId);

        if (previo?.Estado == Cancelada) return Result.Ok(Vista(saga, previo));
        if (saga.Status != SagaStatus.Completed || previo?.Estado == Devuelta)
        {
            return Result.Rejected<VistaDeLiquidacion>(Rejection.Conflict("alquiler.not_cancellable",
                $"El alquiler {sagaId} no está confirmado o ya se devolvió: no hay nada que cancelar."));
        }

        var garantia = Garantia(saga);
        if (porElInquilino)
        {
            if (previo?.Estado == Declarada)
            {
                return Result.Rejected<VistaDeLiquidacion>(Rejection.Conflict("alquiler.not_cancellable",
                    $"El alquiler {sagaId} tiene la devolución declarada: lo resuelve el personal."));
            }
            if (Ventana(saga) is not { } ventana || _huso.YaEmpezo(ventana, _reloj.GetUtcNow()))
            {
                return Result.Rejected<VistaDeLiquidacion>(Rejection.Conflict("alquiler.already_started",
                    $"El alquiler {sagaId} ya empezó: desde el día de inicio lo cancela el personal."));
            }
            penalidad = Money.Zero(garantia.Currency);
        }

        if (penalidad.Amount < 0m || !string.Equals(penalidad.Currency, garantia.Currency, StringComparison.Ordinal))
        {
            return Result.Rejected<VistaDeLiquidacion>(Rejection.Invalid("alquiler.bad_amount",
                $"La penalidad tiene que ser cero o más, en {garantia.Currency}."));
        }
        if (penalidad.Amount > garantia.Amount)
        {
            return Result.Rejected<VistaDeLiquidacion>(Rejection.Invalid("alquiler.damage_exceeds_deposit",
                $"La penalidad ({penalidad.Amount}) supera la garantía retenida ({garantia.Amount})."));
        }

        // Al completar, el intérprete da por hechas las compensaciones armadas; el KIND sigue diciendo qué cierre
        // produjo una reserva (el «despues» de apartar), y su objetivo es la reserva.
        foreach (var reserva in saga.Compensations.Where(c => c.Kind == "booking.cancelar").Select(c => c.TargetId).Distinct(StringComparer.Ordinal))
        {
            var cancelada = await _agenda.CancelarAsync(reserva, ct);
            if (!cancelada.IsOk) return Result.Rejected<VistaDeLiquidacion>(cancelada.Rejection!);
        }

        var total = Total(saga);
        if (Texto(saga, "cobroId") is { } cobro && !total.IsZero)
        {
            var devuelto = await _cobros.DevolverAsync(cobro, total, "alquiler cancelado", IdempotencyKey.From(saga.Id, "liquidar:cancelar"), ct);
            if (!devuelto.IsOk) return Result.Rejected<VistaDeLiquidacion>(devuelto.Rejection!);
        }

        if (Texto(saga, "garantiaId") is { } deposito)
        {
            var movida = await MoverGarantiaAsync(saga.Id, deposito, garantia, penalidad, ct);
            if (movida is { } no) return Result.Rejected<VistaDeLiquidacion>(no);
        }

        var cancelado = (previo ?? new LiquidacionRegistrada(sagaId, Cancelada, null, null, null, null, null)) with
        {
            Estado = Cancelada,
            CanceladaUtc = _reloj.GetUtcNow(),
            Penalidad = penalidad,
            GarantiaLiberada = Money.Of(garantia.Amount - penalidad.Amount, garantia.Currency),
            AlquilerDevuelto = total,
            Personal = porElInquilino ? null : personal,
        };
        _registro.Put(cancelado);
        _log.LogInformation("Alquiler {Saga}: cancelado por {Quien}; penalidad {Penalidad}.", sagaId, quien, penalidad.Amount);
        return Result.Ok(Vista(saga, cancelado));
    }

    /// <summary>Anula la garantía, o la captura y devuelve el sobrante. El rechazo, si algo no salió.</summary>
    private async Task<Rejection?> MoverGarantiaAsync(string sagaId, string cobro, Money garantia, Money danio, CancellationToken ct)
    {
        if (danio.IsZero)
        {
            var anulada = await _cobros.AnularAsync(cobro, ct);
            return anulada.IsOk ? null : anulada.Rejection;
        }

        var capturada = await _pagos.CapturarAsync(cobro, IdempotencyKey.From(sagaId, "liquidar:capturar"), ct);
        if (!capturada.IsOk)
        {
            return YaNoAutorizada.Contains(capturada.Rejection!.Code, StringComparer.Ordinal)
                ? Rejection.Conflict("alquiler.retention_expired",
                    "La garantía ya no está retenida en la pasarela: no hay de dónde cobrar el daño.")
                : capturada.Rejection;
        }

        var sobrante = Money.Of(garantia.Amount - danio.Amount, garantia.Currency);
        if (sobrante.IsZero) return null;

        // Sin compensación a la vista: si devolver falla, el rechazo sale hacia el personal, que repite; la llave
        // derivada no captura otra vez ni devuelve dos veces.
        var devuelta = await _cobros.DevolverAsync(cobro, sobrante, "garantía no consumida", IdempotencyKey.From(sagaId, "liquidar:devolver"), ct);
        return devuelta.IsOk ? null : devuelta.Rejection;
    }

    /// <summary>La saga de la renta, si existe y —con <paramref name="dueno"/>— es suya; si no, nula.</summary>
    private SagaDeFlujo? Hallar(string sagaId, Ref? dueno)
    {
        _motor.Refrescar();
        var saga = string.IsNullOrWhiteSpace(sagaId) ? null : _motor.Find(sagaId);
        if (saga is null || !string.Equals(saga.Flujo, Flujo, StringComparison.Ordinal)) return null;
        return dueno is null || saga.Dueno == dueno ? saga : null;
    }

    private static VistaDeLiquidacion Vista(SagaDeFlujo saga, LiquidacionRegistrada? registro)
    {
        var garantia = Garantia(saga);
        var estado = registro?.Estado switch
        {
            Declarada => "declared",
            Devuelta => "returned",
            Cancelada => "cancelled",
            _ => "reserved",
        };
        var retenida = registro?.Estado is Devuelta or Cancelada ? Money.Zero(garantia.Currency) : garantia;
        return new VistaDeLiquidacion(saga.Id, estado, garantia, retenida, registro);
    }

    /// <summary>La garantía que se retuvo al reservar; cero si el equipo no lleva (el paso se omitió).</summary>
    private static Money Garantia(SagaDeFlujo saga)
    {
        if (saga.Datos.GetValueOrDefault("garantia")?.Valor is { ValueKind: JsonValueKind.Object } v)
        {
            return Money.Of(v.GetProperty("amount").GetDecimal(), v.GetProperty("currency").GetString()!);
        }
        var total = saga.Datos.GetValueOrDefault("total")?.Valor;
        return Money.Zero(total is { ValueKind: JsonValueKind.Object } t ? t.GetProperty("currency").GetString()! : Money.Cop);
    }

    /// <summary>Lo que se cobró por el alquiler; cero si no consta.</summary>
    private static Money Total(SagaDeFlujo saga)
        => saga.Datos.GetValueOrDefault("total")?.Valor is { ValueKind: JsonValueKind.Object } t
            ? Money.Of(t.GetProperty("amount").GetDecimal(), t.GetProperty("currency").GetString()!)
            : Money.Zero(Garantia(saga).Currency);

    /// <summary>La ventana del alquiler, o nula si la saga no la guardó (antes de T020 no estaba en la salida).</summary>
    private static TimeWindow? Ventana(SagaDeFlujo saga)
        => saga.Datos.GetValueOrDefault("ventana")?.Valor is { ValueKind: JsonValueKind.Object } v
            ? (TimeWindow)new Synergos.Bff.Core.Flow.Ventana().Leer(v, $"la saga {saga.Id}, «ventana»")
            : null;

    private static string? Texto(SagaDeFlujo saga, string nombre)
        => saga.Datos.GetValueOrDefault(nombre)?.Valor is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    private static Result<VistaDeLiquidacion> NoExiste(string sagaId)
        => Result.Rejected<VistaDeLiquidacion>(Rejection.NotFound("alquiler.saga_not_found", $"No existe el alquiler {sagaId}."));

    private static Result<VistaDeLiquidacion> Ocupada(string sagaId)
        => Result.Rejected<VistaDeLiquidacion>(Rejection.Unavailable("alquiler.settlement_in_flight",
            $"Otra operación está liquidando el alquiler {sagaId}; vuelve a intentarlo."));
}
