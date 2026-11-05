using System.Globalization;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Lecturas;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El directorio de profesionales de Salud en las lecturas genéricas (003-salud-cita, I1 de producción): su clave de
/// vertical y sus profesionales como catálogo neutro, sobre la MISMA fuente que el directorio
/// (<c>Synergos:Catalog:Sources:Salud</c>: el contenido <c>professionalPage</c> o la muestra de desarrollo).
/// </summary>
/// <remarks>
/// <para><b>Sin calificación</b>: el promedio del profesional no sale de ninguna reseña (la muestra lo trae escrito y
/// el contenido no lo tiene), y una estrella sin reseñas detrás es un dato inventado.</para>
///
/// <para>La agenda que declara el profesional (días, horas y minutos por franja) viaja en los atributos para quien
/// la quiera pintar; las franjas LIBRES son de Booking.</para>
/// </remarks>
public static class CatalogoDeSalud
{
    /// <summary>La clave de Salud en <c>api/lecturas/{vertical}</c>.</summary>
    public const string Vertical = "salud";

    /// <summary>El campo de la faceta de especialidad (el de <c>?filtro.especialidad=</c>).</summary>
    public const string CampoEspecialidad = "especialidad";

    /// <summary>Los profesionales de Salud como lectura genérica.</summary>
    public static ILecturaDelCatalogo Lectura(ICatalogSource<MedicalDoctor> fuente, CatalogSettings ajustes)
    {
        ArgumentNullException.ThrowIfNull(ajustes);
        return new LecturaDelCatalogo<MedicalDoctor>(
            Vertical,
            fuente,
            new InMemoryCatalogIndex<MedicalDoctor>(Descriptor, ajustes),
            Proyeccion,
            ajustes.MaxTake);
    }

    /// <summary>
    /// Lo que Salud declara de su directorio: se busca por nombre y especialidad, se refina por especialidad y se
    /// ordena por nombre, como <see cref="StubDoctorDirectory"/> y <see cref="CatalogDoctorDirectory"/>.
    /// </summary>
    internal static CatalogDescriptor<MedicalDoctor> Descriptor { get; } = new(
        idOf: d => d.Id,
        searchFields: new[]
        {
            new CatalogSearchField<MedicalDoctor>(5, d => d.FullName),
            new CatalogSearchField<MedicalDoctor>(3, d => d.Specialty),
        },
        defaultOrder: doctores => doctores.OrderBy(d => d.FullName, StringComparer.OrdinalIgnoreCase),
        filters: new CatalogFilter<MedicalDoctor>[]
        {
            new CatalogTermFilter<MedicalDoctor>(CampoEspecialidad, "Especialidad", d => new[] { d.Specialty }),
        });

    /// <summary>Un profesional en la forma neutra: la tarjeta con su especialidad y su agenda, y la ficha.</summary>
    internal static ProyeccionDelCatalogo<MedicalDoctor> Proyeccion { get; } = new(
        Id: d => d.Id,
        Elemento: Elemento,
        Ficha: d => new FichaDelCatalogo(
            Elemento: Elemento(d),
            Descripcion: string.Empty,
            Imagenes: string.IsNullOrWhiteSpace(d.AvatarUrl) ? [] : [d.AvatarUrl],
            Opciones: [],
            Preguntas: []));

    private static ElementoDelCatalogo Elemento(MedicalDoctor d)
    {
        var atributos = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CampoEspecialidad] = d.Specialty,
            ["dias"] = string.Join(',', d.WorkingDays.Select(dia => ((int)dia).ToString(CultureInfo.InvariantCulture))),
            ["desde"] = d.SlotStartHour.ToString(CultureInfo.InvariantCulture),
            ["hasta"] = d.SlotEndHour.ToString(CultureInfo.InvariantCulture),
            ["minutos"] = d.SlotMinutes.ToString(CultureInfo.InvariantCulture),
        };
        Poner(atributos, "registro", d.LicenseNumber);
        Poner(atributos, "telefono", d.Phone);
        Poner(atributos, "correo", d.Email);
        if (d.YearsExperience > 0)
        {
            atributos["experiencia"] = d.YearsExperience.ToString(CultureInfo.InvariantCulture);
        }

        // «No consta» no viaja: un «no» por defecto le cerraría la lista a quien sí recibe (#111).
        if (d.AcceptingPatients is { } acepta)
        {
            atributos["aceptaPacientes"] = acepta ? "si" : "no";
        }

        return new ElementoDelCatalogo(
            Id: d.Id,
            Titulo: d.FullName,
            Imagen: string.IsNullOrWhiteSpace(d.AvatarUrl) ? null : d.AvatarUrl,
            Precio: null,
            PrecioAntes: null,
            Calificacion: null,
            Disponibles: null,
            Atributos: atributos);
    }

    private static void Poner(Dictionary<string, string> atributos, string clave, string? valor)
    {
        if (!string.IsNullOrWhiteSpace(valor))
        {
            atributos[clave] = valor.Trim();
        }
    }
}

/// <summary>Los profesionales de la muestra de desarrollo (<see cref="EhrDemoSeed"/>), como fuente del catálogo.</summary>
public sealed class SaludDemoDirectorySource : ICatalogSource<MedicalDoctor>
{
    public Task<IReadOnlyList<MedicalDoctor>> GetAllAsync(string? scope = null, CancellationToken cancellationToken = default)
        => Task.FromResult(EhrDemoSeed.Doctors());
}
