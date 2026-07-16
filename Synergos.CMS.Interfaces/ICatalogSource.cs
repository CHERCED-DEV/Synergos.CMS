namespace Synergos.CMS.Interfaces;

/// <summary>
/// De dónde SALEN los ítems de un catálogo. Desacopla el ORIGEN de los datos del MOTOR que
/// los busca (<see cref="ICatalogIndex{T}"/>).
/// </summary>
/// <remarks>
/// <b>Es la pieza que hace que la mudanza a contenido sea un swap de una línea.</b> Hoy la
/// mercancía comprable está hardcodeada en C# y la que el editor autora en el CMS está
/// inerte: los dos universos no comparten un solo SKU. Cuando Tienda mude a contenido
/// (Ola A), lo único que cambia es qué impl de esta seam se registra —
/// <c>DemoCatalogSource</c> (el seed de siempre) o <c>UmbracoProductCatalogSource</c> (el
/// contenido del editor). El motor, el descriptor, la fachada y el controller no se enteran.
///
/// <para><b>Los stubs NO se borran.</b> Sobreviven como <c>DemoCatalogSource</c> detrás del
/// flag <c>Synergos:Catalog:Sources:*</c> (default <c>demo</c>), calcando el gating de
/// proveedor de T3/ADR 0104: rollback = una línea de config, sin redeploy. Son el fallback y
/// la red de la demo verificada e2e.</para>
///
/// <para><b>Sin <c>Version</c> ni caché, y de ahí sale gratis el read-your-writes:</b> cada
/// búsqueda pide los ítems de nuevo, así que un <c>Publish*</c> se ve en la siguiente
/// pantalla sin más. Es una de las razones por las que el motor es en memoria: un índice
/// Lucene indexa asíncrono, y el organizador publicaría un evento que su propia búsqueda no
/// devuelve — regresión visible en demo que ningún build verde atrapa. Si algún día una
/// fuente cara obliga a cachear, el invalidador se añade JUNTO con su consumidor; declararlo
/// antes solo documenta una caché que no existe.</para>
/// </remarks>
/// <typeparam name="T">El tipo de ítem del catálogo.</typeparam>
public interface ICatalogSource<T>
{
    /// <summary>
    /// Todos los ítems del catálogo, acotados a <paramref name="scope"/> si se pasa.
    /// Catálogo vacío devuelve <c>[]</c>, nunca null ni excepción.
    /// </summary>
    /// <param name="scope">
    /// El siteRoot, resuelto por <see cref="ICatalogScopeResolver"/>. Null = sin acotar.
    /// </param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    Task<IReadOnlyList<T>> GetAllAsync(string? scope = null, CancellationToken cancellationToken = default);
}
