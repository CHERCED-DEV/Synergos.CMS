using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// La configuración de negocio de la consola del vendedor (ADR 0137) — sección <c>Synergos:Features:Seller</c>.
/// </summary>
/// <remarks>
/// Antes, dónde vive la API estaba tres veces: campo del editor, valor por defecto en la vista
/// Razor y <c>DEFAULT_API_BASE</c> en el bundle. Medido el 2026-10-02 en la base: el editor nunca
/// lo cambió. Ahora es del despliegue, por sitio (#196).
/// </remarks>
public sealed class SellerFeatureSettings : SeccionDeNegocio<SellerFeatureSitio, NegocioDeSeller>
{
    /// <summary>La sección de configuración.</summary>
    public const string Seccion = "Synergos:Features:Seller";

    /// <summary>Dónde vive la API de la funcionalidad: relativa al sitio o absoluta.</summary>
    public string ApiBase { get; init; } = "/api/shop";

    /// <inheritdoc />
    protected override NegocioDeSeller Fusionar(SellerFeatureSitio? propio)
        => new(ApiBase: propio?.ApiBase ?? ApiBase);

    /// <inheritdoc />
    protected override IEnumerable<string> ProblemasDe(NegocioDeSeller negocio)
        => new[] { ReglasDeNegocio.ApiBase("ApiBase", negocio.ApiBase) }.OfType<string>();
}

/// <summary>
/// Lo que un sitio cambia de <see cref="SellerFeatureSettings"/>. Una clave sin valor la hereda.
/// </summary>
public sealed class SellerFeatureSitio
{
    /// <inheritdoc cref="SellerFeatureSettings.ApiBase"/>
    public string? ApiBase { get; init; }
}
