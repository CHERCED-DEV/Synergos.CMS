using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Pasos;

/// <summary>Los tipos de compensación que conoce el deshacer común: los que una definición declara en
/// <c>antes</c> y <c>despues</c>.</summary>
public static class TiposDeDeshacer
{
    /// <summary>Soltar un apartado de existencias que no se consumió.</summary>
    public const string SoltarExistencias = "inventory.soltar";

    /// <summary>Devolver al pozo unidades ya consumidas: un ajuste relativo con llave.</summary>
    public const string ReponerExistencias = "inventory.reponer";

    /// <summary>Anular una autorización que no se capturó.</summary>
    public const string AnularCobro = "payments.anular";

    /// <summary>Devolver lo devolvible de un cobro capturado.</summary>
    public const string DevolverCobro = "payments.devolver";

    /// <summary>Soltar un apartado de la agenda que no se confirmó.</summary>
    public const string LiberarAgenda = "booking.liberar";

    /// <summary>Cancelar una reserva de la agenda ya confirmada.</summary>
    public const string CancelarReserva = "booking.cancelar";
}

/// <summary>Cómo se deshace UN tipo de compensación de una saga genérica.</summary>
/// <remarks>
/// Un dominio que necesite deshacer algo propio registra el suyo junto a los estándar: el ejecutor los
/// despacha por <see cref="Tipo"/>, y el arranque no deja pasar un flujo que declare un tipo que nadie
/// registró.
/// </remarks>
public interface IDeshaceUnTipo
{
    /// <summary>El tipo de compensación (<c>payments.anular</c>).</summary>
    string Tipo { get; }

    /// <summary>Deshace; <c>null</c> si quedó hecho, o el rechazo (transitorio o no) de la capacidad.</summary>
    Task<Rejection?> DeshacerAsync(SagaDeFlujo saga, Compensation pendiente, CancellationToken ct);
}

/// <summary>
/// El ejecutor de compensaciones de las sagas genéricas (núcleo N1, P06): despacha por tipo a quien sabe
/// deshacerlo.
/// </summary>
/// <remarks>
/// <para><b>Es el <c>*CompensationExecutor</c> de cada orquestador, una vez</b>: anular y devolver un cobro
/// estaban en 4 de 4 orquestadores (ADR 0140, decisión 2). Lo que hacen el reintento, el retroceso, el
/// rendirse y el aviso sigue siendo de <c>SagaEngine</c>: esto sólo sabe hacer cada cosa una vez.</para>
///
/// <para><b>Un tipo desconocido no se reintenta ocho veces sin decir nada</b>: contesta
/// <see cref="Desconocida"/>, que no es transitorio. El arranque ya lo habría impedido para un flujo
/// genérico; esto es la red para una saga en vuelo de una versión anterior.</para>
/// </remarks>
public sealed class DeshacerComun : ICompensationExecutor<SagaDeFlujo>, IConoceSusTipos
{
    /// <summary>El código con que se rechaza deshacer un tipo que nadie registró.</summary>
    public const string Desconocida = "flow.compensacion_desconocida";

    private readonly Dictionary<string, IDeshaceUnTipo> _tipos = new(StringComparer.Ordinal);

    /// <summary>Los estándar sobre los puertos de deshacer, más los del dominio (<paramref name="propios"/>).</summary>
    /// <exception cref="InvalidOperationException">Si dos dicen deshacer el mismo tipo.</exception>
    public DeshacerComun(
        IDeshacerExistencias existencias, IDeshacerCobros cobros, IDeshacerAgenda agenda, IEnumerable<IDeshaceUnTipo> propios)
    {
        ArgumentNullException.ThrowIfNull(propios);
        foreach (var t in Estandar(existencias, cobros, agenda).Concat(propios))
        {
            if (!_tipos.TryAdd(t.Tipo, t)) throw new InvalidOperationException($"Hay dos que deshacen «{t.Tipo}».");
        }
    }

    /// <summary>Los tipos que sabe deshacer: contra lo que el validador cruza <c>antes</c> y <c>despues</c>.</summary>
    public IReadOnlyCollection<string> Tipos => _tipos.Keys;

    public Task<Rejection?> UndoAsync(SagaDeFlujo saga, Compensation pending, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return _tipos.TryGetValue(pending.Kind, out var tipo)
            ? tipo.DeshacerAsync(saga, pending, ct)
            : Task.FromResult<Rejection?>(Rejection.Invalid(Desconocida, $"No sé deshacer «{pending.Kind}»."));
    }

    /// <summary>Los estándar, sobre los puertos de deshacer.</summary>
    private static IReadOnlyList<IDeshaceUnTipo> Estandar(IDeshacerExistencias existencias, IDeshacerCobros cobros, IDeshacerAgenda agenda)
        =>
        [
            new Deshace(TiposDeDeshacer.SoltarExistencias, (_, c, ct) => Envolver(existencias.SoltarAsync(c.TargetId, ct))),
            new Deshace(TiposDeDeshacer.ReponerExistencias, (s, c, ct) => ReponerAsync(existencias, s, c, ct)),
            new Deshace(TiposDeDeshacer.AnularCobro, (_, c, ct) => Envolver(cobros.AnularAsync(c.TargetId, ct))),
            new Deshace(TiposDeDeshacer.DevolverCobro, (s, c, ct) => DevolverAsync(cobros, s, c, ct)),
            new Deshace(TiposDeDeshacer.LiberarAgenda, (_, c, ct) => Envolver(agenda.LiberarAsync(c.TargetId, ct))),
            new Deshace(TiposDeDeshacer.CancelarReserva, (_, c, ct) => Envolver(agenda.CancelarAsync(c.TargetId, ct))),
        ];

    /// <summary>
    /// Devuelve al pozo lo que ya se había consumido: un ajuste RELATIVO, con llave determinista desde la
    /// saga porque el motor reintenta hasta ocho veces (#30).
    /// </summary>
    /// <remarks>
    /// La cantidad es la que la reserva guardó al apartar. Si dos reservas cerraron sobre el mismo pozo,
    /// sus compensaciones nacieron en el mismo orden que ellas: la n-ésima compensación de reponer sobre
    /// ese pozo es la de la n-ésima reserva que cierra en él.
    /// </remarks>
    private static async Task<Rejection?> ReponerAsync(
        IDeshacerExistencias existencias, SagaDeFlujo saga, Compensation pendiente, CancellationToken ct)
    {
        var reservas = saga.Reservas.Values.SelectMany(r => r).Where(r => r.CloseTargetId == pendiente.TargetId).ToList();
        var orden = saga.Compensations
            .Where(c => c.Kind == pendiente.Kind && c.TargetId == pendiente.TargetId)
            .ToList()
            .FindIndex(c => c.Id == pendiente.Id);

        if (orden < 0 || orden >= reservas.Count || reservas[orden].Cantidad is not { } cantidad)
        {
            // No debería pasar: la compensación se reescribió desde una reserva de esta misma saga.
            // Si pasa es un defecto y hay que verlo, no reintentarlo ocho veces.
            return Rejection.Invalid("flow.reponer_sin_cantidad",
                $"La saga no tiene una reserva con cantidad sobre {pendiente.TargetId} para reponer.");
        }

        return await Envolver(existencias.ReponerAsync(pendiente.TargetId, cantidad, saga.KeyFor($"restock:{pendiente.Id}"), ct));
    }

    /// <summary>Devuelve lo devolvible; si ya no queda nada, se da por hecha (el reintento tras un timeout).</summary>
    private static async Task<Rejection?> DevolverAsync(
        IDeshacerCobros cobros, SagaDeFlujo saga, Compensation pendiente, CancellationToken ct)
    {
        var devolvible = await cobros.DevolvibleAsync(pendiente.TargetId, ct);
        if (!devolvible.IsOk) return devolvible.Rejection;
        if (devolvible.Value.IsZero) return null;

        return await Envolver(cobros.DevolverAsync(
            pendiente.TargetId, devolvible.Value, pendiente.Reason, saga.KeyFor($"refund:{pendiente.Id}"), ct));
    }

    private static async Task<Rejection?> Envolver<T>(Task<Result<T>> llamada)
    {
        var r = await llamada;
        return r.IsOk ? null : r.Rejection;
    }

    private sealed class Deshace(string tipo, Func<SagaDeFlujo, Compensation, CancellationToken, Task<Rejection?>> hace) : IDeshaceUnTipo
    {
        public string Tipo => tipo;

        public Task<Rejection?> DeshacerAsync(SagaDeFlujo saga, Compensation pendiente, CancellationToken ct) => hace(saga, pendiente, ct);
    }
}
