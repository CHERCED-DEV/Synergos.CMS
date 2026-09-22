namespace Synergos.CMS.Interfaces;

/// <summary>
/// Un post editorial tal como lo autoró alguien en el CMS (<c>postPage</c>), antes de estar en
/// el feed.
/// </summary>
/// <remarks>
/// <para><b>Por qué existe este tipo y no se crea el <see cref="ContentStreamItem"/> directo.</b>
/// Un item del feed lleva <see cref="ContentStreamItem.Id"/> y
/// <see cref="ContentStreamItem.CreatedUtc"/> que <b>asigna el stream</b>, y unas
/// <see cref="ContentMetrics"/> que salen de las reacciones vivas. Quien lee el árbol de
/// contenido no sabe ninguna de las tres, así que entrega lo que SÍ es del editor y quien
/// siembra resuelve el resto — el mismo reparto que <see cref="AuthoredLesson"/> (#100).</para>
///
/// <para><b>Por qué vive en Interfaces.</b> Lo PRODUCE <c>UmbracoSocialContentSource</c> (Web) y
/// lo CONSUME <c>CatalogContentStream</c> (Application), y Application no puede mirar a Web
/// (ADR 0002). Interfaces es el único sitio que los dos ven.</para>
///
/// <para><b>El cuerpo es el <c>excerpt</c> y no el Block Grid, y es una decisión.</b> El cuerpo
/// de un <c>postPage</c> son sus <c>sections</c>: bloques compuestos con el Layout Composer, no
/// texto. Aplanarlos a una cadena produciría algo que <i>parece</i> el post y no lo es —la
/// familia de <c>feedback_a_fabrication_can_be_a_derivation</c>—, así que lo que va al feed es
/// el resumen que el editor escribió, y el post entero se lee en su URL. Un
/// <c>postPage</c> sin <c>excerpt</c> <b>no se siembra</b>: no hay nada honesto que mostrar en
/// la tarjeta.</para>
/// </remarks>
/// <param name="Id">El slug del nodo. Estable al editar, que es lo que hace que la huella del
///   cuerpo pueda cambiar sin perder de vista cuál post es.</param>
/// <param name="Title">El título, tal como lo escribió el editor.</param>
/// <param name="Excerpt">El resumen. Es lo que va al feed como cuerpo del item.</param>
/// <param name="Url">La URL del post en el sitio — lo que la tarjeta enlaza.</param>
/// <param name="HeroImageUrl">La imagen destacada, o <c>null</c>.</param>
/// <param name="HeroImageAlt">Su texto alternativo <b>tal como lo escribió quien la subió</b>, o
///   <c>null</c> si nadie lo escribió. NO se deriva del título: <see cref="ContentStreamItem"/>
///   ya dice por qué —«derivarlo del cuerpo es un apaño razonable para pintar, pero guardarlo
///   como si fuera suyo hace invisible que nunca lo escribió»—.</param>
/// <param name="PublishedUtc">Cuándo se publicó, o <c>null</c> si el editor no lo dijo.</param>
/// <param name="Kind">Qué es para el feed: <c>article</c> para un post editorial largo. Va acá y
///   no cableado en quien siembra porque es una propiedad del contenido.</param>
/// <param name="Author">Quién lo firma.</param>
public sealed record AuthoredPost(
    string Id,
    string Title,
    string Excerpt,
    string Url,
    string? HeroImageUrl,
    string? HeroImageAlt,
    DateTime? PublishedUtc,
    string Kind,
    AuthoredPostAuthor Author);

/// <summary>
/// Quien firma un post autorado: la proyección del <c>authorPage</c> que su <c>authorRef</c>
/// nombra.
/// </summary>
/// <remarks>
/// <para><b>Hace falta, y descubrirlo fue medio hallazgo</b> (#146). <c>StubContentStream</c>
/// resuelve el autor de un item con <c>SocialDemoSeed.AuthorById</c>, que ante un id que no
/// conoce devuelve <c>new ContentAuthor(actorId, actorId, actorId, null, false)</c> — o sea
/// FABRICA el handle y el nombre a partir del identificador. Sembrar un post autorado sin
/// traerse a su autor habría puesto en la tarjeta un «@autor-camila-rios» que nadie escribió, y
/// eso no se lee como un defecto: se lee como un handle.</para>
///
/// <para><b><see cref="Handle"/> se DERIVA del segmento de URL y eso no es fabricar.</b> Un
/// handle es un identificador legible, y el slug del nodo es exactamente eso — no es una
/// afirmación sobre la persona. <see cref="DisplayName"/> y <see cref="AvatarUrl"/> sí son
/// datos, y salen de <c>compContentAuthor</c>.</para>
///
/// <para><b>Lo que NO se trae es <c>Verified</c>, y va dicho.</b> El schema no declara ese
/// campo, así que no hay nada que leer; <see cref="ContentAuthor.Verified"/> es un <c>bool</c>
/// cuyo default es <c>false</c>, y ahí <c>false</c> es la lectura conservadora correcta —«no
/// consta que alguien lo haya verificado»— y no la afirmación peligrosa del addendum #111, que
/// era decir «no» sobre algo que sí podía ser «sí». Distinguir «no consta» de «no» exigiría un
/// <c>bool?</c> en el contrato, y eso cruza al otro árbol: el normalizador del UI repone su
/// propio default. <b>El disparador para reabrirlo</b>: que alguien pida el check en el
/// backoffice.</para>
/// </remarks>
public sealed record AuthoredPostAuthor(
    string ActorId,
    string Handle,
    string DisplayName,
    string? AvatarUrl);
