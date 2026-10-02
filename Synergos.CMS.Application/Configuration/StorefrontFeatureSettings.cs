using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// La configuración de negocio de la tienda (ADR 0137) — sección <c>Synergos:Features:Storefront</c>.
/// </summary>
/// <remarks>
/// Antes, dónde vive la API estaba tres veces: campo del editor, valor por defecto en la vista
/// Razor y <c>DEFAULT_API_BASE</c> en el bundle. Medido el 2026-10-02 en la base: el editor nunca
/// lo cambió. Ahora es del despliegue, por sitio (#196).
/// </remarks>
public sealed class StorefrontFeatureSettings : SeccionDeNegocio<StorefrontFeatureSitio, NegocioDeStorefront>
{
    /// <summary>La sección de configuración.</summary>
    public const string Seccion = "Synergos:Features:Storefront";

    /// <summary>Dónde vive la API de la funcionalidad: relativa al sitio o absoluta.</summary>
    public string ApiBase { get; init; } = "/api/shop";

    /// <inheritdoc />
    protected override NegocioDeStorefront Fusionar(StorefrontFeatureSitio? propio)
        => new(ApiBase: propio?.ApiBase ?? ApiBase);

    /// <inheritdoc />
    protected override IEnumerable<string> ProblemasDe(NegocioDeStorefront negocio)
        => new[] { ReglasDeNegocio.ApiBase("ApiBase", negocio.ApiBase) }.OfType<string>();
}

/// <summary>
/// Lo que un sitio cambia de <see cref="StorefrontFeatureSettings"/>. Una clave sin valor la hereda.
/// </summary>
public sealed class StorefrontFeatureSitio
{
    /// <inheritdoc cref="StorefrontFeatureSettings.ApiBase"/>
    public string? ApiBase { get; init; }
}
