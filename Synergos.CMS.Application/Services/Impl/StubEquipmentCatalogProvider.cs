using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El catálogo de equipos SEMBRADO: lo que se sirve mientras nadie autoró un
/// <c>equipmentPage</c>.
/// </summary>
/// <remarks>
/// Es el default de <c>Synergos:Catalog:Sources:Alquiler</c>, para que el repo se levante entero
/// sin contenido. Con <c>= cms</c> lo reemplaza
/// <see cref="CatalogEquipmentCatalogProvider"/> — forma A del doc 13 §5.bis.
/// </remarks>
public sealed class StubEquipmentCatalogProvider : IEquipmentCatalogProvider
{
    private static readonly IReadOnlyList<RentalEquipment> Semilla = new[]
    {
        new RentalEquipment(
            Id: "andamio-multidireccional-2m",
            Name: "Andamio multidireccional 2 m",
            Category: "Andamios",
            Summary: "Cuerpo de andamio certificado para trabajo en altura hasta 2 metros.",
            Description: "Estructura modular de acero galvanizado con plataforma antideslizante "
                + "y barandas. Se entrega armada y se recoge desarmada.",
            CoverUrl: null,
            GalleryUrls: Array.Empty<string>(),
            Units: 12,
            DailyRate: 38_000m,
            Deposit: 250_000m,
            MinDays: 1,
            MaxDays: 30,
            Includes: new[] { "Plataforma", "Dos barandas", "Rodachines con freno" },
            Requirements: new[] { "Arnés propio", "Cédula del responsable" },
            Rates: new[]
            {
                new EquipmentRate("semana", "Semana completa", 7, 30_000m, "A partir de 7 días."),
                new EquipmentRate("mes", "Mes", 28, 24_000m, "A partir de 28 días."),
            },
            Specs: new[]
            {
                new EquipmentSpec("Altura útil", "2,0 m"),
                new EquipmentSpec("Carga máxima", "250 kg"),
            }),
        new RentalEquipment(
            Id: "planta-electrica-5kva",
            Name: "Planta eléctrica 5 kVA",
            Category: "Energía",
            Summary: "Generador a gasolina para obra o evento, arranque manual.",
            Description: "Planta monofásica de 5 kVA con tanque de 25 litros y autonomía "
                + "aproximada de ocho horas al 60 % de carga.",
            CoverUrl: null,
            GalleryUrls: Array.Empty<string>(),
            Units: 3,
            DailyRate: 145_000m,
            Deposit: 900_000m,
            MinDays: 1,
            MaxDays: 15,
            Includes: new[] { "Cable de 10 m", "Embudo de carga" },
            Requirements: new[] { "Devolver con el tanque lleno" },
            Rates: new[] { new EquipmentRate("semana", "Semana completa", 7, 115_000m, string.Empty) },
            Specs: new[]
            {
                new EquipmentSpec("Potencia", "5 kVA"),
                new EquipmentSpec("Combustible", "Gasolina corriente"),
                new EquipmentSpec("Peso", "78 kg"),
            }),
        new RentalEquipment(
            Id: "kit-sonido-500w",
            Name: "Kit de sonido 500 W",
            Category: "Audiovisual",
            Summary: "Dos cabinas activas, consola de cuatro canales y dos micrófonos.",
            Description: "Equipo para reuniones y eventos de hasta 150 personas en interior.",
            CoverUrl: null,
            GalleryUrls: Array.Empty<string>(),
            Units: 5,
            DailyRate: 220_000m,
            Deposit: 600_000m,
            MinDays: 1,
            MaxDays: 10,
            Includes: new[] { "Dos trípodes", "Cableado", "Dos micrófonos alámbricos" },
            Requirements: Array.Empty<string>(),
            Rates: Array.Empty<EquipmentRate>(),
            Specs: new[] { new EquipmentSpec("Potencia total", "500 W") }),
    };

    /// <inheritdoc />
    public Task<IReadOnlyList<RentalEquipment>> ListAsync(
        string? category = null, CancellationToken cancellationToken = default)
    {
        IEnumerable<RentalEquipment> q = Semilla;
        if (!string.IsNullOrWhiteSpace(category))
        {
            var c = category.Trim();
            q = q.Where(e => e.Category.Contains(c, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult<IReadOnlyList<RentalEquipment>>(
            q.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <inheritdoc />
    public Task<RentalEquipment?> GetAsync(string equipmentId, CancellationToken cancellationToken = default)
        => Task.FromResult(string.IsNullOrWhiteSpace(equipmentId)
            ? null
            // Ordinal: el slug es un identificador y viaja como subjectId hasta Api.Booking,
            // que no normaliza mayúsculas.
            : Semilla.FirstOrDefault(e => string.Equals(e.Id, equipmentId, StringComparison.Ordinal)));
}
