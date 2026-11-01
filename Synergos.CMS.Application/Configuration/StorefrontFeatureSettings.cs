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

    /// <summary>
    /// La transportadora con que se despacha una compra por la puerta (<c>tienda.compra</c>).
    /// </summary>
    /// <remarks>
    /// «default» es la que usa hoy la ruta directa cuando <c>Synergos:Tienda:Carrier</c> no dice otra
    /// (<c>HttpShopOrderService</c>), que se conserva para ese camino: con los valores base, los dos
    /// despachan igual.
    /// </remarks>
    public string Carrier { get; init; } = "default";

    /// <summary>
    /// Por dónde compra la tienda, si el despliegue lo declara: <c>puerta</c> o <c>directa</c>
    /// (<see cref="CaminoDeCompra"/>). Sin valor no se declara nada y decide la regla: la puerta cuando
    /// abre <c>tienda.compra</c>, y la ruta directa en otro caso.
    /// </summary>
    /// <remarks>Un sitio con el valor vacío vuelve a la regla aunque los valores base declaren uno.</remarks>
    public string? PurchasePath { get; init; }

    /// <summary>
    /// El país al que despacha la tienda, ISO 3166-1 alfa-2 (<c>CO</c>). La dirección de una compra por la
    /// puerta lo exige y el formulario de envío no lo pregunta: es del despliegue, no de quien compra.
    /// </summary>
    public string ShippingCountry { get; init; } = "CO";

    /// <summary>
    /// La base de las lecturas genéricas de la tienda (014-despiece-tienda, I4): el catálogo, los pedidos, las
    /// devoluciones, la lista de deseos, los mensajes y las reseñas con forma neutra de vertical.
    /// </summary>
    /// <remarks>
    /// Vacía, la tienda vuelve a leer las rutas interinas de <see cref="ApiBase"/> (<c>api/shop</c>): es la vuelta
    /// atrás sin desplegar.
    /// </remarks>
    public string LecturasBase { get; init; } = LecturasDeLaTienda.Base;

    /// <inheritdoc />
    protected override NegocioDeStorefront Fusionar(StorefrontFeatureSitio? propio)
        => new(
            ApiBase: propio?.ApiBase ?? ApiBase,
            Carrier: propio?.Carrier ?? Carrier,
            PurchasePath: CaminoDeCompra.Normalizar(propio?.PurchasePath ?? PurchasePath),
            ShippingCountry: (propio?.ShippingCountry ?? ShippingCountry).Trim().ToUpperInvariant(),
            LecturasBase: (propio?.LecturasBase ?? LecturasBase).Trim());

    /// <inheritdoc />
    protected override IEnumerable<string> ProblemasDe(NegocioDeStorefront negocio)
        => new[]
        {
            ReglasDeNegocio.ApiBase("ApiBase", negocio.ApiBase),
            LecturasDeLaTienda.Problema("LecturasBase", negocio.LecturasBase),
            string.IsNullOrWhiteSpace(negocio.Carrier) ? "Carrier: hace falta una transportadora; sin ella no se despacha." : null,
            ReglasDeNegocio.Camino("PurchasePath", negocio.PurchasePath),
            negocio.ShippingCountry is { Length: 2 } pais && pais.All(char.IsAsciiLetterUpper)
                ? null
                : $"ShippingCountry: «{negocio.ShippingCountry}» no es un país ISO 3166-1 alfa-2 (CO, MX…).",
        }.OfType<string>();
}

/// <summary>
/// Lo que un sitio cambia de <see cref="StorefrontFeatureSettings"/>. Una clave sin valor la hereda.
/// </summary>
public sealed class StorefrontFeatureSitio
{
    /// <inheritdoc cref="StorefrontFeatureSettings.ApiBase"/>
    public string? ApiBase { get; init; }

    /// <inheritdoc cref="StorefrontFeatureSettings.Carrier"/>
    public string? Carrier { get; init; }

    /// <inheritdoc cref="StorefrontFeatureSettings.PurchasePath"/>
    public string? PurchasePath { get; init; }

    /// <inheritdoc cref="StorefrontFeatureSettings.ShippingCountry"/>
    public string? ShippingCountry { get; init; }

    /// <inheritdoc cref="StorefrontFeatureSettings.LecturasBase"/>
    public string? LecturasBase { get; init; }
}
