using Microsoft.Extensions.Logging;
using Synergos.Core;

namespace Synergos.Bff.Core.Flow;

/// <summary>
/// El intérprete de un flujo declarado: da sus pasos en orden y anota cómo deshacer cada uno.
/// </summary>
/// <typeparam name="TSaga">La saga del dominio.</typeparam>
/// <remarks>
/// <para><b>Hace HACIA ADELANTE lo que cada <c>*Flow.cs</c> escribía a mano, y nada de deshacer.</b>
/// Deshacer sigue siendo <see cref="SagaEngine{TSaga}.CompensateAsync"/> con el
/// <see cref="ICompensationExecutor{TSaga}"/> del dominio, intactos: el intérprete sólo deja los
/// mismos datos que dejaba el flujo imperativo —las mismas compensaciones, con los mismos kinds y
/// objetivos, en el mismo instante—, así que una saga guardada antes se deshace igual después, y al
/// revés.</para>
///
/// <para><b>Las reglas que antes eran comentarios, ahora son el código de una vez:</b></para>
/// <list type="bullet">
///   <item>La saga nace justo antes del primer tramo que reserva algo. Lo de antes —revisar,
///   cotizar— rechaza sin saga: un precio que no se pudo cotizar no deja una compra deshecha de
///   más.</item>
///   <item>Cada reserva se anota compensable EN EL ACTO, con su <c>antes</c>, y se guarda: si el
///   proceso se cae en la línea cuatro, las tres primeras ya tienen quién las suelte.</item>
///   <item>Cerrar una reserva le cambia el carácter a su compensación (<c>antes</c> →
///   <c>despues</c>) en el acto, también dentro del bloque: si el consumo falla en la tercera
///   butaca, las dos primeras ya tienen la compensación buena.</item>
///   <item>Un paso que falla después de nacer la saga la deshace con su motivo y devuelve el
///   rechazo ORIGINAL de la capacidad.</item>
///   <item>Al terminar la última fase, la saga queda <c>Completed</c> y lo que estaba armado,
///   hecho: el barrido no tiene nada que intentar.</item>
/// </list>
/// </remarks>
public sealed class FlowRunner<TSaga> where TSaga : class, ISaga<TSaga>
{
    private readonly SagaEngine<TSaga> _motor;
    private readonly IRegistroDePasos _pasos;
    private readonly FlujoDef _flujo;
    private readonly IFlowBinding<TSaga> _binding;
    private readonly TimeProvider _reloj;
    private readonly ILogger _log;

    /// <exception cref="InvalidOperationException">Si la definición no pasa <see cref="FlowValidator"/>
    /// contra estos pasos y esta saga: un intérprete no se construye sobre un flujo roto.</exception>
    public FlowRunner(
        SagaEngine<TSaga> motor, IRegistroDePasos pasos, FlujoDef flujo,
        IFlowBinding<TSaga> binding, TimeProvider reloj, ILogger log)
    {
        var errores = FlowValidator.Validar(flujo, pasos, typeof(TSaga));
        if (errores.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errores));
        }

        _motor = motor;
        _pasos = pasos;
        _flujo = flujo;
        _binding = binding;
        _reloj = reloj;
        _log = log;
    }

    /// <summary>Lo que una fase va llevando: la saga si ya nació, el contexto, y si hay algo sin guardar.</summary>
    private sealed class Corrida
    {
        public Corrida(string sagaId, Ref origen, FlowContext ctx, TSaga? saga)
        {
            SagaId = sagaId;
            Origen = origen;
            Ctx = ctx;
            Saga = saga;
        }

        public string SagaId { get; }
        public Ref Origen { get; }
        public FlowContext Ctx { get; }
        public TSaga? Saga { get; set; }
        public bool SinGuardar { get; set; }
    }

    /// <summary>Cómo terminó un paso o un bloque.</summary>
    private readonly record struct Desenlace(PasoResultado Control, Rejection? Rechazo, string? Motivo)
    {
        public static Desenlace Sigue => new(PasoResultado.Continuar, null, null);
    }

    /// <summary>
    /// Ejecuta una fase. La primera ABRE la saga <paramref name="sagaId"/>; las demás la continúan.
    /// </summary>
    /// <param name="fase">El nombre de la fase.</param>
    /// <param name="sagaId">La saga. Quien llama ya resolvió su llave con <see cref="SagaEngine{TSaga}.Abrir"/>.</param>
    /// <param name="entrada">Lo que pone el llamador. En una fase que continúa se suma a lo que
    /// <see cref="IFlowBinding{TSaga}.Leer"/> reconstruye de la saga, y gana.</param>
    /// <param name="ct">Cancelación.</param>
    /// <exception cref="InvalidOperationException">Si una fase que continúa no encuentra la saga:
    /// eso lo tiene que haber resuelto quien llama, con sus propios códigos de rechazo.</exception>
    public async Task<Result<TSaga>> EjecutarFaseAsync(
        string fase, string sagaId, FlowContext entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        var definicion = _flujo.Fase(fase);
        var abre = ReferenceEquals(definicion, _flujo.Fases[0]);

        // La que abre NO relee: la llave ya la resolvió quien llama con SagaEngine.Abrir, y la
        // saga nace nueva aunque una llamada simultánea con la misma llave ya hubiera escrito — el
        // flujo imperativo hacía lo mismo, y las llaves de cada paso son las que evitan duplicar.
        var saga = abre ? null : _motor.Find(sagaId)
            ?? throw new InvalidOperationException($"La fase «{fase}» continúa la saga {sagaId} y no existe.");

        var ctx = saga is null ? entrada : _binding.Leer(saga).CopiarDe(entrada);
        var corrida = new Corrida(sagaId, Ref.Create(_flujo.Clave, sagaId), ctx, saga);
        var siembra = abre ? PrimerTramoQueReserva(definicion) : -1;

        for (var i = 0; i < definicion.Pasos.Count; i++)
        {
            if (i == siembra) Sembrar(corrida);

            var elemento = definicion.Pasos[i];
            var desenlace = elemento.ParaCada is { } bloque
                ? await BloqueAsync(bloque, corrida, ct)
                : await PasoAsync(_flujo.Pasos[elemento.Paso!], corrida, null, null, ct);

            if (desenlace.Rechazo is { } rechazo) return await FallarAsync(corrida, rechazo, desenlace.Motivo!, ct);
            if (desenlace.Control == PasoResultado.SaltarFase) break;
        }

        // Una fase que abre y no reserva nada —o que saltó antes de reservar— igual deja su saga.
        if (corrida.Saga is null) Sembrar(corrida);

        return Result.Ok(Terminar(corrida, ultima: ReferenceEquals(definicion, _flujo.Fases[^1])));
    }

    /// <summary>El índice del primer elemento que reserva, o el final de la fase si ninguno.</summary>
    private int PrimerTramoQueReserva(FaseDef fase)
    {
        for (var i = 0; i < fase.Pasos.Count; i++)
        {
            var ids = fase.Pasos[i].ParaCada?.Pasos ?? new[] { fase.Pasos[i].Paso! };
            if (ids.Any(id => _flujo.Pasos[id].Reserva is not null)) return i;
        }
        return fase.Pasos.Count;
    }

    private void Sembrar(Corrida corrida)
    {
        // Sin guardar: la guarda el primer paso que reserve, o el fallo que la deshaga. Es lo que
        // hacía el flujo imperativo, y guardarla acá añadiría una escritura que nadie leía.
        corrida.Saga = _binding.Crear(corrida.SagaId, corrida.Ctx, _reloj.GetUtcNow());
        corrida.SinGuardar = true;
    }

    private async Task<Desenlace> BloqueAsync(ParaCadaDef bloque, Corrida corrida, CancellationToken ct)
    {
        // Los ítems se toman UNA vez, al entrar: el bloque reescribe la saga en cada vuelta, y
        // recorrer la lista nueva cada vez haría depender el recorrido de lo que el propio bloque
        // va cambiando.
        foreach (var item in Items(bloque, corrida))
        {
            foreach (var id in bloque.Pasos)
            {
                var desenlace = await PasoAsync(_flujo.Pasos[id], corrida, bloque.Como, item, ct);
                if (desenlace.Rechazo is not null || desenlace.Control == PasoResultado.SaltarFase) return desenlace;
            }
        }

        return Desenlace.Sigue;
    }

    private static IReadOnlyList<FlowContext> Items(ParaCadaDef bloque, Corrida corrida)
    {
        if (!bloque.Fuente.StartsWith(FlowValidator.Reservas, StringComparison.Ordinal))
        {
            return corrida.Ctx.Get<IReadOnlyList<FlowContext>>(bloque.Fuente);
        }

        return ((IHoldLedger)corrida.Saga!).Legs
            .Select(l => new FlowContext()
                .Set(HoldLeg.CampoHold, l.HoldId)
                .Set(HoldLeg.CampoCierre, l.CloseTargetId))
            .ToList();
    }

    private async Task<Desenlace> PasoAsync(
        PasoDef definicion, Corrida corrida, string? alias, FlowContext? item, CancellationToken ct)
    {
        // Cerrar una reserva única que nunca se hizo no tiene nada que cerrar: es el
        // `if (saga.PaymentId is { } id)` de cada ConfirmAsync. Una compra que se cayó entre apartar
        // y autorizar quedó Running sin cobro, y confirmarla consume el aforo sin capturar nada.
        if (definicion.CierraReserva is not null && item is null
            && ((IChargeLedger)corrida.Saga!).ChargeRef is null)
        {
            return Desenlace.Sigue;
        }

        var paso = _pasos.Para(definicion.Tipo)!;
        var salida = await paso.EjecutarAsync(
            new EntradaDePaso(corrida.SagaId, corrida.Origen, definicion, corrida.Ctx, alias, item), ct);

        if (salida.Rechazo is { } rechazo)
        {
            return new Desenlace(PasoResultado.Abortar, rechazo, definicion.Motivo ?? $"falló el paso «{definicion.Id}»");
        }

        if (salida.Valores.Count != definicion.Escribe.Count)
        {
            throw new InvalidOperationException(
                $"«{definicion.Tipo}» produjo {salida.Valores.Count} valor(es) y declaró {paso.Escrituras}.");
        }
        for (var i = 0; i < salida.Valores.Count; i++)
        {
            Ambito.Escribir(definicion.Escribe[i], salida.Valores[i], corrida.Ctx, alias, item);
        }

        if (definicion.Reserva is { } reserva) Reservar(corrida, definicion, reserva, salida, item);
        if (definicion.CierraReserva is { } cerrada) CerrarReserva(corrida, _flujo.Pasos[cerrada].Reserva!, item);

        return new Desenlace(salida.Control, null, null);
    }

    /// <summary>Guarda lo reservado en la saga y su compensación ARMADA, en la misma escritura.</summary>
    private void Reservar(Corrida corrida, PasoDef definicion, ReservaDef reserva, SalidaDePaso salida, FlowContext? item)
    {
        var hecho = salida.Reservado
            ?? throw new InvalidOperationException(
                $"El paso «{definicion.Id}» declara una reserva y «{definicion.Tipo}» no dijo qué reservó.");

        var saga = item is null
            ? _binding.ConCargo(corrida.Saga!, hecho.Id)
            : _binding.ConApartado(corrida.Saga!, new HoldLeg(hecho.Id, hecho.CierreId ?? hecho.Id), item);

        Guardar(corrida, saga.WithCompensations(saga.Compensations
            .Append(Compensation.For(reserva.Antes, hecho.Id, reserva.Motivo ?? $"{_flujo.Clave} no se completó"))
            .ToList()));
    }

    /// <summary>
    /// Le cambia el carácter a la compensación de lo que se acaba de consumir: <c>antes</c> → <c>despues</c>.
    /// </summary>
    /// <remarks>
    /// Una reserva única cambia TODAS sus compensaciones pendientes del kind <c>antes</c>, sobre el
    /// mismo objetivo: anular una autorización ya capturada no se puede, devolverla sí. Una por ítem
    /// cambia sólo la de ESE apartado, y pasa a apuntar al pozo: el apartado consumido ya no existe.
    /// Sin <c>despues</c>, consumir deja la compensación hecha.
    /// </remarks>
    private void CerrarReserva(Corrida corrida, ReservaDef reserva, FlowContext? item)
    {
        var saga = corrida.Saga!;
        var holdId = item?.Get<string>(HoldLeg.CampoHold);
        var cierre = item?.Get<string>(HoldLeg.CampoCierre);
        DateTimeOffset? ahora = reserva.Despues is null ? _reloj.GetUtcNow() : null;

        Guardar(corrida, saga.WithCompensations(saga.Compensations
            .Select(c => c.Kind == reserva.Antes && c.IsPending && (holdId is null || c.TargetId == holdId)
                ? reserva.Despues is { } despues
                    ? c with { Kind = despues, TargetId = cierre ?? c.TargetId }
                    : c with { DoneAtUtc = ahora }
                : c)
            .ToList()));
    }

    private void Guardar(Corrida corrida, TSaga saga)
    {
        corrida.Saga = saga;
        corrida.SinGuardar = false;
        _motor.Put(saga);
    }

    private TSaga Terminar(Corrida corrida, bool ultima)
    {
        var saga = corrida.Saga!;
        if (!ultima)
        {
            if (corrida.SinGuardar) Guardar(corrida, saga);
            return corrida.Saga!;
        }

        // Salió: ya no hay nada que deshacer. Lo armado se marca hecho para que el barrido no lo
        // intente — armada no es pendiente, pero una saga completada tampoco tiene nada armado.
        var ahora = _reloj.GetUtcNow();
        Guardar(corrida, _binding.ConError(saga
            .WithStatus(SagaStatus.Completed)
            .WithCompensations(saga.Compensations.Select(c => c.IsPending ? c with { DoneAtUtc = ahora } : c).ToList()),
            null));
        return corrida.Saga!;
    }

    /// <summary>Anota el fallo, deshace lo que ya se hizo y devuelve el rechazo original.</summary>
    /// <remarks>
    /// Antes de nacer la saga no hay nada que deshacer: el rechazo vuelve tal cual y no se escribe
    /// nada. Es la diferencia entre la tarjeta rechazada —tres butacas que soltar— y el precio que no
    /// se pudo cotizar, que no tocó el aforo de nadie.
    /// </remarks>
    private async Task<Result<TSaga>> FallarAsync(Corrida corrida, Rejection rechazo, string motivo, CancellationToken ct)
    {
        if (corrida.Saga is not { } saga) return Result.Rejected<TSaga>(rechazo);

        _log.LogWarning("El flujo {Flujo} deshace la saga {Saga} ({Motivo}): {Error}", _flujo.Clave, saga.Id, motivo, rechazo);
        _motor.Put(_binding.ConError(saga, rechazo.ToString()));
        await _motor.CompensateAsync(saga.Id, motivo, ct);
        return Result.Rejected<TSaga>(rechazo);
    }
}
