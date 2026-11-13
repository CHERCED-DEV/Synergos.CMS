using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// La configuración de negocio de la red editorial (ADR 0137) — sección <c>Synergos:Features:Blogs</c>.
/// </summary>
/// <remarks>
/// Antes, dónde vive la API estaba tres veces: campo del editor, valor por defecto en la vista
/// Razor y <c>DEFAULT_API_BASE</c> en el bundle. Medido el 2026-10-02 en la base: el editor nunca
/// lo cambió. Ahora es del despliegue, por sitio (#196).
/// </remarks>
public sealed class BlogsFeatureSettings : SeccionDeNegocio<BlogsFeatureSitio, NegocioDeBlogs>
{
    /// <summary>La sección de configuración.</summary>
    public const string Seccion = "Synergos:Features:Blogs";

    /// <summary>Dónde vive la API de la funcionalidad: relativa al sitio o absoluta.</summary>
    public string ApiBase { get; init; } = "/api/blogs";

    /// <summary>
    /// La base de las lecturas genéricas de Blogs (012-blogs-social, I4): el catálogo de la red, los guardados y los
    /// mensajes directos con forma neutra, como la tienda y Salud.
    /// </summary>
    /// <remarks>
    /// Vacía, la app vuelve a leer las rutas interinas de <see cref="ApiBase"/>: es la vuelta atrás sin desplegar.
    /// </remarks>
    public string LecturasBase { get; init; } = "/api/lecturas/" + CatalogoDeBlogs.Vertical;

    /// <inheritdoc />
    protected override NegocioDeBlogs Fusionar(BlogsFeatureSitio? propio)
        => new(ApiBase: propio?.ApiBase ?? ApiBase, LecturasBase: (propio?.LecturasBase ?? LecturasBase).Trim());

    /// <inheritdoc />
    protected override IEnumerable<string> ProblemasDe(NegocioDeBlogs negocio)
        => new[]
        {
            ReglasDeNegocio.ApiBase("ApiBase", negocio.ApiBase),
            // Vacía vale: es la vuelta atrás.
            string.IsNullOrWhiteSpace(negocio.LecturasBase) ? null : ReglasDeNegocio.ApiBase("LecturasBase", negocio.LecturasBase),
        }.OfType<string>();
}

/// <summary>
/// Lo que un sitio cambia de <see cref="BlogsFeatureSettings"/>. Una clave sin valor la hereda.
/// </summary>
public sealed class BlogsFeatureSitio
{
    /// <inheritdoc cref="BlogsFeatureSettings.ApiBase"/>
    public string? ApiBase { get; init; }

    /// <inheritdoc cref="BlogsFeatureSettings.LecturasBase"/>
    public string? LecturasBase { get; init; }
}
