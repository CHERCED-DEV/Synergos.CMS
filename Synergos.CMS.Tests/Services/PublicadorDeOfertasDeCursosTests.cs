using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Notifications;
using Synergos.CMS.Web.Services.Academy;
using Synergos.CMS.Web.Services.Catalog;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Publicar un <c>coursePage</c> publica su oferta en <c>Bff.Educacion</c>, con el catálogo en el contenido, y
/// despublicarlo la retira; nada de eso tumba la acción del editor (010-educacion-matricula, T025).
/// </summary>
/// <remarks>
/// El publicador, con un orquestador falso que graba lo que le llega: la ruta, la llave y el cuerpo. Los manejadores,
/// con el publicador sustituido. Que la forma del cuerpo sea la del contrato del orquestador lo cruza
/// <c>ConsumidorCmsDeBffEducacionTests</c>.
/// </remarks>
public sealed class PublicadorDeOfertasDeCursosTests
{
    internal sealed class OrquestadorFalso : HttpMessageHandler
    {
        public List<(string Ruta, string? Llave, string Cuerpo)> Llamadas { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage> Responde { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"curso":"x","precio":1,"moneda":"COP","planes":[],"precios":1,"retirada":false}""",
                Encoding.UTF8, "application/json"),
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Llamadas.Add((req.RequestUri!.AbsolutePath,
                req.Headers.TryGetValues("Idempotency-Key", out var v) ? v.Single() : null,
                req.Content is null ? "" : await req.Content.ReadAsStringAsync(ct)));
            return Responde(req);
        }
    }

    private sealed class Fabrica(HttpMessageHandler cable) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(PublicadorDeOfertasDeCursos.ClientName, name);
            return new HttpClient(cable, disposeHandler: false) { BaseAddress = new Uri("http://bff-educacion.local/") };
        }
    }

    internal static PublicadorDeOfertasDeCursos Publicador(OrquestadorFalso orquestador, bool hayDestino = true)
        => new(new Fabrica(orquestador), NullLogger<PublicadorDeOfertasDeCursos>.Instance, hayDestino);

    /// <summary>Un curso como lo sirve el catálogo: con las reglas de precio de verdad (contado y cuotas, o gratis).</summary>
    internal static CourseDetail Curso(string id = "gestion-agil", decimal precio = 320_000m)
        => new(
            new CourseSummary(id, "Gestión ágil", "", "Gestión", "Inicial", "Ana", null, precio, "COP", precio <= 0m, 4.8, 12, 360),
            "", [], new CourseInstructor("ana", "Ana", "", "", null), [], CoursePricingRules.Build(precio, "COP"));

    private static HttpResponseMessage Rechazo(HttpStatusCode estado, string codigo) => new(estado)
    {
        Content = new StringContent($$"""{"code":"{{codigo}}","detail":"no","transient":false}""", Encoding.UTF8, "application/problem+json"),
    };

    [Fact]
    public async Task Publicar_manda_el_curso_con_su_precio_su_moneda_y_el_total_de_cada_plan_que_la_ficha_ofrece()
    {
        var orquestador = new OrquestadorFalso();

        var estado = await Publicador(orquestador).PublicarAsync(Curso());

        Assert.Equal(EstadoDeLaOfertaDeCurso.Hecha, estado);
        var (ruta, llave, cuerpo) = Assert.Single(orquestador.Llamadas);
        Assert.Equal("/v1/ofertas", ruta);
        Assert.False(string.IsNullOrWhiteSpace(llave));
        var o = JsonDocument.Parse(cuerpo).RootElement;
        Assert.Equal("gestion-agil", o.GetProperty("curso").GetString());
        Assert.Equal(320_000m, o.GetProperty("precio").GetDecimal());
        Assert.Equal("COP", o.GetProperty("moneda").GetString());
        var plan = Assert.Single(o.GetProperty("planes").EnumerateArray());
        Assert.Equal("full", plan.GetProperty("codigo").GetString());
        Assert.Equal(320_000m, plan.GetProperty("total").GetDecimal());
        // Cómo se llaman, para el aviso de la matrícula (NM-2): el título de la ficha y la etiqueta del plan.
        Assert.Equal("Gestión ágil", o.GetProperty("nombre").GetString());
        Assert.Equal("Pago de contado", plan.GetProperty("nombre").GetString());
    }

    /// <summary>
    /// D-010-P1: el plan en cuotas (<c>emi-3</c>, con su recargo del 8 % cobrado de una vez) queda declarado en el
    /// catálogo y SIN ofrecer: tampoco se publica, así que por la puerta no se puede abrir con él.
    /// </summary>
    [Fact]
    public async Task El_plan_en_cuotas_queda_declarado_en_el_catalogo_y_no_se_publica()
    {
        var orquestador = new OrquestadorFalso();
        var curso = Curso();
        Assert.Contains(curso.Plans, p => p.Code == "emi-3" && p.Installments > 1);

        await Publicador(orquestador).PublicarAsync(curso);

        var cuerpo = Assert.Single(orquestador.Llamadas).Cuerpo;
        Assert.DoesNotContain("emi-3", cuerpo, StringComparison.Ordinal);
        Assert.Equal(["full"], PublicadorDeOfertasDeCursos.PlanesOfrecidos(curso).Select(p => p.Code));
    }

    [Fact]
    public async Task Un_curso_gratis_se_publica_con_total_cero_explicito()
    {
        var orquestador = new OrquestadorFalso();

        await Publicador(orquestador).PublicarAsync(Curso("intro-ia", 0m));

        var o = JsonDocument.Parse(Assert.Single(orquestador.Llamadas).Cuerpo).RootElement;
        Assert.Equal(0m, o.GetProperty("precio").GetDecimal());
        var plan = Assert.Single(o.GetProperty("planes").EnumerateArray());
        Assert.Equal("free", plan.GetProperty("codigo").GetString());
        Assert.Equal(0m, plan.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task Republicar_es_otra_publicacion_con_su_propia_llave()
    {
        var orquestador = new OrquestadorFalso();
        var publicador = Publicador(orquestador);

        await publicador.PublicarAsync(Curso());
        await publicador.PublicarAsync(Curso());

        Assert.Equal(2, orquestador.Llamadas.Select(l => l.Llave).Distinct().Count());
    }

    [Fact]
    public async Task Retirar_va_a_su_ruta_sin_cuerpo_y_retirar_la_que_no_esta_no_hace_nada()
    {
        var orquestador = new OrquestadorFalso { Responde = _ => Rechazo(HttpStatusCode.NotFound, PublicadorDeOfertasDeCursos.NoExiste) };

        var estado = await Publicador(orquestador).RetirarAsync("gestion-agil");

        Assert.Equal(EstadoDeLaOfertaDeCurso.Hecha, estado);
        var (ruta, llave, cuerpo) = Assert.Single(orquestador.Llamadas);
        Assert.Equal("/v1/ofertas/gestion-agil/retirar", ruta);
        Assert.False(string.IsNullOrWhiteSpace(llave));
        Assert.Equal("", cuerpo);
    }

    [Fact]
    public async Task Un_rechazo_o_un_orquestador_caido_es_Fallida_y_no_lanza()
    {
        var rechaza = new OrquestadorFalso { Responde = _ => Rechazo(HttpStatusCode.BadRequest, "educacion.oferta_invalida") };
        var caido = new OrquestadorFalso { Responde = _ => throw new HttpRequestException("Connection refused") };

        Assert.Equal(EstadoDeLaOfertaDeCurso.Fallida, await Publicador(rechaza).PublicarAsync(Curso()));
        Assert.Equal(EstadoDeLaOfertaDeCurso.Fallida, await Publicador(caido).PublicarAsync(Curso()));
        Assert.Equal(EstadoDeLaOfertaDeCurso.Fallida, await Publicador(caido).RetirarAsync("gestion-agil"));
    }

    [Fact]
    public async Task Sin_destino_no_sale_a_la_red()
    {
        var orquestador = new OrquestadorFalso();
        var publicador = Publicador(orquestador, hayDestino: false);

        Assert.Equal(EstadoDeLaOfertaDeCurso.SinDestino, await publicador.PublicarAsync(Curso()));
        Assert.Equal(EstadoDeLaOfertaDeCurso.SinDestino, await publicador.RetirarAsync("gestion-agil"));
        Assert.Empty(orquestador.Llamadas);
    }

    // ─── Los manejadores: al publicar y al retirar ────────────────────────────────────────────────────────────────

    private readonly ICourseCatalogProvider _catalogo = Substitute.For<ICourseCatalogProvider>();
    private readonly IPublicadorDeOfertasDeCursos _ofertas = Substitute.For<IPublicadorDeOfertasDeCursos>();
    private readonly IAcademiaEnElContenido _academia = Substitute.For<IAcademiaEnElContenido>();
    private readonly IContentService _contenidos = Substitute.For<IContentService>();

    public PublicadorDeOfertasDeCursosTests() => _academia.Ve(Arg.Any<Guid>()).Returns(true);

    private static IOptionsMonitor<CatalogSettings> Fuente(string fuente)
    {
        var fuentes = Substitute.For<IOptionsMonitor<CatalogSettings>>();
        fuentes.CurrentValue.Returns(new CatalogSettings
        {
            Sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Academy"] = fuente },
        });
        return fuentes;
    }

    private OfertaDeCursoAlPublicar AlPublicar(string fuente = "cms")
        => new(_catalogo, _ofertas, Fuente(fuente), _academia, NullLogger<OfertaDeCursoAlPublicar>.Instance);

    private OfertaDeCursoAlRetirar AlRetirar(string fuente = "cms")
        => new(_ofertas, _contenidos, Fuente(fuente), NullLogger<OfertaDeCursoAlRetirar>.Instance);

    internal static IContent Nodo(string alias, string? slug, int id = 0)
    {
        var tipo = Substitute.For<ISimpleContentType>();
        tipo.Alias.Returns(alias);
        var nodo = Substitute.For<IContent>();
        nodo.Id.Returns(id);
        nodo.Key.Returns(Guid.NewGuid());
        nodo.ContentType.Returns(tipo);
        nodo.GetValue<string>("courseSlug", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>()).Returns(slug);
        return nodo;
    }

    private static ContentPublishedNotification Publicados(params IContent[] nodos) => new(nodos, new EventMessages());

    private void Hijos(int padre, params IContent[] hijos)
    {
        _contenidos.GetPagedDescendants(Arg.Is(padre), Arg.Is(0L), Arg.Any<int>(), out Arg.Any<long>(), Arg.Any<IQuery<IContent>?>(), Arg.Any<Ordering?>())
            .Returns(x =>
            {
                x[3] = (long)hijos.Length;
                return hijos;
            });
    }

    [Fact]
    public async Task Con_el_catalogo_en_el_contenido_publica_la_oferta_del_curso_tal_como_lo_sirve_el_catalogo()
    {
        var curso = Curso();
        _catalogo.GetCourseAsync("gestion-agil", Arg.Any<CancellationToken>()).Returns(curso);

        await AlPublicar().HandleAsync(Publicados(Nodo("coursePage", "gestion-agil"), Nodo("textPage", "otra")), default);

        await _ofertas.Received(1).PublicarAsync(curso, Arg.Any<CancellationToken>());
        await _catalogo.DidNotReceive().GetCourseAsync("otra", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Con_la_demo_un_coursePage_no_se_matricula_y_no_publica_ni_retira_nada()
    {
        _catalogo.GetCourseAsync("gestion-agil", Arg.Any<CancellationToken>()).Returns(Curso());
        Hijos(1);

        await AlPublicar("demo").HandleAsync(Publicados(Nodo("coursePage", "gestion-agil")), default);
        await AlRetirar("demo").HandleAsync(new ContentUnpublishedNotification(Nodo("coursePage", "gestion-agil", 1), new EventMessages()), default);

        await _ofertas.DidNotReceiveWithAnyArgs().PublicarAsync(default!, default);
        await _ofertas.DidNotReceiveWithAnyArgs().RetirarAsync(default!, default);
    }

    [Fact]
    public async Task Un_curso_que_el_catalogo_omite_no_tiene_oferta_y_la_que_tuviera_se_retira()
    {
        _catalogo.GetCourseAsync("sin-titulo", Arg.Any<CancellationToken>()).Returns((CourseDetail?)null);

        await AlPublicar().HandleAsync(Publicados(Nodo("coursePage", "sin-titulo"), Nodo("coursePage", null)), default);

        await _ofertas.DidNotReceiveWithAnyArgs().PublicarAsync(default!, default);
        await _ofertas.Received(1).RetirarAsync("sin-titulo", Arg.Any<CancellationToken>());
        await _ofertas.Received(1).RetirarAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// El rojo que la tienda ya pagó (004, 2026-10-22): al importar el árbol entero, cada curso se publica antes de que
    /// el catálogo alcance el <c>siteRoot</c> de la academia. Si el catálogo todavía no lo ve, no se le pregunta y no se
    /// toca nada.
    /// </summary>
    [Fact]
    public async Task Si_el_catalogo_todavia_no_ve_la_academia_no_retira_ni_le_pregunta_al_catalogo()
    {
        var nodo = Nodo("coursePage", "gestion-agil");
        _academia.Ve(nodo.Key).Returns(false);
        _catalogo.GetCourseAsync("gestion-agil", Arg.Any<CancellationToken>()).Returns((CourseDetail?)null);

        await AlPublicar().HandleAsync(Publicados(nodo), default);

        await _catalogo.DidNotReceiveWithAnyArgs().GetCourseAsync(default!, default);
        await _ofertas.DidNotReceiveWithAnyArgs().RetirarAsync(default!, default);
        await _ofertas.DidNotReceiveWithAnyArgs().PublicarAsync(default!, default);
    }

    [Fact]
    public async Task Un_fallo_al_leer_o_al_publicar_no_tumba_el_publicar_del_editor_ni_a_los_demas_cursos()
    {
        var gestion = Curso();
        var datos = Curso("datos-101", 250_000m);
        _catalogo.GetCourseAsync("roto", Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("caché"));
        _catalogo.GetCourseAsync("gestion-agil", Arg.Any<CancellationToken>()).Returns(gestion);
        _catalogo.GetCourseAsync("datos-101", Arg.Any<CancellationToken>()).Returns(datos);
        _ofertas.PublicarAsync(gestion, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("al armar el cuerpo"));

        await AlPublicar().HandleAsync(
            Publicados(Nodo("coursePage", "roto"), Nodo("coursePage", "gestion-agil"), Nodo("coursePage", "datos-101")), default);

        await _ofertas.Received(1).PublicarAsync(datos, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Despublicar_un_curso_o_el_nodo_que_lo_contiene_retira_su_oferta_y_nada_mas()
    {
        Hijos(1);
        Hijos(10, Nodo("coursePage", "uno", 11), Nodo("textPage", "no-es-curso", 12), Nodo("coursePage", "dos", 13));

        await AlRetirar().HandleAsync(new ContentUnpublishedNotification(Nodo("coursePage", "gestion-agil", 1), new EventMessages()), default);
        await AlRetirar().HandleAsync(new ContentUnpublishedNotification(Nodo("academyPage", null, 10), new EventMessages()), default);

        await _ofertas.Received(1).RetirarAsync("gestion-agil", Arg.Any<CancellationToken>());
        await _ofertas.Received(1).RetirarAsync("uno", Arg.Any<CancellationToken>());
        await _ofertas.Received(1).RetirarAsync("dos", Arg.Any<CancellationToken>());
        await _ofertas.Received(3).RetirarAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Mandar_a_la_papelera_con_sus_hijos_y_borrar_tambien_retiran()
    {
        Hijos(1, Nodo("coursePage", "hijo", 3));
        var seccion = Nodo("academyPage", null, 1);

        await AlRetirar().HandleAsync(new ContentMovedToRecycleBinNotification(
            new MoveEventInfo<IContent>(seccion, "-1,1", -20), new EventMessages()), default);
        await AlRetirar().HandleAsync(new ContentDeletedNotification(Nodo("coursePage", "borrado", 2), new EventMessages()), default);

        await _ofertas.Received(1).RetirarAsync("hijo", Arg.Any<CancellationToken>());
        await _ofertas.Received(1).RetirarAsync("borrado", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_retiro_que_falla_no_tumba_al_editor_ni_a_los_demas_cursos()
    {
        Hijos(10, Nodo("coursePage", "roto", 11), Nodo("coursePage", "bueno", 12));
        _ofertas.RetirarAsync("roto", Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("x"));

        await AlRetirar().HandleAsync(new ContentUnpublishedNotification(Nodo("academyPage", null, 10), new EventMessages()), default);

        await _ofertas.Received(1).RetirarAsync("bueno", Arg.Any<CancellationToken>());
    }
}
