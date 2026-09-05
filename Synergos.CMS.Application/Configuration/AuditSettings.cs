namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Contra qué se lleva la bitácora (HU #15) — sección <c>Synergos:Audit</c>.
/// </summary>
/// <remarks>
/// <para><b>El default es <c>Local</c>, y no es una transición.</b> El JSONL de
/// <c>App_Data/syn-audit/</c> es el camino del clon limpio y sigue siendo el <b>modelo de
/// lectura</b> incluso con la capacidad encendida: las lecturas del seam son SÍNCRONAS y la
/// bitácora del backoffice se pinta en cada carga. Es la misma decisión del seguimiento de
/// pedidos (#46) y por la misma razón: con <c>Api.Audit</c> caída el administrador SIGUE viendo
/// qué pasó; lo que se para es que el asiento salga de esta máquina.</para>
///
/// <para><b>Y por eso el orden importa</b>: se escribe local primero y se reenvía después. Al
/// revés, un fallo de red dejaría al backoffice sin el asiento que sí se pudo guardar.</para>
/// </remarks>
public sealed class AuditSettings
{
    /// <summary><c>Local</c> (default, sólo el JSONL) o <c>Api</c> (además contra <c>Api.Audit</c>).</summary>
    public string Mode { get; init; } = "Local";

    /// <summary>Dónde vive la capacidad de bitácora.</summary>
    public string BaseUrl { get; init; } = "http://127.0.0.1:5222/";

    /// <summary>La llave compartida servicio↔servicio. Sin ella todo responde 401.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// El <c>Kind</c> con el que este despliegue nombra a quien opera el sitio.
    /// </summary>
    /// <remarks>
    /// Viaja opaco: la capacidad lo guarda y lo devuelve, y no ramifica sobre él
    /// (<c>CLAUDE.md</c> §13).
    /// </remarks>
    public string ActorKind { get; init; } = "cms.actor";

    /// <summary>El <c>Kind</c> con el que viaja el recurso sobre el que se actuó.</summary>
    public string TargetKind { get; init; } = "cms.recurso";

    /// <summary>
    /// Segundos de espera. Corto a propósito, al revés que en el acto notificado (#62).
    /// </summary>
    /// <remarks>
    /// Allá el registro del acceso ES la operación y cortar pronto es peor que esperar. Acá el
    /// asiento ya está guardado en local antes de salir a la red: alargar el timeout sólo hace que
    /// el administrador espere por un reenvío cuyo fallo ya está previsto y anotado.
    /// </remarks>
    public int TimeoutSeconds { get; init; } = 5;
}
