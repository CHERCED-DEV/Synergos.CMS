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

    /// <summary>Las sagas que están deshaciendo algo — la vista de operación.</summary>
    public IReadOnlyList<TSaga> PendingCompensations() => _sagas.WithPendingCompensations();

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
