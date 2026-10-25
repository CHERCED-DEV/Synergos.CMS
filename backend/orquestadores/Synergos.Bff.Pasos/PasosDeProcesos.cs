using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Pasos;

// ── Los pasos sobre los procesos (Api.Workflow), lote 7 ─────────────────────
// La regla de forma de la ADR 0140 (2026-10-16): ninguna saga espera a una persona. Lo que espera es un estado
// declarado en la capacidad de procesos, y cada acto con efectos es un flujo corto que dispara la transición como
// un paso. Estos son sus pasos comunes (009-P2 = A): arrancar, disparar y leer. El proceso, la transición y el rol
// con que se actúa son datos constantes del paso (009-P3 = A), como la plantilla de un aviso.

/// <summary>
/// <c>workflow.arrancar</c>: arranca una instancia del <c>proceso</c> que declara el paso, para el origen de la saga, y
/// escribe la instancia y —si la definición lo nombra— su estado. Llave FIJA: una instancia por saga.
/// </summary>
/// <remarks>
/// <para><b>El sujeto es el origen de la saga</b> (<c>Ref(clave del flujo, saga)</c>), como lo que se reserva: la
/// instancia es de esta saga, y una operación posterior la encuentra en lo que la original deja para lo posterior.</para>
///
/// <para><b>No tiene vuelta</b>: la capacidad no borra instancias. Va detrás de lo que se deshace, y el validador lo
/// exige: si el cobro falla, no hay expediente.</para>
/// </remarks>
public sealed class PasoProcesoArrancar(IWorkflowPort procesos) : IPaso
{
    public string Tipo => "workflow.arrancar";
    public int Lecturas => 0;
    public int Escrituras => 2;
    public int EscriturasOpcionales => 1;
    public LlaveRequerida Llave => LlaveRequerida.Fija;
    public bool UsaProceso => true;
    public bool SinVuelta => true;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string), typeof(string)];

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var instancia = await procesos.ArrancarAsync(entrada.Paso.Proceso!, entrada.Origen, entrada.Llave(), ct);
        return instancia.IsOk
            ? SalidaDePaso.Sigue(instancia.Value.Id, instancia.Value.Estado)
            : SalidaDePaso.Rechaza(instancia.Rejection!);
    }
}

/// <summary>
/// <c>workflow.disparar</c>: lee la instancia, quién actúa y —si la definición la nombra— la nota, y dispara la
/// <c>transicion</c> que declara el paso con el <c>rol</c> que declara. Escribe —si la definición lo nombra— el estado
/// en que queda. Llave FIJA: repetir el acto devuelve la instancia sin dispararla otra vez.
/// </summary>
/// <remarks>
/// <para><b>El rol lo declara el paso</b> (009-P6 = A): la puerta es la guarda —el acceso <c>Personal</c> comprueba el
/// rol de quien atiende— y la capacidad lo recibe declarado, sin token. Un despliegue que exija roles verificados
/// rechaza el disparo (<c>workflow.roles_not_verified</c>) y usa la ruta directa.</para>
///
/// <para><b>El rechazo de la capacidad sube tal cual</b>: <c>workflow.transition_not_allowed</c> si la instancia ya no
/// está donde la transición sale, <c>workflow.role_required</c> si el rol no alcanza.</para>
/// </remarks>
public sealed class PasoProcesoDisparar(IWorkflowPort procesos) : IPaso
{
    public string Tipo => "workflow.disparar";
    public int Lecturas => 3;
    public int LecturasOpcionales => 1;
    public int Escrituras => 1;
    public int EscriturasOpcionales => 1;
    public LlaveRequerida Llave => LlaveRequerida.Fija;
    public bool UsaProceso => true;
    public bool UsaTransicion => true;
    public bool UsaRol => true;
    public bool SinVuelta => true;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return PasosDeProcesos.DispararAsync(
            procesos, entrada, entrada.Paso.Transicion!, entrada.Paso.Lee.Count > 2 ? entrada.Lee<string?>(2) : null, ct);
    }
}

/// <summary>
/// <c>workflow.disparar-elegida</c>: lo mismo que <c>workflow.disparar</c>, con la transición que elige quien actúa
/// entre las de la lista cerrada <c>transiciones</c>. Lee la instancia, quién actúa, la elegida y —si la definición la
/// nombra— la nota.
/// </summary>
/// <remarks>
/// Una elegida que no está en la lista se rechaza sin llamar a la capacidad (<c>flow.transicion_no_admitida</c>): la
/// lista es lo que el flujo deja hacer, y que el proceso tenga más transiciones no la amplía.
/// </remarks>
public sealed class PasoProcesoDispararElegida(IWorkflowPort procesos) : IPaso
{
    public string Tipo => "workflow.disparar-elegida";
    public int Lecturas => 4;
    public int LecturasOpcionales => 1;
    public int Escrituras => 1;
    public int EscriturasOpcionales => 1;
    public LlaveRequerida Llave => LlaveRequerida.Fija;
    public bool UsaProceso => true;
    public bool UsaTransiciones => true;
    public bool UsaRol => true;
    public bool SinVuelta => true;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var elegida = entrada.Lee<string>(2);
        if (!entrada.Paso.Transiciones!.Contains(elegida, StringComparer.Ordinal))
        {
            return Task.FromResult(SalidaDePaso.Rechaza(Rejection.Invalid(PasosDeProcesos.TransicionNoAdmitida,
                $"«{elegida}» no es una de las que este acto deja elegir: {string.Join(", ", entrada.Paso.Transiciones!)}.")));
        }
        return PasosDeProcesos.DispararAsync(
            procesos, entrada, elegida, entrada.Paso.Lee.Count > 3 ? entrada.Lee<string?>(3) : null, ct);
    }
}

/// <summary>
/// <c>workflow.leer</c>: lee la instancia y escribe el estado en que está. Lee, no escribe: sin llave, y puede correr
/// en una lectura sin saga.
/// </summary>
public sealed class PasoProcesoLeer(IWorkflowPort procesos) : IPaso
{
    public string Tipo => "workflow.leer";
    public int Lecturas => 1;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public bool UsaProceso => true;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string)];

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var leida = await procesos.LeerAsync(entrada.Lee<string>(0), ct);
        if (!leida.IsOk) return SalidaDePaso.Rechaza(leida.Rejection!);
        return PasosDeProcesos.Ajena(entrada, leida.Value) is { } ajena
            ? SalidaDePaso.Rechaza(ajena)
            : SalidaDePaso.Sigue(leida.Value.Estado);
    }
}

/// <summary>Lo que comparten los pasos de procesos.</summary>
public static class PasosDeProcesos
{
    /// <summary>La instancia es de otro proceso que el que declara el paso.</summary>
    public const string ProcesoAjeno = "flow.proceso_ajeno";

    /// <summary>La transición elegida no está en la lista cerrada del paso.</summary>
    public const string TransicionNoAdmitida = "flow.transicion_no_admitida";

    /// <summary>El rechazo si la instancia no es del proceso que declara el paso; <c>null</c> si lo es.</summary>
    /// <remarks>
    /// La capacidad guarda una transición por su nombre y su estado de origen: dos procesos pueden tener «approve» desde
    /// «in-review», y disparar sobre la instancia equivocada aprobaría otro expediente.
    /// </remarks>
    internal static Rejection? Ajena(EntradaDePaso entrada, InstanciaDeProceso instancia)
        => string.Equals(instancia.Proceso, entrada.Paso.Proceso, StringComparison.Ordinal)
            ? null
            : Rejection.Conflict(ProcesoAjeno,
                $"La instancia {instancia.Id} sigue «{instancia.Proceso}», y el paso «{entrada.Paso.Id}» actúa sobre «{entrada.Paso.Proceso}».");

    /// <summary>Lee la instancia, comprueba su proceso y dispara con el rol del paso y la llave fija.</summary>
    internal static async Task<SalidaDePaso> DispararAsync(
        IWorkflowPort procesos, EntradaDePaso entrada, string transicion, string? nota, CancellationToken ct)
    {
        var id = entrada.Lee<string>(0);
        var leida = await procesos.LeerAsync(id, ct);
        if (!leida.IsOk) return SalidaDePaso.Rechaza(leida.Rejection!);
        if (Ajena(entrada, leida.Value) is { } ajena) return SalidaDePaso.Rechaza(ajena);

        var disparada = await procesos.DispararAsync(id, transicion, entrada.Lee<Ref>(1), [entrada.Paso.Rol!], nota, entrada.Llave(), ct);
        return disparada.IsOk ? SalidaDePaso.Sigue(disparada.Value.Estado) : SalidaDePaso.Rechaza(disparada.Rejection!);
    }
}
