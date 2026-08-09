using Microsoft.Extensions.Logging;
using Synergos.Core;

namespace Synergos.Bff.Core;

/// <summary>
/// La máquina de sagas: deshacer, reintentar, rendirse y avisar.
/// </summary>
/// <remarks>
/// <para><b>Esto es lo único que un orquestador aporta y ninguna capacidad puede aportar.</b>
/// Booking sabe apartar, Payments sabe cobrar, Inventory sabe reservar existencias — y ninguno de
/// los tres sabe en qué orden van ni qué hacer si el tercero falla después de que el segundo movió
/// plata. El ORDEN lo pone el flujo de cada dominio; la COMPENSACIÓN la pone esto, igual para
/// todos.</para>
///
/// <para><b>Por qué vive acá y no en cada BFF.</b> Vivió dentro de <c>Bff.Salud</c> mientras hubo
/// un solo consumidor, que es la regla de CLAUDE.md §6. Con el segundo, copiarla significaría
/// copiar el retroceso exponencial, la guarda de «armada no es pendiente», el aviso una-sola-vez
/// y la llave determinista — y perder una copia de cada una por cada dominio nuevo. Las
/// decisiones sutiles no sobreviven al copiar-pegar; es exactamente por eso que existe esta
/// capa.</para>
/// </remarks>
public sealed class SagaEngine<TSaga> where TSaga : class, ISaga<TSaga>
{
    private readonly ISagaStore<TSaga> _sagas;
    private readonly Compensator<TSaga> _compensator;
    private readonly CompensationAlert _alert;
    private readonly SagaVocabulary _vocabulary;
    private readonly TimeProvider _clock;
    private readonly ILogger<SagaEngine<TSaga>> _log;

    public SagaEngine(
        ISagaStore<TSaga> sagas, Compensator<TSaga> compensator, CompensationAlert alert,
        SagaVocabulary vocabulary, TimeProvider clock, ILogger<SagaEngine<TSaga>> log)
    {
        _sagas = sagas;
        _compensator = compensator;
        _alert = alert;
        _vocabulary = vocabulary;
        _clock = clock;
        _log = log;
    }

    public TSaga? Find(string id) => _sagas.Find(id);

    public void Put(TSaga saga) => _sagas.Put(saga);

    // ── La llave de idempotencia, y qué significa encontrarla (defecto #41) ──

    /// <summary>
    /// Qué hacer con una llave que quizá ya se usó: <see cref="Reusar"/> con la saga que hay que
    /// devolver tal cual, o <see cref="Id"/> con el identificador para empezar una nueva.
    /// </summary>
    public readonly record struct SagaSlot(TSaga? Reusar, string Id);

    /// <summary>
    /// Resuelve una llave de idempotencia ANTES de tocar nada, y decide si es un reintento de algo
    /// que sigue existiendo o un intento nuevo de algo que ya no está.
    /// </summary>
    /// <remarks>
    /// <para><b>Esto existe porque una llave protege contra DUPLICAR algo que existe</b>, y los dos
    /// flujos la trataban como si prohibiera volver a intentarlo. Cuando una saga terminó en
    /// <see cref="SagaStatus.Compensated"/> no queda nada que duplicar —el cupo volvió al pozo, el
    /// cobro se liberó, no se emitió nada— así que devolverla no es idempotencia: es negarse a
    /// empezar de nuevo algo que ya no está. El comprador al que le rechazaron la tarjeta quedaba
    /// <b>encerrado para siempre</b>, porque la llave se deriva de lo que compra y por lo tanto
    /// nunca cambia.</para>
    ///
    /// <para><b>Vive acá y no en cada flujo a propósito.</b> Los dos orquestadores tenían las
    /// mismas tres líneas con el mismo comentario copiado. Una regla sutil copiada dos veces se
    /// corrige una vez y se olvida la otra — que es la razón por la que esta capa existe.</para>
    ///
    /// <para><b>Qué NO desbloquea, y por qué:</b></para>
    /// <list type="bullet">
    ///   <item><see cref="SagaStatus.Running"/> y <see cref="SagaStatus.Compensating"/> — todavía
    ///   hay cupo apartado o plata retenida. Dejar arrancar otra compra ahí es pedir <b>el mismo
    ///   cupo dos veces</b>, que es justo lo que la llave venía a evitar.</item>
    ///   <item><see cref="SagaStatus.Completed"/> — salió bien. Devolverla es la idempotencia
    ///   funcionando: nadie compra dos veces lo mismo por darle dos clics.</item>
    ///   <item><see cref="SagaStatus.CompensationFailed"/> — <b>algo quedó colgado y necesita una
    ///   persona.</b> Dejar reintentar acá esconde ese estado detrás de una compra nueva, y el
    ///   cupo que no se pudo devolver se pierde sin que nadie lo mire.</item>
    /// </list>
    /// </remarks>
    public SagaSlot Abrir(string llave)
    {
        var previa = _sagas.Find(llave);
        if (previa is null) return new SagaSlot(null, llave);
        if (previa.Status != SagaStatus.Compensated) return new SagaSlot(previa, llave);

        // Se deshizo todo: se puede volver a intentar. Pero con identidad PROPIA — sobrescribir la
        // muerta borraría qué falló y, peor, las compensaciones que el barrido todavía pudiera
        // estar reintentando.
        //
        // El identificador del intento nuevo es DETERMINISTA: se busca el primer hueco desde la
        // raíz de la llave. Así un reintento por timeout del segundo intento vuelve a caer en el
        // mismo sitio y devuelve esa saga en vez de crear una tercera — que es la propiedad que
        // hacía correcta la llave derivada del carrito, y que no se puede perder al arreglar esto.
        var raiz = Raiz(llave);
        for (var intento = 2; intento <= MaxIntentos; intento++)
        {
            var id = $"{raiz}#{intento}";
            var otra = _sagas.Find(id);
            if (otra is null) return new SagaSlot(null, id);
            if (otra.Status != SagaStatus.Compensated) return new SagaSlot(otra, id);
        }

        // Cien compras deshechas sobre la misma llave no son un cliente insistente: es un lazo.
        // Se devuelve la última en vez de seguir creando sagas, y queda dicho en el log.
        _log.LogWarning(
            "Llave {Llave}: {Max} intentos deshechos seguidos. Se deja de abrir sagas nuevas.",
            llave, MaxIntentos);
        return new SagaSlot(_sagas.Find($"{raiz}#{MaxIntentos}"), $"{raiz}#{MaxIntentos}");
    }

    /// <summary>Cuántos intentos deshechos se admiten sobre la misma llave antes de sospechar.</summary>
    private const int MaxIntentos = 100;

    /// <summary>La llave sin el sufijo de intento — para que el intento 3 se busque desde la raíz.</summary>
    private static string Raiz(string llave)
    {
        var corte = llave.LastIndexOf('#');
        return corte > 0 ? llave[..corte] : llave;
    }

    /// <summary>Las sagas que están deshaciendo algo — la vista de operación.</summary>
    public IReadOnlyList<TSaga> PendingCompensations() => _sagas.WithPendingCompensations();

    /// <summary>Las que empezaron antes de <paramref name="limite"/> y siguen sin cerrar (HU #29).</summary>
    public IReadOnlyList<TSaga> StartedBefore(DateTimeOffset limite) => _sagas.StartedBefore(limite);

    public Result<TSaga> Get(string id)
        => _sagas.Find(id) is { } s ? Result.Ok(s) : NotFound(id);

    private Rejection NotFound(string id)
        => Rejection.NotFound($"{_vocabulary.Origin}.saga_not_found", $"No existe {_vocabulary.Noun} {id}.");

    /// <summary>
    /// Ejecuta las compensaciones pendientes de una saga.
    /// </summary>
    /// <remarks>
    /// <b>Se llama desde dos sitios y hace lo mismo en los dos:</b> en línea cuando un paso falla,
    /// y desde el barrido de fondo cuando llega la hora de reintentar. Que sea el mismo camino es
    /// lo que evita que la segunda vez se haga distinto de la primera.
    /// </remarks>
    public async Task<Result<TSaga>> CompensateAsync(string sagaId, string reason, CancellationToken ct)
    {
        var saga = _sagas.Find(sagaId);
        if (saga is null) return NotFound(sagaId);

        if (saga.Status == SagaStatus.Completed)
        {
            return Rejection.Conflict($"{_vocabulary.Origin}.already_completed",
                $"Ya se completó {_vocabulary.Noun}. Deshacerlo es una cancelación con su política, no una compensación.");
        }

        var ahora = _clock.GetUtcNow();
        var actualizadas = new List<Compensation>(saga.Compensations.Count);

        foreach (var c in saga.Compensations)
        {
            // Solo lo pendiente, lo que no se rindió, Y cuyo turno llegó. Las tres condiciones
            // cuentan: respetar el retroceso evita que un barrido cada minuto martillee una
            // capacidad caída, y saltarse lo rendido es lo que hace que rendirse SIGNIFIQUE algo
            // — sin eso, una compensación agotada se reintentaría cada minuto para siempre,
            // gritando el mismo error en el log y tapando los que sí se pueden atender.
            if (!c.IsPending || c.IsStuck || (c.NextAttemptUtc is { } cuando && ahora < cuando))
            {
                actualizadas.Add(c);
                continue;
            }
            actualizadas.Add(await _compensator.TryAsync(saga, c with { Reason = reason }, ct));
        }

        var quedan = actualizadas.Any(c => c.IsPending);
        var colgadas = actualizadas.Any(c => c.IsStuck);

        saga = saga
            .WithCompensations(actualizadas)
            .WithStatus(colgadas ? SagaStatus.CompensationFailed
                : quedan ? SagaStatus.Compensating
                : SagaStatus.Compensated);

        // El aviso sale UNA vez por vez que la saga cae en colgada, no una por vuelta del
        // barrido. Sin esa guarda, la guardia recibiría el mismo correo cada minuto hasta que
        // Api.Notifications lo cortara por tope de frecuencia — y ese corte se llevaría por
        // delante los avisos de las demás sagas.
        if (saga.Status == SagaStatus.CompensationFailed && saga.AlertedAtUtc is null)
        {
            saga = await AvisarAsync(saga, ahora, ct);
        }

        _sagas.Put(saga);
        return Result.Ok(saga);
    }

    /// <summary>
    /// Vuelve a intentar lo que se había rendido. <b>Es la puerta de la persona.</b>
    /// </summary>
    /// <remarks>
    /// Rendirse a los ocho intentos es correcto mientras haya una forma de decir «ya arreglé la
    /// causa, probá otra vez». Sin ella, «se rinde» sería «se abandona», y la única salida a una
    /// devolución colgada sería tocarla a mano en la capacidad — por fuera del rastro de la saga.
    /// </remarks>
    public async Task<Result<TSaga>> RetryStuckAsync(string sagaId, CancellationToken ct)
    {
        var saga = _sagas.Find(sagaId);
        if (saga is null) return NotFound(sagaId);

        if (saga.Stuck().Count == 0)
        {
            return Rejection.Conflict($"{_vocabulary.Origin}.nothing_stuck",
                $"No hay compensaciones rendidas en {_vocabulary.Noun}. Las que están en curso las reintenta el barrido solo.");
        }

        _log.LogInformation("Reintento manual de las compensaciones rendidas de la saga {Saga}.", saga.Id);

        saga = saga
            .WithCompensations(saga.Compensations
                .Select(c => c.IsStuck ? c with { Attempts = 0, NextAttemptUtc = null } : c)
                .ToList())
            // Se rearma el aviso: si vuelve a colgarse tras el arreglo, la guardia tiene que
            // enterarse otra vez. La llave lleva AlertsSent, así que el segundo aviso no lo
            // confunde Notifications con el primero.
            .WithAlert(null, saga.AlertsSent);

        _sagas.Put(saga);

        return await CompensateAsync(sagaId, "reintento pedido por una persona", ct);
    }

    /// <summary>Manda el aviso y deja anotado en la saga qué pasó con él.</summary>
    private async Task<TSaga> AvisarAsync(TSaga saga, DateTimeOffset ahora, CancellationToken ct)
    {
        var motivo = await _alert.RaiseAsync(saga, ct);

        if (motivo is null)
        {
            _log.LogError("COMPENSACIÓN COLGADA en la saga {Saga}: avisado a la guardia.", saga.Id);
            return saga.WithAlert(ahora, saga.AlertsSent + 1);
        }

        if (motivo.IsTransient)
        {
            // Notifications caída es lo mismo que cualquier otra capacidad caída: se reintenta al
            // siguiente barrido. NO se marca como avisada, que es justo lo que permite el
            // reintento.
            _log.LogWarning("No se pudo avisar de la saga {Saga} ({Error}); se reintenta en el barrido.",
                saga.Id, motivo);
            return saga;
        }

        // Falta la plantilla, o no hay a quién avisar. Eso no lo arregla reintentar: lo arregla
        // una persona tocando la configuración o autorando la plantilla, y repetirlo cada minuto
        // solo llenaría el log del mismo error tapando el que importa. Se grita una vez.
        _log.LogError(
            "COMPENSACIÓN COLGADA en la saga {Saga} y NO SE PUDO AVISAR A NADIE ({Error}). Revisa Alerts y la plantilla '{Plantilla}'.",
            saga.Id, motivo, _alert.TemplateKey);
        return saga.WithAlert(ahora, saga.AlertsSent);
    }
}
