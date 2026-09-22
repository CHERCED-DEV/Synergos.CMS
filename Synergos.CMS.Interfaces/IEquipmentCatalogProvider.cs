namespace Synergos.CMS.Interfaces;

/// <summary>
/// De dónde salen los equipos que se muestran: el EJE 1 de Alquiler.
/// </summary>
/// <remarks>
/// Lo que hay detrás lo decide <c>Synergos:Catalog:Sources:Alquiler</c> — el seed de demo por
/// defecto, o los <c>equipmentPage</c> que el editor autoró. Esta costura **no sale a la red** y
/// no debe: el catálogo ya tiene dueño en el árbol de contenido, y meterle una ida a
/// <c>Api.Catalog</c> sería el retroceso que `a_vertical_is_three_axes_and_only_one_crosses`
/// describe.
/// </remarks>
public interface IEquipmentCatalogProvider
{
    /// <summary>Los equipos publicados, opcionalmente filtrados por categoría.</summary>
    /// <param name="category">Familia por la que filtrar; vacío o null los trae todos.</param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns>Los equipos, ordenados por nombre.</returns>
    Task<IReadOnlyList<RentalEquipment>> ListAsync(
        string? category = null, CancellationToken cancellationToken = default);

    /// <summary>Un equipo por su slug, o null si no existe.</summary>
    /// <param name="equipmentId">El slug que escribió el editor.</param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns>El equipo, o null.</returns>
    Task<RentalEquipment?> GetAsync(string equipmentId, CancellationToken cancellationToken = default);
}
