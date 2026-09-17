namespace Synergos.CMS.Interfaces;

/// <summary>
/// Una lección tal como la autoró alguien, antes de volverse <see cref="CourseLesson"/>.
/// Lleva el CUERPO, que la lección del contrato no tiene.
/// </summary>
/// <remarks>
/// <b>El cuerpo viaja aparte porque <see cref="CourseLesson"/> no lo lleva a propósito:</b> su
/// <see cref="CourseLesson.ContentItemId"/> apunta al item del <see cref="IContentStream"/>
/// (<c>Kind=lesson</c>) del que sale, y ese id lo asigna el feed al sembrar — no lo puede saber
/// quien lee el contenido. Así que la fuente entrega el cuerpo y quien siembra resuelve el id.
///
/// <para><b>Por qué vive en Interfaces y no en Web.</b> Lo PRODUCE la fuente de contenido
/// (<c>UmbracoCourseCatalogSource</c>, en Web) y lo CONSUME el catálogo
/// (<c>CatalogCourseCatalogProvider</c>, en Application), y Application no puede mirar a Web —
/// el grafo va en una sola dirección (ADR 0002). Interfaces es el único sitio que los dos ven.
/// </para>
/// </remarks>
public sealed record AuthoredLesson(
    string Id,
    string Title,
    int Order,
    int DurationMinutes,
    string? VideoRef,
    string Body,
    IReadOnlyList<CourseResource> Resources,
    bool IsPreview);

/// <summary>Un módulo autorado, con sus lecciones todavía sin sembrar.</summary>
public sealed record AuthoredModule(
    string Id,
    string Title,
    int Order,
    IReadOnlyList<AuthoredLesson> Lessons);

/// <summary>
/// Un curso autorado completo: el detalle que sirve el catálogo más el currículum con los
/// cuerpos de sus lecciones, que todavía no están en el feed.
/// </summary>
/// <remarks>
/// El <see cref="CourseDetail.Modules"/> de <see cref="Detail"/> llega VACÍO a propósito: se
/// puebla al sembrar, cuando cada lección ya tiene su <see cref="CourseLesson.ContentItemId"/>.
/// Rellenarlo antes obligaría a inventar ese id, que es el defecto que este tipo evita.
/// </remarks>
public sealed record AuthoredCourse(
    CourseDetail Detail,
    IReadOnlyList<AuthoredModule> Modules);
