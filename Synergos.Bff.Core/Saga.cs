using Synergos.Core;

namespace Synergos.Bff.Core;

/// <summary>Los topes de la compensación, iguales para los ocho orquestadores.</summary>
public static class CompensationLimits
{
    /// <summary>Cuántos intentos antes de rendirse y pedir una persona.</summary>
    public const int MaxAttempts = 8;
}

/// <summary>Qué hay que deshacer, si hay que deshacerlo.</summary>
/// <param name="Id">Identificador dentro de la saga.</param>
/// <param name="Kind">Qué deshacer. <b>Lo nombra el dominio</b>, no esta capa.</param>
/// <param name="TargetId">Sobre qué identificador de la capacidad.</param>
/// <param name="Reason">Por qué. Viaja al motivo de la devolución, para conciliar.</param>
/// <param name="Attempts">Cuántas veces se intentó.</param>
/// <param name="NextAttemptUtc">Cuándo toca el próximo intento.</param>
/// <param name="LastError">Qué dijo el último fallo.</param>
/// <param name="DoneAtUtc">Cuándo se completó, si se completó.</param>
/// <remarks>
/// <para><b>Es un dato, no una función.</b> Una compensación tiene que poder ejecutarse
/// <i>después</i> —desde otro proceso, tras un reinicio, horas más tarde— y un <c>Action</c> en
/// memoria no sobrevive a nada de eso. Guardar la intención como dato es lo que permite que un
/// barrido de fondo la reintente.</para>
///
/// <para><b><c>Kind</c> es un <c>string</c> y no un <c>enum</c>, y ese es el cambio que la
/// promoción obligó a hacer.</b> Un enum en esta capa tendría que enumerar
/// <c>ReleaseBookingHold</c>, <c>ReleaseStockHold</c>, <c>CancelOrder</c>… es decir, el catálogo
/// de todo lo que los ocho dominios saben deshacer. Eso convertiría a Bff.Core en el sitio que
/// hay que tocar cada vez que nace un dominio — exactamente el acople que la capa viene a
/// evitar. El dominio nombra sus kinds en constantes suyas y su ejecutor sabe deshacerlos; esta
/// capa solo sabe <i>que hay algo pendiente y cuándo reintentarlo</i>.</para>
/// </remarks>
public sealed record Compensation(
    string Id,
    string Kind,
    string TargetId,
    string Reason,
    int Attempts = 0,
    DateTimeOffset? NextAttemptUtc = null,
    string? LastError = null,
    DateTimeOffset? DoneAtUtc = null)
{
    public bool IsPending => DoneAtUtc is null;

    /// <summary>
    /// Se rindió: agotó los intentos y sigue pendiente. <b>Necesita una persona.</b>
    /// </summary>
    /// <remarks>
    /// Rendirse tiene que ser un estado y no solo una línea de log, porque de eso dependen dos
    /// cosas: que el barrido <b>deje de intentarlo</b> —si no, «tras N intentos se rinde» es
    /// mentira: sigue martillando cada minuto para siempre— y que el aviso a una persona salga
    /// <b>una vez</b> y no una por vuelta del barrido.
    /// </remarks>
    public bool IsStuck => IsPending && Attempts >= CompensationLimits.MaxAttempts;

    /// <summary>Arma una compensación nueva sobre un identificador de una capacidad.</summary>
    public static Compensation For(string kind, string targetId, string reason)
        => new(Guid.NewGuid().ToString("n"), kind, targetId, reason);
}

/// <summary>En qué punto está una saga.</summary>
/// <remarks>
/// Los nombres son deliberadamente genéricos. Salud llama a esto «esperando confirmación» y
/// «confirmada», Tienda «pendiente de pago» y «pagado»; el dominio pone el nombre en su respuesta
/// HTTP. Lo que la máquina necesita saber es solo si algo <b>ya falló</b> y hay que deshacerlo.
/// </remarks>
public enum SagaStatus
{
    /// <summary>Los pasos reversibles están dados; falta el paso que cuesta.</summary>
    Running,

    /// <summary>Salió todo. Estado final.</summary>
    Completed,

    /// <summary>Algo falló y hay compensaciones por hacer.</summary>
    Compensating,

    /// <summary>Se deshizo todo lo que había que deshacer. Estado final.</summary>
    Compensated,

    /// <summary>Se deshizo lo que se pudo y algo quedó colgado. <b>Necesita una persona.</b></summary>
    CompensationFailed,
}

/// <summary>
/// Lo que la máquina de sagas necesita saber de una saga — y nada más.
/// </summary>
/// <remarks>
/// Es la superficie mínima a propósito. Todo lo demás que una saga lleva —el paciente y la
/// ventana en Salud, el carrito y las líneas en Tienda— es del dominio, y esta capa no tiene por
/// qué verlo. Si algún día hiciera falta agregarle un campo a esta interfaz para que el motor
/// funcione, hay que sospechar: probablemente lo que se está metiendo es negocio.
/// </remarks>
public interface ISaga
{
    string Id { get; }
    SagaStatus Status { get; }
    IReadOnlyList<Compensation> Compensations { get; }
    DateTimeOffset StartedAtUtc { get; }

    /// <summary>Cuándo se avisó a una persona de que algo quedó colgado.</summary>
    DateTimeOffset? AlertedAtUtc { get; }

    /// <summary>Cuántos avisos salieron. <b>Semilla de la llave del aviso.</b></summary>
    int AlertsSent { get; }
}

/// <summary>
/// Una saga que sabe devolver una copia suya con un campo cambiado.
/// </summary>
/// <remarks>
/// <para><b>Por qué el parámetro se refiere a sí mismo.</b> Las sagas son <c>record</c>
/// inmutables y el motor tiene que poder actualizarlas sin conocer su tipo concreto. Un
/// <c>with</c> genérico no existe en C#, así que el tipo se declara a sí mismo como lo que
/// devuelven sus mutadores. A cambio, el motor devuelve la saga <i>del dominio</i> ya
/// actualizada: el llamador no tiene que volver a leerla ni castearla.</para>
///
/// <para>Tres métodos y no uno con parámetros opcionales: <c>AlertedAtUtc</c> hay que poder
/// ponerlo <b>en nulo</b> —es lo que rearma el aviso tras un reintento manual— y con un
/// «nulo significa no cambiar» eso sería imposible de expresar.</para>
/// </remarks>
public interface ISaga<out TSelf> : ISaga where TSelf : ISaga<TSelf>
{
    TSelf WithStatus(SagaStatus status);
    TSelf WithCompensations(IReadOnlyList<Compensation> compensations);
    TSelf WithAlert(DateTimeOffset? alertedAtUtc, int alertsSent);
}

/// <summary>Lo que se deriva de una saga sin que cada dominio tenga que reescribirlo.</summary>
public static class SagaExtensions
{
    public static IReadOnlyList<Compensation> Pending(this ISaga saga)
        => saga.Compensations.Where(c => c.IsPending).ToList();

    /// <summary>Lo que se rindió y necesita una persona.</summary>
    public static IReadOnlyList<Compensation> Stuck(this ISaga saga)
        => saga.Compensations.Where(c => c.IsStuck).ToList();

    /// <summary>
    /// Si esta saga está deshaciendo algo. <b>Armada no es lo mismo que pendiente.</b>
    /// </summary>
    /// <remarks>
    /// Es la distinción de la que dependen el barrido y la vista de operación, y saltársela sale
    /// carísimo. Una saga en curso lleva sus compensaciones <i>armadas</i> —el cupo que soltar,
    /// el stock que devolver— porque se anotan en el instante en que existe lo que habría que
    /// deshacer. Pero eso es el seguro, no la tarea: sin este filtro el barrido las ejecuta y
    /// <b>toda operación sana se cancela sola</b> en el primer minuto. Una compensación solo es
    /// trabajo cuando algo YA falló.
    /// </remarks>
    public static bool IsUnwinding(this ISaga saga)
        => saga.Status is SagaStatus.Compensating or SagaStatus.CompensationFailed;

    /// <summary>
    /// Si queda algo que el barrido pueda hacer por esta saga.
    /// </summary>
    /// <remarks>
    /// Dentro de una saga que ya está deshaciendo, dos cosas y solo dos: una compensación que
    /// todavía tiene intentos, o un aviso que no llegó a salir. Una saga rendida y ya avisada
    /// sigue apareciendo en la vista de operación —ahí es donde tiene que estar— pero el barrido
    /// no la vuelve a tocar: reescribir su fichero cada minuto para no cambiar nada es ruido, no
    /// vigilancia.
    /// </remarks>
    public static bool NeedsSweep(this ISaga saga)
        => saga.IsUnwinding()
           && (saga.Compensations.Any(c => c.IsPending && !c.IsStuck)
               || (saga.AlertedAtUtc is null && saga.Stuck().Count > 0));

    /// <summary>
    /// La llave de idempotencia de un paso. <b>Determinista: reintentar es recuperar.</b>
    /// </summary>
    /// <remarks>
    /// Es la decisión que hace recuperable todo lo demás. Con llaves deterministas
    /// —<c>{sagaId}:capture</c>, <c>{sagaId}:refund</c>— la capacidad reconoce la llave y
    /// devuelve lo que ya hizo, en vez de hacerlo otra vez. Sin eso, tras una caída entre «cobré»
    /// y «lo anoté» no habría manera de averiguar si el cobro salió — y las capacidades no
    /// exponen «búscame por llave».
    /// </remarks>
    public static IdempotencyKey KeyFor(this ISaga saga, string step)
        => IdempotencyKey.From(saga.Id, step);
}
