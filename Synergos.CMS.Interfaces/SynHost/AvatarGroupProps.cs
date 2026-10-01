namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-avatar-group&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba el TEXTO <c>avatarsJson</c> y
/// <c>maxVisible</c> como texto; el elemento lee la LISTA <c>avatars</c> y un NÚMERO: el grupo
/// colocado decía «No hay integrantes para mostrar» y el tope de visibles quedaba en 5.</para>
///
/// <para><b>Cada integrante lleva <c>name</c> y <c>src</c>.</b> El editor escribe <c>url</c> para
/// la FOTO (así lo documenta el ElementType) y el elemento lee <c>url</c> como el ENLACE del
/// integrante: sin traducir, la foto se volvía un enlace que el elemento ni siquiera pinta. El
/// resolver la manda como <c>src</c>. <c>role</c> lo documenta el ElementType y el elemento no lo
/// pinta: no viaja. Un integrante sin nombre ni foto no viaja y se anota.</para>
///
/// <para><b><c>label</c></b> es el <c>ariaLabel</c> del bloque: el elemento lo usa para nombrar el
/// grupo («Equipo: 8 integrantes»). <c>size</c> y <c>overflowHref</c> los acepta el elemento y no
/// los autora el ElementType: quedan como atributos.</para>
///
/// <para><b>Su microcopia sale del diccionario</b> (ADR 0136): sección <c>AvatarGroup</c> —el nombre por
/// defecto, el recuento con su plural, el desborde y el grupo vacío— y <c>Avatar</c> por una sola clave,
/// <c>Avatar.Fallback</c>: el nombre de un avatar sin nombre es el mismo concepto que en
/// <c>avatar</c>, así que vive una vez, en la sección de la pieza base. No comparten la sección
/// entera: los textos del grupo no los usa el avatar suelto, y en <c>Avatar</c> se publicarían en cada
/// página con un avatar.</para>
/// </remarks>
[ElementoSynHost("avatar-group", TipoDeColocable.Pieza, Diccionario = ["AvatarGroup", "Avatar"])]
public sealed record AvatarGroupProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] IReadOnlyList<AvatarGroupMember>? Avatars,
    [property: CampoSynHost(OrigenDelCampo.Decision)] int? MaxVisible,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Label);

/// <summary>Un integrante del grupo: su nombre (iniciales y nombre accesible) y su foto.</summary>
public sealed record AvatarGroupMember(string? Name = null, string? Src = null);
