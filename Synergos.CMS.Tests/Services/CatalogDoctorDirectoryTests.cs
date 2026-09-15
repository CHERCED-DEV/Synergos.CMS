using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre el directorio de profesionales servido desde el CONTENIDO que autoró el editor (#118),
/// que es lo que se registra cuando <c>Synergos:Catalog:Sources:Salud = cms</c>.
/// </summary>
/// <remarks>
/// <para>La propiedad protegida es de regresión silenciosa: <b>el directorio se comporta igual
/// vengan los profesionales de donde vengan</b>. Si el orden o el filtro por especialidad
/// cambiaran al mover una línea de configuración, el buscador de médicos devolvería otra cosa
/// sin que nada fallara — y sólo lo vería quien comparara las dos listas.</para>
///
/// <para><b>El fixture llega desordenado y con dos especialidades que comparten prefijo</b>: con
/// una lista ya ordenada, quitar el <c>OrderBy</c> pasaría en verde, y con una sola especialidad
/// el filtro daría lo mismo filtre o no.</para>
/// </remarks>
public sealed class CatalogDoctorDirectoryTests
{
    private sealed class FakeSource : ICatalogSource<MedicalDoctor>
    {
        private readonly IReadOnlyList<MedicalDoctor> _items;

        public FakeSource(params MedicalDoctor[] items) => _items = items;

        public Task<IReadOnlyList<MedicalDoctor>> GetAllAsync(
            string? scope = null, CancellationToken cancellationToken = default)
            => Task.FromResult(_items);
    }

    private static MedicalDoctor Doctor(
        string id,
        string name,
        string specialty = "Medicina Interna",
        bool? acceptingPatients = null,
        string phone = "",
        string email = "")
        => new(
            Id: id,
            FullName: name,
            Specialty: specialty,
            LicenseNumber: "RM-48213",
            Rating: 0d,
            YearsExperience: 14,
            AvatarUrl: null,
            WorkingDays: new[] { DayOfWeek.Monday },
            SlotStartHour: 8,
            SlotEndHour: 16,
            SlotMinutes: 30,
            Phone: phone,
            Email: email,
            AcceptingPatients: acceptingPatients);

    private static CatalogDoctorDirectory Directorio(params MedicalDoctor[] items)
        => new(new FakeSource(items));

    [Fact]
    public async Task Un_directorio_vacio_devuelve_vacio_y_no_revienta()
    {
        Assert.Empty(await Directorio().ListAsync());
        Assert.Null(await Directorio().GetAsync("ana-rios"));
    }

    [Fact]
    public async Task Lista_por_nombre_y_no_por_el_orden_del_arbol()
    {
        var lista = await Directorio(
            Doctor("carlos-mejia", "Dr. Carlos Mejía"),
            Doctor("ana-rios", "Dra. Ana Ríos"),
            Doctor("laura-vega", "Dra. Laura Vega")).ListAsync();

        Assert.Equal(
            new[] { "Dr. Carlos Mejía", "Dra. Ana Ríos", "Dra. Laura Vega" },
            lista.Select(d => d.FullName));
    }

    [Fact]
    public async Task Filtra_por_especialidad_igual_que_el_staff_sembrado()
    {
        // "Medicina" es prefijo de una y subcadena de ninguna otra: si el filtro se perdiera,
        // volverían las tres.
        var lista = await Directorio(
            Doctor("ana-rios", "Dra. Ana Ríos", "Medicina Interna"),
            Doctor("carlos-mejia", "Dr. Carlos Mejía", "Cardiología"),
            Doctor("laura-vega", "Dra. Laura Vega", "Pediatría")).ListAsync("medicina");

        Assert.Equal(new[] { "ana-rios" }, lista.Select(d => d.Id));
    }

    [Fact]
    public async Task El_slug_es_la_llave_y_se_compara_exacto()
    {
        // Para Api.Booking el slug ES el subjectId. Dos que sólo se diferencian en mayúsculas
        // son dos sujetos distintos, así que resolver «Ana-Rios» como «ana-rios» agendaría
        // sobre el recurso de otro.
        var directorio = Directorio(Doctor("ana-rios", "Dra. Ana Ríos"));

        Assert.NotNull(await directorio.GetAsync("ana-rios"));
        Assert.Null(await directorio.GetAsync("Ana-Rios"));
        Assert.Null(await directorio.GetAsync("   "));
    }

    [Fact]
    public async Task El_contacto_y_la_lista_abierta_viajan_tal_como_se_autoraron()
    {
        // El caso que ningún default produce: `false` sólo puede venir de que alguien lo
        // eligiera —`null` es lo que sale solo y `true` es lo que repone un normalizador
        // defensivo—. Con todos en `null` el directorio daría lo mismo aunque no leyera el
        // campo.
        var lista = await Directorio(
            Doctor("ana-rios", "Dra. Ana Ríos", acceptingPatients: true,
                phone: "+57 604 448 0901", email: "ana.rios@example.co"),
            Doctor("carlos-mejia", "Dr. Carlos Mejía", acceptingPatients: false),
            Doctor("laura-vega", "Dra. Laura Vega")).ListAsync();

        var porId = lista.ToDictionary(d => d.Id, StringComparer.Ordinal);

        Assert.True(porId["ana-rios"].AcceptingPatients);
        Assert.Equal("+57 604 448 0901", porId["ana-rios"].Phone);
        Assert.Equal("ana.rios@example.co", porId["ana-rios"].Email);
        Assert.False(porId["carlos-mejia"].AcceptingPatients);
        Assert.Null(porId["laura-vega"].AcceptingPatients);
    }
}
