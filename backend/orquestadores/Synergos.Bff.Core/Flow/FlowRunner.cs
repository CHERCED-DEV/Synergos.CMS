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
///   butaca, las dos primeras ya tienen la compensación buena. Si el cierre produjo su propio
///   objetivo (<see cref="SalidaDePaso.Cierra"/>), la de después es sobre ése.</item>
///   <item>Cada reserva se guarda y se lee por el nombre del paso que la hizo: un flujo puede
///   llevar varias únicas —la hora y el cobro— y varias por ítem, y cada una se cierra y se
///   deshace por su lado.</item>
///   <item>Sólo se continúa una saga <c>Running</c>. Las guardas con los códigos de cada dominio
///   son de la fachada; ésta es la red para la fachada que se las olvide, porque continuar una
///   saga ya completada volvería a consumir lo consumido.</item>
///   <item>Un paso que falla después de nacer la saga la deshace con su motivo y devuelve el
///   rechazo ORIGINAL de la capacidad. Salvo uno con <c>al_fallar: seguir</c>, que el validador
///   sólo admite después del último cierre: ése se anota —la saga y el código, nada más— y la
///   fase sigue, porque ya no queda nada que deshacer y deshacer devolvería una compra hecha.</item>
///   <item>Un paso que reserva con <c>omitir_si_cero</c> no corre cuando ese monto es cero: no reserva
///   nada, deja nulo lo que escribiría, y su cierre se salta solo (la regla de la reserva que nunca
///   se hizo). Es lo gratis, que <c>Api.Payments</c> rechaza como cobro.</item>
///   <item>Lo efímero de una fase (<see cref="IFlowBinding{TSaga}.Efimera"/>) lo tiene que poner la
///   fachada antes del primer paso, y vive sólo en el contexto de esa llamada.</item>
///   <item>Al terminar la última fase, la saga queda <c>Completed</c> y lo que estaba armado,
///   hecho: el barrido no tiene nada que intentar.</item>
///   <item><b>Una saga, un turno.</b> Cada fase corre con el MISMO arriendo con el que se deshace
///   una saga (<see cref="ISagaLease"/>, #34), tomado antes de leerla y soltado al terminar. Así
///   ninguna fase se intercala con otra fase de la misma saga ni con su compensación —la del
///   barrido, la de abandono o la que pide quien compra—: sin turno, una fase escribía su copia en
///   memoria encima de lo que la otra decidió, y un cierre terminaba <c>Completed</c> sobre una
///   saga ya devuelta. Quien no consigue el turno recibe <see cref="Ocupada"/>, transitorio y sin
///   tocar nada; el arriendo vence solo, así que un proceso muerto a media fase no deja la saga
///   trabada.</item>
/// </list>
/// </remarks>
public sealed class FlowRunner<TSaga> where TSaga : class, ISaga<TSaga>
{
    /// <summary>El código con el que se rechaza continuar una saga que ya no está en curso.</summary>
    public const string NoEnCurso = "flow.not_running";

    /// <summary>
    /// El código con el que se rechaza una fase mientras otra operación tiene el turno de la saga.
    /// </summary>
    /// <remarks>
    /// <b>Transitorio (<c>Unavailable</c>) y no un conflicto</b>, como <c>compensation_in_flight</c>:
    /// no es que la fase no se pueda hacer, es que hay otra en curso. Quien lo recibe vuelve —el doble
    /// clic, el reintento tras un 504— y entonces encuentra la saga como la dejó la otra: completada
    /// (y la fachada contesta idempotente), deshecha, o en curso para seguir.
    /// </remarks>
    public const string Ocupada = "flow.busy";

    private readonly SagaEngine<TSaga> _motor;
    private readonly IRegistroDePasos _pasos;
    private readonly FlujoDef _flujo;
    private readonly IFlowBinding<TSaga> _binding;
    private readonly TimeProvider _reloj;
    private readonly ILogger _log;

    /// <param name="motor">La máquina de sagas del dominio.</param>
    /// <param name="pasos">Los pasos registrados.</param>
    /// <param name="flujo">La definición.</param>
    /// <param name="binding">Cómo la guarda la saga del dominio.</param>
    /// <param name="fases">Las fases que la fachada invoca, en orden: las mismas que declara al
    /// registrar el flujo. Tienen que ser las de la definición, así que una que no declaró no
    /// existe.</param>
    /// <param name="reloj">El reloj.</param>
    /// <param name="log">El log de la fachada.</param>
    /// <exception cref="InvalidOperationException">Si la definición no pasa <see cref="FlowValidator"/>
    /// contra estos pasos, este binding y estas fases: un intérprete no se construye sobre un flujo
    /// roto.</exception>
    public FlowRunner(
        SagaEngine<TSaga> motor, IRegistroDePasos pasos, FlujoDef flujo,
        IFlowBinding<TSaga> binding, IReadOnlyList<string> fases, TimeProvider reloj, ILogger log)
    {
        var errores = FlowValidator.Validar(flujo, pasos, ContratoDelFlujo.De(binding, fases));
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
    /// <see cref="IFlowBinding{TSaga}.Leer"/> reconstruye de la saga, y gana; pero el validador sólo
    /// cuenta con lo que el binding declara efímero para esa fase.</param>
    /// <param name="ct">Cancelación.</param>
    /// <returns>La saga como quedó, o el rechazo ORIGINAL del paso que falló; <see cref="NoEnCurso"/>
    /// si una fase que continúa encuentra la saga en otro estado que <c>Running</c>;
    /// <see cref="Ocupada"/> si otra operación tiene el turno de la saga, o si una fase que abre la
    /// encuentra ya escrita por otra llamada con la misma llave.</returns>
    /// <exception cref="InvalidOperationException">Si la fase no existe, o si una fase que continúa
    /// no encuentra la saga: eso lo tiene que haber resuelto quien llama, con sus propios códigos de
    /// rechazo.</exception>
    public async Task<Result<TSaga>> EjecutarFaseAsync(
        string fase, string sagaId, FlowContext entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        var definicion = _flujo.Fase(fase);
        var abre = ReferenceEquals(definicion, _flujo.Fases[0]);

        // Lo efímero lo pone la fachada en cada llamada, aunque sea nulo: el validador aceptó las
        // lecturas de esta fase contando con ello, y es mejor saberlo antes de llamar a nadie.
        if (_binding.Efimera.GetValueOrDefault(fase) is { } efimera
            && efimera.Where(n => !entrada.Has(n)).ToList() is { Count: > 0 } sinPoner)
        {
            throw new InvalidOperationException(
                $"La fachada de «{_flujo.Clave}» declara que pone {string.Join(", ", sinPoner)} en «{fase}» y no lo pone.");
        }

        // El turno ANTES de leer: lo que se lee sin él puede haberlo cambiado otra operación cuando
        // esta escriba. Se suelta al salir, también si un paso lanza.
        using var turno = _motor.TomarTurno(sagaId);
        if (turno is null) return Result.Rejected<TSaga>(SinTurno(fase, sagaId));

        // La que abre no continúa nada: la llave ya la resolvió quien llama con SagaEngine.Abrir, y
        // la saga nace nueva. Si ya está escrita, otra llamada con la misma llave la abrió mientras
        // ésta esperaba su turno: abrirla otra vez pisaría lo que la otra dejó —quizá ya cerrado—, y
        // el reintento la resuelve con Abrir como cualquier llave usada.
        if (abre && _motor.Find(sagaId) is not null) return Result.Rejected<TSaga>(SinTurno(fase, sagaId));

        var saga = abre ? null : _motor.Find(sagaId)
            ?? throw new InvalidOperationException($"La fase «{fase}» continúa la saga {sagaId} y no existe.");

        if (saga is not null && saga.Status != SagaStatus.Running)
        {
            return Result.Rejected<TSaga>(Rejection.Conflict(NoEnCurso,
                $"La fase «{fase}» sólo continúa una saga en curso, y {sagaId} está {saga.Status}."));
        }

        var ctx = saga is null ? Abrir(entrada) : Reconstruir(saga).CopiarDe(entrada);
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

        return Terminar(corrida, fase, ultima: ReferenceEquals(definicion, _flujo.Fases[^1]));
    }

    /// <summary>Lo que contesta una fase que no consiguió el turno de su saga.</summary>
    private static Rejection SinTurno(string fase, string sagaId)
        => Rejection.Unavailable(Ocupada,
            $"Otra operación sobre {sagaId} está en curso y «{fase}» no se intercala con ella. "
            + "Vuelve a intentarlo en un momento: repetirla no duplica nada.");

    /// <summary>Lo que la fase que abre tiene al empezar: la entrada que puso la fachada.</summary>
    /// <exception cref="InvalidOperationException">Si la fachada no pone todo lo que el binding declara
    /// que pone al abrir: el validador aceptó la <c>entrada</c> contando con ello.</exception>
    private FlowContext Abrir(FlowContext entrada)
    {
        var faltan = _binding.PoneAlAbrir.Where(n => !entrada.Has(n)).ToList();
        if (faltan.Count > 0)
        {
            throw new InvalidOperationException(
                $"La fachada de «{_flujo.Clave}» declara que pone {string.Join(", ", faltan)} al abrir y no lo pone.");
        }
        return entrada;
    }

    /// <summary>Lo que la fase que continúa tiene al empezar: lo que el binding reconstruye.</summary>
    /// <exception cref="InvalidOperationException">Si no pone todo lo que declara: el validador
    /// aceptó lecturas contando con ello, y es mejor fallar acá —antes de llamar a nadie— que en
    /// el paso que lo lea, con la plata capturada.</exception>
    private FlowContext Reconstruir(TSaga saga)
    {
        var ctx = _binding.Leer(saga);
        var faltan = _binding.Reconstruye.Where(n => !ctx.Has(n)).ToList();
        if (faltan.Count > 0)
        {
            throw new InvalidOperationException(
                $"El binding de «{_flujo.Clave}» declara que reconstruye {string.Join(", ", faltan)} y no lo pone.");
        }
        return ctx;
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

        // Que la saga sepa leer cada reserva que el binding declara, AHORA: es el último momento en
        // que no hay nada reservado. Si no sabe, lanzaría al cerrar esa reserva —después de
        // capturar, con la compensación sin cambiar de carácter—. La ranura es del record del
        // dominio y el arranque no tiene una saga que preguntarle; ésta es la primera.
        foreach (var paso in _binding.Reservas.Keys) _ = Ledger(corrida).Legs(paso);
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

    private static IHoldLedger Ledger(Corrida corrida) => (IHoldLedger)corrida.Saga!;

    private static IReadOnlyList<FlowContext> Items(ParaCadaDef bloque, Corrida corrida)
    {
        if (!bloque.Fuente.StartsWith(FlowValidator.Reservas, StringComparison.Ordinal))
        {
            return corrida.Ctx.Get<IReadOnlyList<FlowContext>>(bloque.Fuente);
        }

        return Ledger(corrida).Legs(bloque.Fuente[FlowValidator.Reservas.Length..])
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
        if (definicion.CierraReserva is { } unica && item is null && Ledger(corrida).Legs(unica).Count == 0)
        {
            return Desenlace.Sigue;
        }

        // Un cobro de cero no es un cobro: el paso no corre, no reserva, y lo que escribiría queda
        // nulo —como al saltar la fase— para que nadie lea un valor que no se produjo.
        if (definicion.OmitirSiCero is { } monto
            && Ambito.Leer<Money?>(monto, corrida.Ctx, alias, item) is { IsZero: true })
        {
            foreach (var nombre in definicion.Escribe) Ambito.Escribir(nombre, null, corrida.Ctx, alias, item);
            return Desenlace.Sigue;
        }

        var paso = _pasos.Para(definicion.Tipo)!;
        SalidaDePaso salida;
        try
        {
            salida = await paso.EjecutarAsync(
                new EntradaDePaso(corrida.SagaId, corrida.Origen, definicion, corrida.Ctx, alias, item), ct);
        }
        catch (Exception ex) when (definicion.SigueSiFalla && !(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            // Sólo el tipo: el mensaje puede llevar lo que el paso recibió, y lo efímero no se escribe
            // en ningún sitio, tampoco en el log.
            _log.LogWarning("El flujo {Flujo} sigue sin el paso {Paso} en la saga {Saga}: lanzó {Tipo}.",
                _flujo.Clave, definicion.Id, corrida.SagaId, ex.GetType().Name);
            return Desenlace.Sigue;
        }

        if (salida.Rechazo is { } rechazo)
        {
            if (definicion.SigueSiFalla)
            {
                _log.LogWarning("El flujo {Flujo} sigue sin el paso {Paso} en la saga {Saga}: {Codigo}.",
                    _flujo.Clave, definicion.Id, corrida.SagaId, rechazo.Code);
                return Desenlace.Sigue;
            }
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
        if (definicion.CierraReserva is { } cerrada)
        {
            CerrarReserva(corrida, cerrada, _flujo.Pasos[cerrada].Reserva!, item, salida.Cerrado);
        }

        return new Desenlace(salida.Control, null, null);
    }

    /// <summary>Guarda lo reservado en la saga y su compensación ARMADA, en la misma escritura.</summary>
    private void Reservar(Corrida corrida, PasoDef definicion, ReservaDef reserva, SalidaDePaso salida, FlowContext? item)
    {
        var hecho = salida.Reservado
            ?? throw new InvalidOperationException(
                $"El paso «{definicion.Id}» declara una reserva y «{definicion.Tipo}» no dijo qué reservó.");

        var saga = _binding.ConReserva(corrida.Saga!, definicion.Id, new HoldLeg(hecho.Id, hecho.CierreId ?? hecho.Id), item);

        Guardar(corrida, saga.WithCompensations(saga.Compensations
            .Append(Compensation.For(reserva.Antes, hecho.Id, reserva.Motivo ?? $"{_flujo.Clave} no se completó"))
            .ToList()));
    }

    /// <summary>
    /// Le cambia el carácter a la compensación de lo que se acaba de consumir: <c>antes</c> → <c>despues</c>.
    /// </summary>
    /// <remarks>
    /// Cambia la compensación pendiente del kind <c>antes</c> sobre ESA reserva —la única, o la del
    /// ítem del bloque—, y no la de otra reserva que se deshaga igual: anular una autorización ya
    /// capturada no se puede, devolverla sí. La de después apunta a lo que el cierre produjo, o al
    /// objetivo anotado al reservar —el pozo, porque el apartado consumido ya no existe—. Sin
    /// <c>despues</c>, consumir deja la compensación hecha.
    /// </remarks>
    private void CerrarReserva(Corrida corrida, string reservante, ReservaDef reserva, FlowContext? item, string? objetivo)
    {
        var saga = corrida.Saga!;
        var cerrada = item is not null
            ? new HoldLeg(item.Get<string>(HoldLeg.CampoHold), item.Get<string>(HoldLeg.CampoCierre))
            : Ledger(corrida).Legs(reservante).Single();
        DateTimeOffset? ahora = reserva.Despues is null ? _reloj.GetUtcNow() : null;

        Guardar(corrida, saga.WithCompensations(saga.Compensations
            .Select(c => c.Kind == reserva.Antes && c.IsPending && c.TargetId == cerrada.HoldId
                ? reserva.Despues is { } despues
                    ? c with { Kind = despues, TargetId = objetivo ?? cerrada.CloseTargetId }
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

    private Result<TSaga> Terminar(Corrida corrida, string fase, bool ultima)
    {
        var saga = corrida.Saga!;
        if (!ultima)
        {
            if (corrida.SinGuardar) Guardar(corrida, saga);
            return Result.Ok(corrida.Saga!);
        }

        // Completar es la escritura que no se puede deshacer, y se hace contra el DISCO, no contra la
        // copia de esta corrida: si el turno venció mientras un paso tardaba más que el arriendo y
        // otro deshizo la saga, escribir Completed encima dejaría entradas emitibles sobre un cobro
        // devuelto. Lo que otro decidió gana, y se grita.
        if (_motor.Find(corrida.SagaId) is { Status: not SagaStatus.Running } enDisco)
        {
            _log.LogError(
                "El flujo {Flujo} terminó «{Fase}» sobre la saga {Saga} y la encontró {Estado}: no se completa. Su turno venció antes de terminar.",
                _flujo.Clave, fase, corrida.SagaId, enDisco.Status);
            return Result.Rejected<TSaga>(Rejection.Conflict(NoEnCurso,
                $"La fase «{fase}» terminó sobre {corrida.SagaId} y la saga ya está {enDisco.Status}."));
        }

        // Salió: ya no hay nada que deshacer. Lo armado se marca hecho para que el barrido no lo
        // intente — armada no es pendiente, pero una saga completada tampoco tiene nada armado.
        //
        // Todo lo armado, sin excepciones, y eso deja fuera a propósito la confirmación PARCIAL de
        // Viajes (#40): allí el apartado de un ítem no cumplido que no se pudo soltar sigue
        // pendiente al completar, para que el barrido lo reintente. Expresarlo exige que el dominio
        // marque qué compensaciones siguen vivas; queda diferido hasta portar ese flujo.
        var ahora = _reloj.GetUtcNow();
        Guardar(corrida, _binding.ConError(saga
            .WithStatus(SagaStatus.Completed)
            .WithCompensations(saga.Compensations.Select(c => c.IsPending ? c with { DoneAtUtc = ahora } : c).ToList()),
            null));
        return Result.Ok(corrida.Saga!);
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

        // Con el turno que ya tiene esta fase: el arriendo no es reentrante, y pedirlo otra vez
        // dejaría la compensación en línea esperándose a sí misma hasta que la hiciera el barrido.
        await _motor.CompensarConTurnoAsync(saga.Id, motivo, ct);
        return Result.Rejected<TSaga>(rechazo);
    }
}
