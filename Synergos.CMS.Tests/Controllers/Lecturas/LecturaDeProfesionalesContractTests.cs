using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using static Synergos.CMS.Tests.Controllers.Lecturas.LecturasDePrueba;

namespace Synergos.CMS.Tests.Controllers.Lecturas;

/// <summary>
/// Los profesionales de Salud en el catálogo genérico (003-salud-cita, I1 de producción, T041): lo que lee el directorio
/// de <c>ehr</c> en <c>api/lecturas/salud/catalogo</c>.
/// </summary>
public sealed class LecturaDeProfesionalesContractTests
{
    private static LecturaDelCatalogoController Controlador(string? query = null, ICatalogSource<MedicalDoctor>? fuente = null)
    {
        var controlador = new LecturaDelCatalogoController(
            new[] { CatalogoDeSalud.Lectura(fuente ?? new SaludDemoDirectorySource(), new CatalogSettings()) });
        var http = new DefaultHttpContext();
        if (query is not null)
        {
            http.Request.QueryString = new QueryString(query);
        }

        controlador.ControllerContext = new ControllerContext { HttpContext = http };
        return controlador;
    }

    [Fact]
    public async Task El_listado_emite_cada_profesional_con_su_especialidad_su_agenda_y_la_faceta()
    {
        var listado = Wire(await Controlador().Listado("salud", null, null, null, null, default));

        var ana = listado.GetProperty("elementos").EnumerateArray().Single(e => e.GetProperty("id").GetString() == "doc-ana-rios");
        Assert.Equal("Dra. Ana Ríos", ana.GetProperty("titulo").GetString());
        TieneLasClaves(ana.GetProperty("atributos"), "especialidad", "dias", "desde", "hasta", "minutos", "registro", "experiencia");
        Assert.Equal("Medicina Interna", ana.GetProperty("atributos").GetProperty("especialidad").GetString());
        Assert.Equal("1,2,3,4,5", ana.GetProperty("atributos").GetProperty("dias").GetString());
        Assert.Equal("8", ana.GetProperty("atributos").GetProperty("desde").GetString());
        Assert.Equal("16", ana.GetProperty("atributos").GetProperty("hasta").GetString());
        Assert.Equal("30", ana.GetProperty("atributos").GetProperty("minutos").GetString());
        var faceta = listado.GetProperty("facetas").EnumerateArray().Single(f => f.GetProperty("campo").GetString() == "especialidad");
        Assert.Contains(faceta.GetProperty("valores").EnumerateArray(), v => v.GetProperty("valor").GetString() == "Cardiología");
    }

    /// <summary>
    /// Ni precio ni estrellas: la consulta se cotiza en la cita, y el promedio que trae la muestra no sale de ninguna
    /// reseña. Lo que no consta no viaja como un «no».
    /// </summary>
    [Fact]
    public async Task Sin_precio_sin_calificacion_y_lo_que_no_consta_no_viaja()
    {
        var listado = Wire(await Controlador().Listado("salud", null, null, null, null, default));

        Assert.All(listado.GetProperty("elementos").EnumerateArray(), e =>
        {
            Assert.Equal(JsonValueKind.Null, e.GetProperty("precio").ValueKind);
            Assert.Equal(JsonValueKind.Null, e.GetProperty("calificacion").ValueKind);
            Assert.False(e.GetProperty("atributos").TryGetProperty("aceptaPacientes", out _));
        });
    }

    [Fact]
    public async Task Admite_pacientes_viaja_solo_cuando_el_editor_lo_dijo()
    {
        var fuente = new FuenteFija(
            Profesional("doc-si", "Dra. Sí", acepta: true),
            Profesional("doc-no", "Dr. No", acepta: false),
            Profesional("doc-nc", "Dr. No Consta", acepta: null));

        var porId = Wire(await Controlador(fuente: fuente).Listado("salud", null, null, null, null, default))
            .GetProperty("elementos").EnumerateArray().ToDictionary(e => e.GetProperty("id").GetString()!);

        Assert.Equal("si", porId["doc-si"].GetProperty("atributos").GetProperty("aceptaPacientes").GetString());
        Assert.Equal("no", porId["doc-no"].GetProperty("atributos").GetProperty("aceptaPacientes").GetString());
        Assert.False(porId["doc-nc"].GetProperty("atributos").TryGetProperty("aceptaPacientes", out _));
    }

    /// <summary>La lectura genérica sirve lo que sirve el directorio: los mismos profesionales, en el mismo orden.</summary>
    [Fact]
    public async Task Da_los_mismos_profesionales_en_el_mismo_orden_que_el_directorio()
    {
        var generico = Wire(await Controlador().Listado("salud", null, null, null, 50, default));
        var directorio = await new StubDoctorDirectory().ListAsync();

        Assert.Equal(
            directorio.Select(d => d.Id),
            generico.GetProperty("elementos").EnumerateArray().Select(e => e.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Se_refina_por_especialidad_y_se_busca_por_nombre()
    {
        var porEspecialidad = Wire(await Controlador("?filtro.especialidad=Cardiolog%C3%ADa").Listado("salud", null, null, null, null, default));
        Assert.Equal(new[] { "doc-carlos-mejia" }, porEspecialidad.GetProperty("elementos").EnumerateArray().Select(e => e.GetProperty("id").GetString()));

        var porNombre = Wire(await Controlador().Listado("salud", "Ríos", null, null, null, default));
        Assert.Equal("doc-ana-rios", porNombre.GetProperty("elementos")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task La_ficha_trae_el_profesional_y_su_foto_y_uno_que_no_existe_es_404()
    {
        var ficha = Wire(await Controlador().Ficha("salud", "doc-ana-rios", default));

        Assert.Equal("doc-ana-rios", ficha.GetProperty("elemento").GetProperty("id").GetString());
        Assert.Equal("/media/ehr/doctors/ana-rios.webp", ficha.GetProperty("imagenes")[0].GetString());
        Assert.Equal(0, ficha.GetProperty("opciones").GetArrayLength());
        Assert.Equal(404, Estado(await Controlador().Ficha("salud", "doc-no-existe", default)));
    }

    private static MedicalDoctor Profesional(string id, string nombre, bool? acepta) => new(
        Id: id, FullName: nombre, Specialty: "Pediatría", LicenseNumber: "RM-1", Rating: 0, YearsExperience: 0,
        AvatarUrl: null, WorkingDays: [DayOfWeek.Monday], SlotStartHour: 8, SlotEndHour: 12, SlotMinutes: 20,
        AcceptingPatients: acepta);

    private sealed class FuenteFija(params MedicalDoctor[] profesionales) : ICatalogSource<MedicalDoctor>
    {
        public Task<IReadOnlyList<MedicalDoctor>> GetAllAsync(string? scope = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MedicalDoctor>>(profesionales);
    }
}
