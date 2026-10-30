using Synergos.Core;

namespace Synergos.Bff.Core.Flow;

/// <summary>
/// Lo que una fachada contesta con los códigos y las palabras de SU dominio.
/// </summary>
/// <param name="NoExiste">Una saga que no existe, que es de otro, o que es de otro flujo: las tres dan lo
/// mismo, para no confirmar que existe.</param>
/// <param name="NoContinuable">Continuar una saga que no está en curso ni completada.</param>
/// <param name="MotivoDeCancelar">Con qué motivo se deshace cuando quien la abrió la cancela.</param>
public sealed record ReglasDeLaFachada(
    Func<string, Rejection> NoExiste,
    Func<SagaStatus, Rejection> NoContinuable,
    string MotivoDeCancelar)
{
    /// <summary>Las de un flujo sin palabras propias, desde el vocabulario de su orquestador.</summary>
    public static ReglasDeLaFachada Para(SagaVocabulary vocabulario)
    {
        ArgumentNullException.ThrowIfNull(vocabulario);
        return new(
            id => Rejection.NotFound($"{vocabulario.Origin}.saga_not_found", $"No existe {vocabulario.Noun} {id}."),
            estado => Rejection.Conflict($"{vocabulario.Origin}.not_continuable", $"No se puede continuar: está {estado}."),
            "cancelada por quien la abrió");
    }
}

/// <summary>
/// La puerta de un flujo declarado, igual para todos (núcleo N1, P09): lo que cada <c>*Flow.cs</c>
/// escribía alrededor del intérprete.
/// </summary>
/// <typeparam name="TSaga">La saga: la de un dominio (Eventos) o la genérica.</typeparam>
/// <remarks>
/// <para><b>Las reglas que estaban copiadas en cada fachada, una vez:</b></para>
/// <list type="bullet">
///   <item>Se abre por la llave con <see cref="SagaEngine{TSaga}.Abrir"/> y su dueño ANTES de tocar nada
///   (#41, #166): una llave usada devuelve la misma saga, una deshecha deja reintentar, y una de otro no
///   le devuelve nada a éste.</item>
///   <item>Continuar exige la saga del dueño y del flujo (si no, «no existe»), devuelve tal cual una ya
///   completada (el doble clic es idempotente), rechaza con el código del dominio una que no está en
///   curso, y si otra llamada la completó mientras ésta esperaba turno contesta lo mismo que si hubiera
///   llegado después.</item>
///   <item>Cancelar y consultar sólo para el dueño; reintentar lo rendido y ver lo pendiente son de
///   operación.</item>
/// </list>
///
/// <para><b>Varios flujos en un orquestador comparten almacén</b> si comparten tipo de saga (la genérica):
/// el filtro <c>esDelFlujo</c> hace que una llave reutilizada para otro flujo no devuelva aquélla
/// (<see cref="LlaveDeOtroFlujo"/>) y que consultar una saga de otro flujo dé «no existe».</para>
/// </remarks>
public sealed class FachadaDeFlujo<TSaga> where TSaga : class, ISaga<TSaga>
{
    /// <summary>El código con que se rechaza abrir con una llave que ya es de otro flujo del orquestador.</summary>
    public const string LlaveDeOtroFlujo = "flow.llave_de_otro_flujo";

    private readonly SagaEngine<TSaga> _sagas;
    private readonly FlowRunner<TSaga> _flujo;
    private readonly ReglasDeLaFachada _reglas;
    private readonly Func<TSaga, bool> _esDelFlujo;
    private readonly string _clave;
    private readonly string _abre;

    /// <param name="sagas">La máquina de sagas.</param>
    /// <param name="flujo">El intérprete del flujo, ya validado.</param>
    /// <param name="definicion">Su definición: de ella sale la fase que abre.</param>
    /// <param name="reglas">Los códigos y las palabras del dominio.</param>
    /// <param name="esDelFlujo">Si una saga es de este flujo. Por defecto, todas: un orquestador con un solo flujo.</param>
    public FachadaDeFlujo(
        SagaEngine<TSaga> sagas, FlowRunner<TSaga> flujo, FlujoDef definicion, ReglasDeLaFachada reglas,
        Func<TSaga, bool>? esDelFlujo = null)
    {
        ArgumentNullException.ThrowIfNull(definicion);
        _sagas = sagas ?? throw new ArgumentNullException(nameof(sagas));
        _flujo = flujo ?? throw new ArgumentNullException(nameof(flujo));
        _reglas = reglas ?? throw new ArgumentNullException(nameof(reglas));
        _esDelFlujo = esDelFlujo ?? (_ => true);
        _clave = definicion.Clave;
        _abre = definicion.Fases[0].Nombre;
    }

    /// <summary>
    /// Abre la saga de <paramref name="llave"/> para <paramref name="dueno"/>: la misma si ya existe y es
    /// suya, una nueva si la anterior se deshizo entera.
    /// </summary>
    public async Task<Result<TSaga>> AbrirAsync(string llave, Ref dueno, FlowContext entrada, CancellationToken ct)
    {
        // La saga existe ANTES de tocar nada: si el proceso se cae después del primer paso, lo que se
        // hizo queda escrito con su identificador, y como las llaves derivan de él, repetir la llamada
        // no duplica nada. Quién decide si una llave usada es «esto ya pasó» vive en el motor (#41).
        var slot = _sagas.Abrir(llave, dueno);
        if (slot.Reusar is { } previa)
        {
            if (!_esDelFlujo(previa))
            {
                return Rejection.Conflict(LlaveDeOtroFlujo, $"La llave ya abrió otro flujo de este orquestador; «{_clave}» necesita otra.");
            }

            // La que quedó en duda al abrir (001-P10 = A) no abrió: repetir con su llave la retoma.
            return previa.Status == SagaStatus.Running && string.Equals(_flujo.FaseEnDuda(previa), _abre, StringComparison.Ordinal)
                ? await _flujo.EjecutarFaseAsync(_abre, previa.Id, entrada, ct)
                : Result.Ok(previa);
        }

        return await _flujo.EjecutarFaseAsync(_abre, slot.Id, entrada, ct);
    }

    /// <summary>Si abrir con esta llave devolvería una saga de <paramref name="dueno"/> que ya existe, sin escribir nada.</summary>
    public bool DevolveriaLaMisma(string llave, Ref dueno) => _sagas.Abrir(llave, dueno).Reusar is { } s && _esDelFlujo(s);

    /// <summary>
    /// Ejecuta una fase que continúa la saga.
    /// </summary>
    /// <param name="fase">La fase.</param>
    /// <param name="sagaId">La saga.</param>
    /// <param name="dueno">Quién continúa; nulo sólo para quien ya comprobó el dueño.</param>
    /// <param name="entrada">Lo efímero de la fase.</param>
    /// <param name="ct">Cancelación.</param>
    public async Task<Result<TSaga>> ContinuarAsync(string fase, string sagaId, Ref? dueno, FlowContext entrada, CancellationToken ct)
    {
        var saga = dueno is null ? _sagas.Find(sagaId) : _sagas.FindDe(sagaId, dueno);
        if (saga is null || !_esDelFlujo(saga)) return _reglas.NoExiste(sagaId);
        if (saga.Status == SagaStatus.Completed) return Result.Ok(saga);   // idempotente
        if (saga.Status != SagaStatus.Running) return _reglas.NoContinuable(saga.Status);

        var r = await _flujo.EjecutarFaseAsync(fase, sagaId, entrada, ct);

        // Otra llamada a la misma saga terminó entre la guarda de arriba y el turno de ésta: es el doble
        // clic, y contesta lo mismo que si hubiera llegado después.
        if (r.Rejection?.Code == FlowRunner<TSaga>.NoEnCurso && _sagas.Find(sagaId) is { Status: SagaStatus.Completed } hecha)
        {
            return Result.Ok(hecha);
        }
        return r;
    }

    /// <summary>Deshace la saga; con <paramref name="dueno"/>, sólo si es suya.</summary>
    public Task<Result<TSaga>> CancelarAsync(string sagaId, Ref? dueno, CancellationToken ct)
        => dueno is not null && (_sagas.FindDe(sagaId, dueno) is not { } suya || !_esDelFlujo(suya))
            ? Task.FromResult<Result<TSaga>>(_reglas.NoExiste(sagaId))
            : _sagas.CompensateAsync(sagaId, _reglas.MotivoDeCancelar, ct);

    /// <summary>La saga; con <paramref name="dueno"/>, sólo si es suya. La de otro flujo no existe.</summary>
    public Result<TSaga> Consultar(string sagaId, Ref? dueno)
        => (dueno is null ? _sagas.Find(sagaId) : _sagas.FindDe(sagaId, dueno)) is { } s && _esDelFlujo(s)
            ? Result.Ok(s)
            : _reglas.NoExiste(sagaId);

    /// <summary>
    /// Las sagas de este flujo que son de <paramref name="dueno"/> —en <paramref name="estado"/>, si se pide—, de la
    /// más nueva a la más vieja y por página (núcleo, NB-6 de 011 y NN-7 de 005): «mis reservas».
    /// </summary>
    /// <remarks>
    /// Sólo del dueño, como consultar: no hay forma de pedir las de otro. Las de otro flujo del mismo orquestador
    /// no salen, aunque compartan almacén.
    /// </remarks>
    public (IReadOnlyList<TSaga> Items, int Total) Listar(Ref dueno, SagaStatus? estado, int offset, int limit)
    {
        ArgumentNullException.ThrowIfNull(dueno);
        var todas = _sagas.DeDueno(dueno).Where(_esDelFlujo).Where(s => estado is null || s.Status == estado).ToList();
        return (todas.Skip(Math.Max(0, offset)).Take(Math.Max(0, limit)).ToList(), todas.Count);
    }

    /// <summary>Una lectura sin saga (núcleo, NB-3 de 011): sin dueño, sin escribir nada.</summary>
    public Task<Result<FlowContext>> LeerAsync(string lectura, FlowContext entrada, CancellationToken ct)
        => _flujo.LeerAsync(lectura, entrada, ct);

    /// <summary>Si la fase que abre se puede anticipar: lo que decide si el flujo publica «cotizar».</summary>
    public bool SeAnticipa => _flujo.SeAnticipa;

    /// <summary>La fase que abre, anticipada hasta antes de reservar, sin escribir nada (ADR 0140, el cupón).</summary>
    public Task<Result<FlowContext>> AnticiparAsync(FlowContext entrada, CancellationToken ct)
        => _flujo.AnticiparAsync(_abre, entrada, ct);

    /// <summary>Vuelve a intentar lo que se rindió. Una saga de otro flujo no existe.</summary>
    public Task<Result<TSaga>> ReintentarAsync(string sagaId, CancellationToken ct)
        => _sagas.Find(sagaId) is { } s && !_esDelFlujo(s)
            ? Task.FromResult<Result<TSaga>>(_reglas.NoExiste(sagaId))
            : _sagas.RetryStuckAsync(sagaId, ct);

    /// <summary>Las sagas de este flujo que están deshaciendo algo: la vista de operación.</summary>
    public IReadOnlyList<TSaga> Pendientes() => _sagas.PendingCompensations().Where(_esDelFlujo).ToList();
}
