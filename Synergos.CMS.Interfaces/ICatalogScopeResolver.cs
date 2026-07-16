namespace Synergos.CMS.Interfaces;

/// <summary>
/// A qué siteRoot pertenece el catálogo del request en curso. El Web lo resuelve y lo pasa
/// como DATO en <see cref="CatalogQuery.Scope"/>; Application nunca conoce Umbraco (ADR 0002).
/// </summary>
/// <remarks>
/// <b>El problema real que cierra:</b> <c>productPage</c> lo comparten TRES verticales —
/// Tienda, Booking (servicios reservables) y Propiedades (inmuebles). El truco habitual para
/// acotar por sitio, <c>AncestorOrSelf("siteRoot")</c>, <b>no funciona en ruta API</b>:
/// <c>ShopCatalogController</c> es <c>[ApiController]</c> y no tiene <c>PublishedRequest</c>,
/// así que cae al fallback <c>GetAtRoot()</c> = TODOS los siteRoots. Sin resolver esto por
/// otra vía, <c>/api/shop/search</c> sirve "Masaje terapéutico 60 min" y "Casa campestre en
/// Los Lagos" entre los productos.
///
/// <para>Se resuelve por el hostname del request → Umbraco Domain → siteRoot, que es la vía
/// que sí existe fuera del pipeline de contenido. Ver
/// <c>reference_siteroot_identity_system</c> y la trampa <c>.Root()</c> vs
/// <c>AncestorOrSelf("siteRoot")</c>.</para>
///
/// <para>No es multi-tenancy: un deploy sigue siendo un origen y el scope sale del hostname
/// nativo de Umbraco, no de un <c>ITenantContext</c> (prohibido — ver
/// <c>feedback_product_not_saas_multitenant</c>).</para>
/// </remarks>
public interface ICatalogScopeResolver
{
    /// <summary>
    /// El scope del request en curso, o <c>null</c> si no se puede determinar (fuera de un
    /// request, hostname sin Domain configurado). <b>Null significa "sin acotar", así que un
    /// llamador que necesite aislamiento debe tratarlo como un fallo, no como un permiso.</b>
    /// </summary>
    string? ResolveScope();
}
