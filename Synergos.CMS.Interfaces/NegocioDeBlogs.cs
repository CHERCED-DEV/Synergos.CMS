namespace Synergos.CMS.Interfaces;

/// <summary>Los valores de negocio de la red editorial ya fusionados para un sitio (ADR 0137, escala #196).</summary>
/// <param name="ApiBase">Dónde vive la API de la funcionalidad (relativa al sitio o absoluta).</param>
/// <param name="LecturasBase">La base de las lecturas genéricas de Blogs (012-blogs-social, I4): el catálogo de la red,
/// los guardados y los mensajes directos con forma neutra. Vacía, la app vuelve a las rutas interinas de
/// <paramref name="ApiBase"/>.</param>
/// <param name="ModeracionBase">Dónde se denuncia lo publicado y vive la cola de moderación (012-blogs-social, I5).
/// Vacía, la app no ofrece denunciar.</param>
public sealed record NegocioDeBlogs(string ApiBase, string LecturasBase, string ModeracionBase);
