using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.Listados;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Los listados que arma el servidor (#196, tanda D): antes la grilla no consultaba nada y decía
/// «No hay resultados que coincidan con los filtros».
/// </summary>
public sealed class ListadosTests
{
    private static readonly CultureInfo EsCo = new("es-CO");
    private static readonly TimeZoneInfo Bogota = new ListadosSettings().Zona()!;

    private static PeticionDelListado Peticion(string? consulta = null)
        => new(consulta, EsCo, Bogota, (_, respaldo) => respaldo);

    // ── El formato: una sola regla para todas las fuentes ───────────────────────────────────

    [Fact]
    public void Un_precio_en_la_moneda_de_la_cultura_lleva_su_simbolo_y_en_otra_su_codigo()
    {
        Assert.Equal(480_000m.ToString("C0", EsCo), FormatoDelListado.Precio(480_000m, "COP", EsCo));
        Assert.Contains("480.000", FormatoDelListado.Precio(480_000m, "cop", EsCo), StringComparison.Ordinal);
        Assert.Equal("USD 1.200", FormatoDelListado.Precio(1_200m, "USD", EsCo));
    }

    [Fact]
    public void La_fecha_se_dice_en_la_zona_del_sitio_y_no_en_UTC()
    {
        // 00:00 UTC del 5 de octubre son las 19:00 del 4 en Bogotá.
        var fecha = FormatoDelListado.Fecha(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero), EsCo, Bogota);

        Assert.StartsWith("4 ", fecha, StringComparison.Ordinal);
        Assert.EndsWith("2026", fecha, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(480, "8 h")]
    [InlineData(45, "45 min")]
    [InlineData(90, "1 h 30 min")]
    public void La_duracion_en_horas_y_minutos(int minutos, string esperado)
        => Assert.Equal(esperado, FormatoDelListado.Duracion(minutos));

    [Fact]
    public void La_consulta_no_distingue_mayusculas_ni_tildes_y_sin_consulta_todo_responde()
    {
        Assert.True(FormatoDelListado.Responde("educacion", "Plataformas", "Educación"));
        Assert.False(FormatoDelListado.Responde("piscina", "Apartamento", null));
        Assert.True(FormatoDelListado.Responde(null, "lo que sea"));
    }

    [Fact]
    public void La_zona_por_defecto_existe_y_una_que_el_sistema_no_conoce_no()
    {
        Assert.NotNull(new ListadosSettings().Zona());
        Assert.Null(new ListadosSettings { ZonaHoraria = "Marte/Olympus" }.Zona());
    }

    // ── El resolver: elige la fuente y le pasa la petición ──────────────────────────────────

    private sealed class FuenteQueRecuerda(string clave) : IFuenteDeListado
    {
        public PeticionDelListado? Ultima { get; private set; }
        public string Clave => clave;
        public IReadOnlyList<FilaDelListado> Filas(PeticionDelListado peticion)
        {
            Ultima = peticion;
            return [new FilaDelListado("1", "Una", Specs: [new DatoDeLaFila(peticion.Rotulo("DataGrid.Price", "Precio"), "$ 1")])];
        }
    }

    private static DataGridResolutor Resolutor(IFuenteDeListado fuente, string? q = null, params (string, string)[] diccionario)
    {
        var http = new DefaultHttpContext();
        if (q is not null)
        {
            http.Request.QueryString = new QueryString($"?q={Uri.EscapeDataString(q)}");
        }

        return new DataGridResolutor(
            ElementoFalso.Fallback,
            [fuente],
            ElementoFalso.Diccionario(diccionario),
            Options.Create(new ListadosSettings()),
            NullLogger<DataGridResolutor>.Instance,
            new HttpContextAccessor { HttpContext = http });
    }

    [Fact]
    public void Consulta_la_fuente_que_eligio_el_editor_con_el_q_de_la_pagina()
    {
        var fuente = new FuenteQueRecuerda("cursos");

        var resuelto = Resolutor(fuente, q: "  angular ").Resolver(ElementoFalso.Con(("fuente", "cursos")));

        Assert.Equal("Una", Assert.Single(resuelto.Props.Rows!).Title);
        Assert.Equal("angular", fuente.Ultima!.Consulta);
        // Las mismas filas en HTML: lo que se ve sin JavaScript.
        Assert.Contains(">Una</h3>", resuelto.RespaldoHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Los_rotulos_salen_del_diccionario_y_sin_traduccion_del_respaldo()
    {
        var traducido = Resolutor(new FuenteQueRecuerda("cursos"), null, ("DataGrid.Price", "Price"))
            .Resolver(ElementoFalso.Con(("fuente", "cursos"))).Props;
        var sinTraduccion = Resolutor(new FuenteQueRecuerda("cursos"))
            .Resolver(ElementoFalso.Con(("fuente", "cursos"))).Props;

        Assert.Equal("Price", traducido.Rows![0].Specs![0].Label);
        Assert.Equal("Precio", sinTraduccion.Rows![0].Specs![0].Label);
    }

    [Fact]
    public void Una_fuente_que_no_existe_no_tumba_la_pagina_y_no_trae_filas()
    {
        var fuente = new FuenteQueRecuerda("cursos");

        var props = Resolutor(fuente).Resolver(ElementoFalso.Con(("fuente", "noticias"))).Props;
        var sinFuente = Resolutor(fuente).Resolver(ElementoFalso.Con()).Props;

        Assert.Null(props.Rows);
        Assert.Null(sinFuente.Rows);
        Assert.Null(fuente.Ultima);
    }

    [Fact]
    public void El_listado_se_ve_sin_JavaScript_con_las_mismas_filas_y_escapado()
    {
        var html = Web.Services.SynHostFallbackBuilder.Listado(
        [
            new FilaDelListado("1", "Asesoría <express>", Href: "/booking/asesoria/", Badge: "Consultoría",
                Specs: [new DatoDeLaFila("Precio", "$ 90.000")]),
            new FilaDelListado("2", "Sin página"),
        ], "Listado")!;

        Assert.Contains("<a href=\"/booking/asesoria/\">Asesoría &lt;express&gt;</a>", html, StringComparison.Ordinal);
        Assert.Contains("<dt>Precio</dt><dd>$ 90.000</dd>", html, StringComparison.Ordinal);
        Assert.Contains(">Sin página</h3>", html, StringComparison.Ordinal);
        Assert.Null(Web.Services.SynHostFallbackBuilder.Listado([], "Listado"));
        Assert.Null(Web.Services.SynHostFallbackBuilder.Listado(null, "Listado"));
    }

    // ── Las fuentes de catálogo: el mismo proveedor que su app ──────────────────────────────

    [Fact]
    public void Los_cursos_salen_del_catalogo_de_la_academia_con_sus_datos_formateados()
    {
        var catalogo = Substitute.For<ICourseCatalogProvider>();
        catalogo.SearchAsync(Arg.Any<CourseQuery>(), Arg.Any<CancellationToken>()).Returns(new CourseSearchResult(
        [
            new CourseSummary("c1", "Angular moderno", "", "Desarrollo", "Intermedio", "Ana", "/media/a.jpg", 480_000m, "COP", false, 4.8, 12, 480),
            new CourseSummary("c2", "Introducción gratuita", "", "Fundamentos", "Básico", "Luis", null, 0m, "COP", true, 4.6, 5, 45),
        ], 2));

        var filas = new CursosDelCatalogo(catalogo).Filas(Peticion());

        Assert.Equal(["Angular moderno", "Introducción gratuita"], filas.Select(f => f.Title));
        Assert.Equal("Desarrollo", filas[0].Badge);
        Assert.Equal(["Intermedio", "8 h", 480_000m.ToString("C0", EsCo)], filas[0].Specs!.Select(d => d.Value));
        Assert.Equal("Gratis", filas[1].Specs![2].Value);
    }

    [Fact]
    public void Los_eventos_salen_en_orden_de_fecha_y_la_consulta_filtra()
    {
        var catalogo = Substitute.For<IEventCatalogProvider>();
        catalogo.SearchAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new List<EventSummary>
        {
            new("e2", "e2", "Festival de jazz", "Música", "Bogotá", "Teatro Colón", new DateTimeOffset(2026, 12, 14, 20, 0, 0, TimeSpan.Zero), "", 90_000m, "COP", "general"),
            new("e1", "e1", "Conferencia de datos", "Tecnología", "Medellín", "Plaza Mayor", new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero), "", 50_000m, "COP", "general"),
        });

        var todas = new EventosDelCatalogo(catalogo).Filas(Peticion());
        var jazz = new EventosDelCatalogo(catalogo).Filas(Peticion("musica"));

        Assert.Equal(["Conferencia de datos", "Festival de jazz"], todas.Select(f => f.Title));
        Assert.StartsWith("4 ", todas[0].Specs![0].Value, StringComparison.Ordinal);
        Assert.Equal("Plaza Mayor, Medellín", todas[0].Specs![1].Value);
        Assert.Equal("Festival de jazz", Assert.Single(jazz).Title);
    }

    [Fact]
    public void Los_inmuebles_salen_del_catalogo_del_portal_con_su_ubicacion()
    {
        var catalogo = Substitute.For<IPropertyCatalogProvider>();
        catalogo.SearchAsync(Arg.Any<PropertyQuery>(), Arg.Any<CancellationToken>()).Returns(new PropertySearchResult(
        [
            new PropertyListing("p1", "p1", "Apartamento en Chicó", "sale", "apartamento", 850_000_000m, "COP", "Bogotá", "Chicó", 3, 2, 98, 6, 0, 0, "/media/p1.jpg"),
        ], []));

        var fila = Assert.Single(new InmueblesDelCatalogo(catalogo).Filas(Peticion()));

        Assert.Equal("Apartamento", fila.Badge);
        Assert.Equal("Chicó, Bogotá", fila.Specs![0].Value);
        Assert.Equal(850_000_000m.ToString("C0", EsCo), fila.Specs![1].Value);
    }
}
