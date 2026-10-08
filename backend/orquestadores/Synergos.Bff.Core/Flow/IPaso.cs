using Synergos.Core;

namespace Synergos.Bff.Core.Flow;

/// <summary>
/// Un tipo de paso: lo que un flujo declarado ejecuta cuando nombra su <see cref="Tipo"/>.
/// </summary>
/// <remarks>
/// <para><b>Dos familias y el mismo contrato.</b> Los pasos sobre capacidades —cotizar, apartar,
/// autorizar— viven en <c>Synergos.Bff.Pasos</c>, para cualquier orquestador que declare su flujo
/// (hoy, Eventos); los del dominio —revisar las líneas de una compra, sumarle la comisión— viven en
/// su orquestador. El intérprete no distingue: busca por tipo y llama.</para>
///
/// <para><b>Declara cuántos nombres lee, cuántos escribe y qué llave usa</b> para que el validador
/// lo cruce con la definición al ARRANCAR. Un paso que lee dos valores declarado con uno no falla
/// al leer el JSON: falla en la primera compra, con un índice fuera de rango. Y uno que necesita
/// llave y no la tiene lanza con el aforo ya apartado.</para>
///
/// <para><b>No lanza por un no de la capacidad.</b> Lo devuelve como
/// <see cref="SalidaDePaso.Rechaza"/>, con el rechazo original: quien llamó necesita saber si fue
/// <c>inventory.out_of_stock</c> —ofrecer otra localidad— o <c>payments.payment_declined</c>.</para>
/// </remarks>
public interface IPaso
{
    /// <summary>El nombre con el que lo nombra una definición: <c>payments.autorizar</c>.</summary>
    string Tipo { get; }

    /// <summary>Cuántos nombres lee, en orden: los <c>lee</c> de la definición.</summary>
    int Lecturas { get; }

    /// <summary>Cuántos valores produce, en orden: los <c>escribe</c> de la definición.</summary>
    int Escrituras { get; }

    /// <summary>Qué llave de idempotencia pide: ninguna, la de <c>llave</c> o la de <c>llave_base</c>.</summary>
    LlaveRequerida Llave { get; }

    /// <summary>Si avisa con la <c>plantilla</c> que declara su definición. Por defecto, no.</summary>
    /// <remarks>
    /// Se cruza como la llave, en los dos sentidos: un paso de aviso sin plantilla saldría
    /// <c>template_not_found</c> en cada compra, y una plantilla declarada en un paso que no avisa
    /// es una regla que nadie cumple.
    /// </remarks>
    bool UsaPlantilla => false;

    Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct);
}

/// <summary>Qué llave de idempotencia necesita un tipo de paso, para cruzarla con su definición.</summary>
/// <remarks>
/// En los dos sentidos: un paso que la pide y no la tiene lanza en mitad de la compra, y una llave
/// declarada que el paso no usa es una regla que nadie cumple — quien la escribió cree que ese
/// paso no se duplica.
/// </remarks>
public enum LlaveRequerida
{
    /// <summary>No llama con llave: lee, calcula, o la capacidad no la pide.</summary>
    Ninguna,

    /// <summary>La de <c>llave</c>: <c>{sagaId}|{llave}</c>.</summary>
    Fija,

    /// <summary>La de <c>llave_base</c> más lo que el paso pone: <c>{sagaId}|{llave_base}:{ítem}</c>.</summary>
    PorItem,
}

/// <summary>Cómo sigue la fase después de un paso.</summary>
public enum PasoResultado
{
    /// <summary>Al siguiente paso.</summary>
    Continuar,

    /// <summary>La fase termina acá, bien: lo que queda de ella no se ejecuta.</summary>
    SaltarFase,

    /// <summary>Falló: la saga se deshace con el rechazo del paso.</summary>
    Abortar,
}

/// <summary>Lo que un paso reservó: sobre qué se deshace antes de consumirlo y sobre qué después.</summary>
/// <param name="Id">Lo reservado en la capacidad (el apartado, el cobro). Es el objetivo de la
/// compensación mientras no se consuma.</param>
/// <param name="CierreId">El objetivo una vez consumido, si cambia: devolver aforo consumido es un
/// ajuste sobre el POZO, no sobre el apartado, que ya no existe. <c>null</c> si es el mismo.</param>
public sealed record Reservado(string Id, string? CierreId = null);

/// <summary>Lo que un paso recibe: su definición, la saga y dónde leer.</summary>
public sealed class EntradaDePaso
{
    private readonly FlowContext _ctx;
    private readonly string? _alias;
    private readonly FlowContext? _item;

    public EntradaDePaso(string sagaId, Ref origen, PasoDef paso, FlowContext ctx, string? alias, FlowContext? item)
    {
        SagaId = sagaId;
        Origen = origen;
        Paso = paso;
        _ctx = ctx;
        _alias = alias;
        _item = item;
    }

    /// <summary>La saga. Semilla de las llaves de idempotencia.</summary>
    public string SagaId { get; }

    /// <summary>
    /// Para qué se reserva, tal como lo ve una capacidad: <c>Ref(clave del flujo, sagaId)</c>.
    /// </summary>
    public Ref Origen { get; }

    /// <summary>La definición del paso que se está ejecutando.</summary>
    public PasoDef Paso { get; }

    /// <summary>El valor del <paramref name="indice"/>-ésimo nombre de <c>lee</c>.</summary>
    public T Lee<T>(int indice) => Ambito.Leer<T>(Paso.Lee[indice], _ctx, _alias, _item);

    /// <summary>La llave FIJA del paso: <c>{sagaId}|{llave}</c>, la misma que <c>saga.KeyFor(llave)</c>.</summary>
    public IdempotencyKey Llave()
        => Paso.Llave is { } llave
            ? IdempotencyKey.From(SagaId, llave)
            : throw new InvalidOperationException($"El paso «{Paso.Id}» ({Paso.Tipo}) necesita «llave» y no la declara.");

    /// <summary>
    /// La llave POR ÍTEM: <c>{sagaId}|{llave_base}:{discriminante}</c>.
    /// </summary>
    /// <remarks>
    /// El discriminante lo pone el paso, en C#, y no una plantilla en el dato. Apartar usa el ítem
    /// del pozo y no la posición de la línea: si el comprador reordena las butacas entre dos
    /// intentos, una llave por posición apartaría dos veces.
    /// </remarks>
    public IdempotencyKey LlavePara(string discriminante)
        => Paso.LlaveBase is { } llaveBase
            ? IdempotencyKey.From(SagaId, $"{llaveBase}:{discriminante}")
            : throw new InvalidOperationException($"El paso «{Paso.Id}» ({Paso.Tipo}) necesita «llave_base» y no la declara.");
}

/// <summary>Lo que un paso devuelve: cómo sigue la fase, qué produjo y qué reservó.</summary>
/// <remarks>
/// Se construye sólo por sus fábricas, para que no exista una salida que diga «continuar» y traiga
/// un rechazo a la vez: con dos campos que pueden contradecirse, el intérprete tendría que elegir
/// a cuál creerle.
/// </remarks>
public sealed class SalidaDePaso
{
    private SalidaDePaso(
        PasoResultado control, IReadOnlyList<object?> valores, Rejection? rechazo, Reservado? reservado, string? cerrado)
    {
        Control = control;
        Valores = valores;
        Rechazo = rechazo;
        Reservado = reservado;
        Cerrado = cerrado;
    }

    public PasoResultado Control { get; }

    /// <summary>Lo producido, en el orden de los <c>escribe</c> de la definición.</summary>
    public IReadOnlyList<object?> Valores { get; }

    /// <summary>El no de la capacidad o del dominio, tal cual. Sólo con <see cref="PasoResultado.Abortar"/>.</summary>
    public Rejection? Rechazo { get; }

    /// <summary>Lo que el paso reservó, si su definición declara una reserva.</summary>
    public Reservado? Reservado { get; }

    /// <summary>
    /// Sobre qué se deshace lo que este paso acaba de cerrar, si lo produjo él: <c>null</c> si sigue
    /// siendo el <see cref="Synergos.Bff.Core.Flow.Reservado.CierreId"/> anotado al reservar.
    /// </summary>
    public string? Cerrado { get; }

    public static SalidaDePaso Sigue(params object?[] valores) => new(PasoResultado.Continuar, valores, null, null, null);

    public static SalidaDePaso Reserva(Reservado reservado, params object?[] valores)
        => new(PasoResultado.Continuar, valores, null, reservado, null);

    /// <summary>
    /// Cerró una reserva y el cierre produjo su propio objetivo: confirmar un apartado devuelve una
    /// reserva nueva, y cancelarla es sobre ESA, no sobre el apartado que ya no existe.
    /// </summary>
    public static SalidaDePaso Cierra(string objetivo, params object?[] valores)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objetivo);
        return new(PasoResultado.Continuar, valores, null, null, objetivo);
    }

    public static SalidaDePaso SaltaFase(params object?[] valores) => new(PasoResultado.SaltarFase, valores, null, null, null);

    public static SalidaDePaso Rechaza(Rejection rechazo)
        => new(PasoResultado.Abortar, Array.Empty<object?>(), rechazo, null, null);
}

/// <summary>Dónde busca el intérprete el paso que una definición nombra.</summary>
public interface IRegistroDePasos
{
    /// <summary>El paso de ese tipo, o <c>null</c> si nadie lo registró.</summary>
    IPaso? Para(string tipo);

    /// <summary>Los tipos registrados.</summary>
    IReadOnlyCollection<string> Tipos { get; }
}

/// <summary>Los pasos de un orquestador, por tipo.</summary>
public sealed class RegistroDePasos : IRegistroDePasos
{
    private readonly Dictionary<string, IPaso> _pasos = new(StringComparer.Ordinal);

    /// <exception cref="InvalidOperationException">Si dos pasos dicen ser del mismo tipo: elegir uno
    /// en silencio sería decidir por orden de registro qué hace una compra.</exception>
    public RegistroDePasos(IEnumerable<IPaso> pasos)
    {
        ArgumentNullException.ThrowIfNull(pasos);
        foreach (var paso in pasos)
        {
            if (!_pasos.TryAdd(paso.Tipo, paso))
            {
                throw new InvalidOperationException($"Hay dos pasos registrados como «{paso.Tipo}».");
            }
        }
    }

    public IPaso? Para(string tipo) => _pasos.GetValueOrDefault(tipo);

    public IReadOnlyCollection<string> Tipos => _pasos.Keys;
}
