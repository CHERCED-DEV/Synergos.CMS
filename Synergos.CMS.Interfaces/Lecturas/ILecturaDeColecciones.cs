namespace Synergos.CMS.Interfaces.Lecturas;

/// <summary>
/// Una colección del miembro (la lista de deseos, los favoritos, los guardados) con sus elementos hidratados del
/// catálogo del vertical (014-despiece-tienda, I4). La colección es de <see cref="IUserCollection"/>, que ya era común;
/// lo del vertical es cómo se lee cada elemento.
/// </summary>
public interface ILecturaDeColecciones : ILecturaDeUnVertical
{
    /// <summary>
    /// La colección <paramref name="coleccion"/> del miembro <paramref name="correo"/> (de la sesión), en su orden.
    /// </summary>
    Task<ColeccionDelMiembro> ColeccionAsync(
        string correo,
        string coleccion,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Algo guardado en la colección. <see cref="Elemento"/> es nulo si el catálogo ya no lo tiene: se muestra que estaba,
/// no se inventa.
/// </summary>
public sealed record ElementoGuardado(string Id, DateTimeOffset Guardado, ElementoDelCatalogo? Elemento);

/// <summary>Una colección del miembro.</summary>
public sealed record ColeccionDelMiembro(string Coleccion, IReadOnlyList<ElementoGuardado> Elementos);
