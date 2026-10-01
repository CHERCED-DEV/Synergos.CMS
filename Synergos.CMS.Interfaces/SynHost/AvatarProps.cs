namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-avatar&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba la foto como <c>avatarSrc</c> y el
/// elemento lee <c>src</c>: el avatar colocado pintaba el icono genérico con el nombre accesible
/// «Avatar de usuario», nunca la foto que eligió el editor.</para>
///
/// <para><b><c>src</c> es la URL absoluta del medio</b> (MediaPicker3 <c>avatarImage</c>).
/// <b><c>alt</c></b> es el nombre accesible de la foto: el <c>ariaLabel</c> que el editor escribe
/// en el bloque (<c>compDomAttributes</c>) y, si lo deja vacío, el texto alternativo del medio
/// (<c>altDefault</c>) — el mismo orden que ya sigue <c>MediaText</c>.</para>
///
/// <para><c>name</c>, <c>size</c>, <c>shape</c> y <c>status</c> los acepta el elemento y no los
/// autora el ElementType: quedan como atributos, no viajan en el <c>config</c>.</para>
///
/// <para><b>Su microcopia sale del diccionario, sección <c>Avatar</c></b> (ADR 0136): los cuatro estados
/// de presencia y <c>Avatar.Fallback</c>, el nombre de un avatar sin nombre, que <c>avatar-group</c>
/// usa también: una clave, no dos copias.</para>
/// </remarks>
[ElementoSynHost("avatar", TipoDeColocable.Pieza, Diccionario = ["Avatar"])]
public sealed record AvatarProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Src,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Alt);
