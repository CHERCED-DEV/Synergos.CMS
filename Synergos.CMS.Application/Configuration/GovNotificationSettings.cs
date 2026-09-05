namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Contra qué se pone un acto en conocimiento (HU #62) — sección
/// <c>Synergos:Gob:Notifications</c>.
/// </summary>
/// <remarks>
/// <para><b>Va por SU PROPIO interruptor y no por <c>Synergos:Gob:Mode</c></b>, que decide contra
/// qué avanza el expediente (<c>Api.Workflow</c>, #44). Son dos capacidades distintas y un
/// despliegue puede querer una y no la otra; juntarlas obligaría a encender las dos para probar
/// una, y a apagar la notificación el día que hubiera que apagar el motor de trámites.</para>
///
/// <para><b>El default es <c>Local</c>, y no es una transición.</b> Una entidad pequeña que no
/// despliega el árbol de servicios sigue pudiendo notificar y registrar accesos; lo que no tiene
/// es la afirmación verificable de identidad, y por eso todo lo que registra dice
/// <c>CmsSession</c> — que es la verdad sobre ello.</para>
/// </remarks>
public sealed class GovNotificationSettings
{
    /// <summary><c>Local</c> (default, el registro de este lado) o <c>Api</c> (contra <c>Api.Messaging</c>).</summary>
    public string Mode { get; init; } = "Local";

    /// <summary>Dónde vive la capacidad de mensajería.</summary>
    public string BaseUrl { get; init; } = "http://127.0.0.1:5221/";

    /// <summary>La llave compartida servicio↔servicio. Sin ella todo responde 401.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Cómo se llama la entidad que notifica, para <c>Api.Messaging</c>.
    /// </summary>
    /// <remarks>
    /// Viaja opaco: la capacidad lo guarda y lo devuelve, y no ramifica sobre él
    /// (<c>CLAUDE.md</c> §13). Es configurable porque el nombre de quien notifica es del negocio
    /// que despliega, no de la plataforma.
    /// </remarks>
    public string EntityKind { get; init; } = "gov.entidad";

    /// <summary>Qué ventanilla concreta notifica.</summary>
    public string EntityId { get; init; } = "ventanilla";

    /// <summary>El <c>Kind</c> con el que este vertical nombra al ciudadano.</summary>
    public string CitizenKind { get; init; } = "gov.ciudadano";

    /// <summary>
    /// Segundos de espera. Generoso a propósito: registrar un acceso NO es auxiliar.
    /// </summary>
    /// <remarks>
    /// Cortarlo pronto es peor que esperar. Un timeout no dice «no se registró» — dice «no sé», y
    /// acá el peor resultado posible es un ciudadano leyendo un acto cuyo término nadie empezó.
    /// </remarks>
    public int TimeoutSeconds { get; init; } = 20;
}
