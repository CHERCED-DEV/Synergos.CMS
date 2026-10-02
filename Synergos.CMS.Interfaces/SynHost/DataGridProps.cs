namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-data-grid&gt;</c>: un listado cuyas filas arma el servidor.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (#196, tanda D).</b> El editor escribía un <c>dataSource</c>
/// (<c>/api/search?type=curso</c>…) y un <c>columnsJson</c> que el elemento no leía: no consultaba
/// nada y pintaba «0 propiedades · No hay resultados que coincidan con los filtros» en cursos,
/// servicios, agenda y el listado de inmuebles. Una negación sin consulta detrás. Y la URL tampoco
/// habría servido: la API ignora <c>type</c> (su parámetro es <c>docType</c>) y sin <c>q</c> devuelve
/// cero.</para>
///
/// <para><b>Las filas las arma el servidor</b> (decisión del arquitecto, como <c>seat-map</c>): el
/// editor elige la FUENTE —las fichas de su sección, o el catálogo de cursos, de eventos o de
/// inmuebles, los mismos que leen las apps— y el resolver la consulta, con el <c>?q</c> de la
/// petición si lo hay. Sin endpoint en el cliente, el listado se ve sin JavaScript y lo indexa el
/// buscador. Los datos de cada fila llegan formateados en la cultura de la petición.</para>
///
/// <para><b>Sección <c>DataGrid</c></b> (ADR 0136): los textos del listado —el conteo, el vacío,
/// el botón— y los rótulos de los datos de cada fila.</para>
/// </remarks>
[ElementoSynHost("data-grid", TipoDeColocable.Pieza, Diccionario = ["DataGrid"], SelectoresDeDatos = ["fuente"])]
public sealed record DataGridProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] IReadOnlyList<FilaDelListado>? Rows);

/// <summary>Una fila: lo que la tarjeta muestra y adónde lleva, si la fuente tiene página.</summary>
public sealed record FilaDelListado(
    string Id,
    string Title,
    string? Href = null,
    string? Image = null,
    string? ImageAlt = null,
    string? Badge = null,
    IReadOnlyList<DatoDeLaFila>? Specs = null);

/// <summary>Un dato de la fila, con su rótulo y su valor ya formateado.</summary>
public sealed record DatoDeLaFila(string Label, string Value);
