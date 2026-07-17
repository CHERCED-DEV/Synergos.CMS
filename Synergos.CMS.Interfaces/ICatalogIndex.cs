namespace Synergos.CMS.Interfaces;

/// <summary>
/// El motor de búsqueda/filtrado/facetado de un catálogo de dominio, transversal a los
/// verticales (T5, doc 25). Recibe los ítems de un <see cref="ICatalogSource{T}"/>, aplica
/// una <see cref="CatalogQuery"/> y devuelve un <see cref="CatalogResult{T}"/>: los de la
/// página + las facetas con conteo + el total.
/// </summary>
/// <remarks>
/// <b>Por qué existe (la regla de oro del doc 25):</b> hoy Tienda, Eventos, Propiedades,
/// Trámites y Educación implementan CADA UNO su propio matching, su propio facetado y su
/// propio orden — 5 copias de la misma capacidad, con 3 firmas distintas de <c>SearchAsync</c>
/// y 2 records de faceta gemelos. Es el mismo cuadro que ADR 0105 encontró en storage (4
/// stores idénticos → <see cref="IJsonEntityStore"/>) y se colapsa igual: UNA impl detrás de
/// UNA seam. El comportamiento por vertical se DECLARA en un descriptor, no se recodifica.
///
/// <para><b>La forma no se inventó:</b> es la de <c>DiscoveryCriteria</c>
/// (<c>discovery-shell.ts:54-58</c>), ya probada en 6 verticales. La UI siempre habló UN
/// idioma —<c>facets: Record&lt;string, readonly string[]&gt;</c>, multi-valor, con
/// <c>page</c>— mientras el backend hablaba 5 dialectos y estrechaba las facetas a
/// <c>Dictionary&lt;string,string&gt;</c>, un valor por clave. Ahí se perdían los filtros
/// multi-select, en silencio. Esto hace que el backend hable el idioma de la UI (ADR 0083).</para>
///
/// <para><b>La implementación es en memoria a propósito, no por pereza</b> (ADR 0107): a
/// este volumen (~24 ítems por vertical; el propio <c>IShopQuery.cs:14</c> pone el umbral de
/// Lucene en "10k+") un índice invertido no compra rendimiento y sí cuesta 4 modos de fallo.
/// Examine 3.7.1 además no tiene facetado first-class, así que el conteo habría que
/// escribirlo en memoria igual. El swap queda abierto: cambia la impl de esta seam y nada
/// más. Criterio de reapertura escrito en el ADR: &gt;5.000 ítems/vertical o p95 &gt;50ms.</para>
/// </remarks>
/// <typeparam name="TSummary">
/// El tipo de ítem del catálogo (<c>CatalogProductSummary</c>, <c>EventSummary</c>,
/// <c>PropertyListing</c>…). El motor no lo conoce: lee sus campos por el descriptor.
/// </typeparam>
public interface ICatalogIndex<TSummary>
{
    /// <summary>
    /// Aplica <paramref name="query"/> sobre <paramref name="items"/> y devuelve la página
    /// pedida + las facetas + el total. Nunca lanza por filtro vacío ni por catálogo vacío:
    /// devuelve <c>Items = []</c> y <c>Total = 0</c>. <b>Las facetas declaradas siguen
    /// llegando</b>, cada una con su lista de valores vacía — la UI necesita saber que la
    /// columna existe para pintar su título aunque no haya nada que contar.
    /// </summary>
    /// <remarks>
    /// Los ítems entran como parámetro (y no se leen del source aquí dentro) para que el
    /// motor sea una función PURA: mismos ítems + misma query = mismo resultado. Eso lo hace
    /// testeable sin montar un source y deja la política de caché en el llamador.
    /// </remarks>
    CatalogResult<TSummary> Search(IReadOnlyList<TSummary> items, CatalogQuery query);
}

/// <summary>
/// Los criterios de una búsqueda de catálogo. Todo opcional: vacío = el catálogo completo
/// en su orden por defecto. Calco de <c>DiscoveryCriteria</c> de la UI.
/// </summary>
/// <param name="Text">
/// Texto libre. Se compara ignorando mayúsculas y tildes, y por prefijo ("zapa" encuentra
/// "zapatos"). Los campos sobre los que busca y su peso los declara el descriptor.
/// </param>
/// <param name="Filters">
/// Facetas seleccionadas, <b>multi-valor por clave</b>: <c>{"brand": ["Aurora","Barista"]}</c>.
/// OR dentro de la misma clave (Aurora <b>o</b> Barista), AND entre claves distintas
/// (marca <b>y</b> categoría) — que es lo que un usuario espera al encender dos chips de la
/// misma columna. <b>La lista es la razón de ser de este record:</b> el
/// <c>IReadOnlyDictionary&lt;string,string&gt;</c> anterior no podía expresarlo y por eso
/// filtrar por dos marcas devolvía MENOS que filtrar por una.
/// </param>
/// <param name="Sort">Clave de orden declarada por el descriptor. Null = el orden por defecto del vertical.</param>
/// <param name="Skip">Ítems a saltar. Se capa a rango válido; negativo = 0.</param>
/// <param name="Take">Tamaño de página. Se capa a <c>CatalogSettings.MaxTake</c>.</param>
/// <remarks>
/// <b>Aquí NO hay <c>Scope</c>, y es deliberado.</b> Acotar por siteRoot es decidir QUÉ ÍTEMS
/// existen, y eso es trabajo de la <see cref="ICatalogSource{T}"/>. El motor es puro sobre
/// los ítems que le entregan: si además tuviera un <c>Scope</c>, habría dos sitios donde
/// acotar y un implementador podría creer que rellenar este basta — pasaría el scope aquí, el
/// motor lo ignoraría por no tener de dónde leer el siteRoot de un <c>T</c> cualquiera, y
/// <c>/api/shop/search</c> seguiría sirviendo apartamentos EN SILENCIO. Un campo que promete
/// aislamiento y no lo da es peor que no tenerlo.
/// </remarks>
public sealed record CatalogQuery(
    string? Text = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Filters = null,
    string? Sort = null,
    int Skip = 0,
    int Take = 24);

/// <summary>
/// El resultado de <see cref="ICatalogIndex{T}.Search"/>: la página + las facetas con
/// conteo + cuántos hay en total.
/// </summary>
/// <param name="Items">Los ítems de ESTA página (ya ordenados y paginados).</param>
/// <param name="Facets">
/// Las facetas derivadas con su conteo, para pintar los chips de refinamiento.
/// </param>
/// <param name="Total">
/// Cuántos ítems pasan el filtro <b>en total</b>, no cuántos trae esta página. Es el número
/// con el que la UI calcula el número de páginas: si aquí va <c>Items.Count</c>, el
/// paginador se cree que solo hay una página y se esconde.
/// </param>
public sealed record CatalogResult<TSummary>(
    IReadOnlyList<TSummary> Items,
    IReadOnlyList<CatalogFacet> Facets,
    int Total);

/// <summary>
/// Una faceta derivada del catálogo (categoría, marca, rating…) con sus valores y conteos.
/// </summary>
/// <param name="Field">El nombre canónico que la UI manda de vuelta en <see cref="CatalogQuery.Filters"/>.</param>
/// <param name="Label">Cómo se titula la columna de chips. Editor-facing.</param>
/// <param name="Kind">Cómo se comporta el filtro. Lo lee tanto el motor como la UI.</param>
/// <param name="Values">Los valores posibles con su conteo.</param>
public sealed record CatalogFacet(
    string Field,
    string Label,
    CatalogFacetKind Kind,
    IReadOnlyList<CatalogFacetValue> Values);

/// <summary>
/// Un valor de faceta + cuántos ítems lo tienen.
/// </summary>
/// <param name="Value">El valor canónico, el que viaja en la query.</param>
/// <param name="Label">
/// Cómo se le muestra al usuario. Separado de <see cref="Value"/> porque no siempre
/// coinciden: la faceta <c>minRating</c> tiene valor <c>"4.0"</c> y label
/// <c>"4 estrellas o más"</c>. Sin este campo los chips se pintan con el valor crudo.
/// </param>
/// <param name="Count">Cuántos ítems del universo filtrado tienen este valor.</param>
public sealed record CatalogFacetValue(string Value, string Label, int Count);

/// <summary>
/// Cómo se comporta una faceta al filtrar. El motor NO lo adivina por el nombre del campo:
/// lo declara el descriptor del vertical.
/// </summary>
public enum CatalogFacetKind
{
    /// <summary>
    /// Varios valores a la vez, OR entre ellos (marca = Aurora <b>o</b> Barista). El caso
    /// normal de una columna de chips.
    /// </summary>
    MultiSelect = 0,

    /// <summary>Un solo valor a la vez (categoría). Varios valores = OR igual, pero la UI pinta radios.</summary>
    SingleSelect = 1,

    /// <summary>
    /// Umbral: el valor es un mínimo, no una coincidencia exacta (<c>minRating=4.0</c> =
    /// "4 o más"). <b>Con varios valores gana el MENOS restrictivo</b>, no se descarta la
    /// faceta: encender "4+" y "3+" significa "3+". El código anterior hacía
    /// <c>double.TryParse("4.0,4.5")</c>, que falla, y el filtro moría EN SILENCIO con los
    /// dos chips encendidos.
    /// </summary>
    Threshold = 2,

    /// <summary>Rango numérico <c>min-max</c> (precio, área). El valor viaja como <c>"1000-5000"</c>.</summary>
    Range = 3,
}
