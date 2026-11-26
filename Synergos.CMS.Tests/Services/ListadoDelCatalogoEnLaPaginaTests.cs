using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Lecturas;
using Synergos.CMS.Web.Services.Lecturas;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El bloque «Listado del catálogo» (003-salud-cita, D-003-V2): «Equipo» de Salud lista los <c>professionalPage</c> de
/// verdad, por la MISMA lectura que el directorio del portal, y sin profesionales no pinta nada.
/// </summary>
public sealed class ListadoDelCatalogoEnLaPaginaTests
{
    private static readonly DayOfWeek[] Semana =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    private sealed class Fuente(params MedicalDoctor[] profesionales) : ICatalogSource<MedicalDoctor>
    {
        public Task<IReadOnlyList<MedicalDoctor>> GetAllAsync(string? scope = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MedicalDoctor>>(profesionales);
    }

    private sealed class FuenteCaida : ICatalogSource<MedicalDoctor>
    {
        public Task<IReadOnlyList<MedicalDoctor>> GetAllAsync(string? scope = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("caída");
    }

    private static MedicalDoctor Profesional(string id, string nombre, string especialidad)
        => new(id, nombre, especialidad, string.Empty, 0, 0, null, Semana, 8, 17, 30);

    private static ILecturaDelCatalogo[] Salud(ICatalogSource<MedicalDoctor> fuente)
        => [CatalogoDeSalud.Lectura(fuente, new CatalogSettings())];

    [Fact]
    public async Task Lista_los_profesionales_de_Salud_con_su_especialidad_y_sus_iniciales()
    {
        var lecturas = Salud(new Fuente(
            Profesional("laura-mendez", "Dra. Laura Méndez", "Medicina general"),
            Profesional("andres-villa", "Dr. Andrés Villa", "Cardiología")));

        var tarjetas = await ListadoDelCatalogoEnLaPagina.LeerAsync(lecturas, "salud", "especialidad", 12);

        Assert.Equal(
            new (string, string?, string)[] { ("Dr. Andrés Villa", "Cardiología", "AV"), ("Dra. Laura Méndez", "Medicina general", "LM") },
            tarjetas.Select(t => (t.Titulo, t.Subtitulo, t.Iniciales)));
        Assert.All(tarjetas, t => Assert.Null(t.Imagen));
    }

    [Fact]
    public async Task Sin_profesionales_un_vertical_desconocido_o_una_lectura_caida_no_pinta_nada()
    {
        Assert.Empty(await ListadoDelCatalogoEnLaPagina.LeerAsync(Salud(new Fuente()), "salud", "especialidad", 12));
        Assert.Empty(await ListadoDelCatalogoEnLaPagina.LeerAsync(Salud(new Fuente(Profesional("a", "Dra. A", "B"))), "tienda", null, 12));
        Assert.Empty(await ListadoDelCatalogoEnLaPagina.LeerAsync(Salud(new Fuente(Profesional("a", "Dra. A", "B"))), null, null, 12));
        Assert.Empty(await ListadoDelCatalogoEnLaPagina.LeerAsync(Salud(new FuenteCaida()), "salud", "especialidad", 12));
    }

    [Fact]
    public async Task El_maximo_corta_el_listado_y_sin_el_dato_pedido_no_hay_subtitulo()
    {
        var lecturas = Salud(new Fuente(
            Profesional("a", "Dra. Ana Uno", "Pediatría"),
            Profesional("b", "Dr. Beto Dos", "Pediatría"),
            Profesional("c", "Dra. Carla Tres", "Pediatría")));

        var tarjetas = await ListadoDelCatalogoEnLaPagina.LeerAsync(lecturas, "salud", "noexiste", 2);

        Assert.Equal(2, tarjetas.Count);
        Assert.All(tarjetas, t => Assert.Null(t.Subtitulo));
    }

    [Theory]
    [InlineData(null, ListadoDelCatalogoEnLaPagina.MaximoPorDefecto)]
    [InlineData("", ListadoDelCatalogoEnLaPagina.MaximoPorDefecto)]
    [InlineData("0", ListadoDelCatalogoEnLaPagina.MaximoPorDefecto)]
    [InlineData("3", 3)]
    [InlineData("500", ListadoDelCatalogoEnLaPagina.MaximoPermitido)]
    public void El_maximo_que_escribe_el_editor_queda_entre_uno_y_el_tope(string? escrito, int esperado)
        => Assert.Equal(esperado, ListadoDelCatalogoEnLaPagina.Maximo(escrito));

    [Theory]
    [InlineData("Dra. Laura Méndez", "LM")]
    [InlineData("Dr. Andrés Villa Gómez", "AV")]
    [InlineData("sofía", "S")]
    [InlineData("Dr.", "")]
    public void Las_iniciales_saltan_el_titulo(string nombre, string esperadas)
        => Assert.Equal(esperadas, ListadoDelCatalogoEnLaPagina.Iniciales(nombre));
}
