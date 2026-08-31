namespace Synergos.CMS.Interfaces;

/// <summary>
/// Quién actúa, tal como lo nombra el CMS hacia el árbol de servicios.
/// </summary>
/// <param name="Kind">El vocabulario del negocio que despliega — <c>gov.funcionario</c>.</param>
/// <param name="Id">Quién, dentro de ese vocabulario. Viaja opaco.</param>
/// <param name="Roles">
/// Qué puede hacer. <b>Se usan al DAR DE ALTA la identidad, no en cada llamada</b>: el día que
/// una capacidad los exija verificados, tienen que venir firmados por quien los custodia, no
/// declarados por quien pide.
/// </param>
public sealed record IdentitySubject(string Kind, string Id, IReadOnlyList<string> Roles);

/// <summary>
/// Consigue una identidad VERIFICABLE para quien está actuando (HU #14).
/// </summary>
/// <remarks>
/// <para><b>Qué compra, y qué no.</b> Hasta acá el CMS decía quién actuaba y las capacidades le
/// creían: cualquiera con la llave compartida podía escribir otro nombre y otros roles en el
/// cuerpo de la petición (defectos #42 y #48). Un token firmado por <c>Api.Identity</c> cierra
/// eso — el sujeto viene firmado y no se puede reapuntar. Lo que <b>no</b> compra es prueba
/// frente a un tercero: lo emite un servicio nuestro a partir de nuestra propia sesión, así que
/// la cadena de confianza toca fondo en el mismo sitio.</para>
///
/// <para><b>Devolver <c>null</c> es una respuesta legítima y frecuente.</b> Un clon limpio no
/// tiene <c>Api.Identity</c> levantada, y un despliegue puede no querer identidad verificada
/// todavía. Quien llama sigue adelante declarando quién actúa —que es exactamente lo que hacía
/// antes— y la capacidad decide si eso le alcanza. Lo que NO puede pasar es que la falta de
/// identidad tumbe la operación: un trámite no se cae porque la identidad esté caída.</para>
///
/// <para><b>Por eso esto nunca lanza.</b> Si lanzara, la primera caída de <c>Api.Identity</c>
/// pararía las decisiones de ventanilla — que es justamente el punto único de fallo que la HU
/// #14 evitó al verificar los tokens en local.</para>
/// </remarks>
public interface IIdentityTokenIssuer
{
    /// <summary>
    /// Un token vigente para <paramref name="subject"/>, o <c>null</c> si no se puede emitir.
    /// </summary>
    Task<string?> IssueAsync(IdentitySubject subject, CancellationToken cancellationToken = default);
}
