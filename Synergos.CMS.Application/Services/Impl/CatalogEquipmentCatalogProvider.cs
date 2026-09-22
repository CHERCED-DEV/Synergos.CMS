using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El catálogo de equipos servido desde el CONTENIDO del CMS, por
/// <c>ICatalogSource&lt;RentalEquipment&gt;</c>.
/// </summary>
/// <remarks>
/// Gemelo de <c>CatalogDoctorDirectory</c> (#118): la fuente sabe leer Umbraco y esta clase no —
/// sólo ordena y filtra, que es lo que el seam promete. Lo elige
/// <c>Synergos:Catalog:Sources:Alquiler = cms</c>.
/// </remarks>
public sealed class CatalogEquipmentCatalogProvider : IEquipmentCatalogProvider
{
    private readonly ICatalogSource<RentalEquipment> _source;

    /// <summary>Construye el proveedor.</summary>
    /// <param name="source">De dónde salen los equipos autorados.</param>
    public CatalogEquipmentCatalogProvider(ICatalogSource<RentalEquipment> source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RentalEquipment>> ListAsync(
        string? category = null, CancellationToken cancellationToken = default)
    {
        var todos = await _source.GetAllAsync(null, cancellationToken).ConfigureAwait(false);
        IEnumerable<RentalEquipment> q = todos.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(category))
        {
            var c = category.Trim();
            q = q.Where(e => e.Category.Contains(c, StringComparison.OrdinalIgnoreCase));
        }

        return q.ToList();
    }

    /// <inheritdoc />
    public async Task<RentalEquipment?> GetAsync(
        string equipmentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(equipmentId))
        {
            return null;
        }

        var todos = await _source.GetAllAsync(null, cancellationToken).ConfigureAwait(false);
        return todos.FirstOrDefault(e => string.Equals(e.Id, equipmentId, StringComparison.Ordinal));
    }
}
