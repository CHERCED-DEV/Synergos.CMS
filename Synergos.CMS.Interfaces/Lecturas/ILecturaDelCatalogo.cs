namespace Synergos.CMS.Interfaces.Lecturas;

/// <summary>
/// El listado facetado y la ficha de un catálogo, con forma neutra de vertical (014-despiece-tienda, I4): un producto,
/// un evento o un viaje son el mismo <see cref="ElementoDelCatalogo"/>. Va sobre el motor del catálogo
/// (<see cref="ICatalogSource{T}"/> + <see cref="ICatalogIndex{T}"/>), que ya era neutro; lo propio del vertical es la
/// fuente y la proyección.
/// </summary>
public interface ILecturaDelCatalogo : ILecturaDeUnVertical
{
    /// <summary>Una página del listado, con las facetas para refinarlo.</summary>
    Task<ListadoDelCatalogo> ListarAsync(ConsultaDelCatalogo consulta, CancellationToken cancellationToken = default);

    /// <summary>La ficha de un elemento, o <c>null</c> si no existe.</summary>
    Task<FichaDelCatalogo?> FichaAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Los elementos con esos ids que existen, en el orden pedido (para hidratar una colección).</summary>
    Task<IReadOnlyList<ElementoDelCatalogo>> ElementosAsync(
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Lo que se pide al listado. <see cref="Filtros"/> va por el campo de cada faceta (el mismo <c>campo</c> que trae el
/// listado), con sus valores.
/// </summary>
public sealed record ConsultaDelCatalogo(
    string? Texto,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Filtros,
    string? Orden,
    int Pagina,
    int PorPagina);

/// <summary>
/// Un elemento del catálogo. <see cref="Atributos"/> lleva lo que el vertical quiere mostrar con su clave
/// (la tienda: <c>marca</c> y <c>categoria</c>; un evento: <c>fecha</c> y <c>lugar</c>).
/// </summary>
public sealed record ElementoDelCatalogo(
    string Id,
    string Titulo,
    string? Imagen,
    Precio? Precio,
    Calificacion? Calificacion,
    int? Disponibles,
    IReadOnlyDictionary<string, string> Atributos);

/// <summary>Un valor de una faceta, con cuántos elementos lo tienen.</summary>
public sealed record ValorDeLaFaceta(string Valor, string Etiqueta, int Cantidad);

/// <summary>
/// Una faceta del listado. <see cref="Tipo"/> es el del motor (<c>MultiSelect</c>, <c>SingleSelect</c>,
/// <c>Threshold</c>, <c>Range</c>).
/// </summary>
public sealed record FacetaDelListado(string Campo, string Titulo, string Tipo, IReadOnlyList<ValorDeLaFaceta> Valores);

/// <summary>Una página del listado.</summary>
public sealed record ListadoDelCatalogo(
    IReadOnlyList<ElementoDelCatalogo> Elementos,
    IReadOnlyList<FacetaDelListado> Facetas,
    int Total,
    int Pagina,
    int PorPagina);

/// <summary>Una opción del elemento (la variante de un producto, la localidad de un evento).</summary>
public sealed record OpcionDelElemento(string Id, string Titulo, Precio? Precio, int? Disponibles);

/// <summary>Una pregunta sobre el elemento, con su respuesta si la tiene.</summary>
public sealed record PreguntaDelElemento(string Autor, string Pregunta, string? Respuesta, DateOnly Fecha);

/// <summary>La ficha de un elemento. Las reseñas son otra lectura (<see cref="ILecturaDeResenas"/>).</summary>
public sealed record FichaDelCatalogo(
    ElementoDelCatalogo Elemento,
    string Descripcion,
    IReadOnlyList<string> Imagenes,
    IReadOnlyList<OpcionDelElemento> Opciones,
    IReadOnlyList<PreguntaDelElemento> Preguntas);
