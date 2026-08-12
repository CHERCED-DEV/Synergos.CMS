namespace Synergos.Api.Workflow.Domain;

/// <summary>
/// Qué prueba exige este despliegue para creerle un rol a quien dispara (defecto #48).
/// </summary>
/// <remarks>
/// <para><b>Existe porque el arreglo completo no se puede encender todavía.</b> Cerrar el agujero
/// del todo es exigir un token verificado para toda transición con <c>requiredRoles</c>, y hoy
/// <b>nadie puede presentar uno</b>: el puente Member ↔ Principal de la HU #14 no está construido,
/// así que el CMS no tiene con qué. Exigirlo por defecto rompería Gobierno (#44), que manda el rol
/// a mano.</para>
///
/// <para><b>Es la forma que ya usó la HU #27 con los cobros</b>: el despliegue declara su postura,
/// y lo que no existe es el stub sirviendo en silencio. Un servidor que ya tenga identidad lo
/// enciende y la guarda pasa a guardar de verdad; el clon limpio sigue arrancando.</para>
/// </remarks>
public sealed class WorkflowRoleOptions
{
    /// <summary>
    /// Con <c>true</c>, una transición que exige rol <b>sólo</b> acepta roles de un token
    /// verificado. Con <c>false</c> (default) se aceptan los declarados, que guardan contra el
    /// accidente y no contra quien quiera saltarse la guarda.
    /// </summary>
    public bool RequireVerifiedRoles { get; init; }
}
