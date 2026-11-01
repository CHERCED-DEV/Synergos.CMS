namespace Synergos.CMS.Interfaces.Lecturas;

/// <summary>
/// Las reseñas de un elemento del catálogo, con forma neutra de vertical (014-despiece-tienda, I4): las que el catálogo
/// del vertical le da al elemento, cuáles escribió alguien que lo compró, y si quien lee puede escribir una.
/// </summary>
public interface ILecturaDeResenas : ILecturaDeUnVertical
{
    /// <summary>
    /// Las reseñas del elemento, o <c>null</c> si no existe. <paramref name="miembro"/> es quien lee, de la sesión;
    /// sin sesión no puede reseñar.
    /// </summary>
    Task<ResenasDelElemento?> DelElementoAsync(string id, Guid? miembro, CancellationToken cancellationToken = default);
}

/// <summary>Una reseña. <see cref="Verificada"/>: la escribió alguien que compró el elemento.</summary>
public sealed record ResenaDelElemento(
    string Autor,
    int Calificacion,
    string Titulo,
    string Texto,
    DateOnly Fecha,
    bool Verificada);

/// <summary>Las reseñas de un elemento, su resumen y si quien lee puede reseñarlo.</summary>
public sealed record ResenasDelElemento(
    string Id,
    Calificacion? Resumen,
    IReadOnlyList<ResenaDelElemento> Resenas,
    bool PuedeResenar);
