using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Notifications;
using Synergos.CMS.Web.Services.Salud;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La oferta del profesional con su agenda (003-salud-cita, T047, sobre NN-22 del núcleo): lo que manda el CMS al publicar
/// la ficha, que cabe en la oferta común de <c>Bff.Salud</c>, y los manejadores al publicar y al retirar.
/// </summary>
public sealed class PublicadorDeOfertasDeProfesionalesTests
{
    internal sealed class OrquestadorFalso : HttpMessageHandler
    {
        public List<(string Ruta, string? Llave, string Cuerpo)> Llamadas { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage> Responde { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"pieza":"ana-rios","precio":0,"moneda":"COP","variantes":[],"precios":1,"retirada":false,"recursoId":"r-1","reservasFuera":0}""",
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
            Assert.Equal(PublicadorDeOfertasDeProfesionales.ClientName, name);
            return new HttpClient(cable, disposeHandler: false) { BaseAddress = new Uri("http://bff-salud.local/") };
        }
    }

    internal static PublicadorDeOfertasDeProfesionales Publicador(OrquestadorFalso orquestador, bool hayDestino = true)
        => new(new Fabrica(orquestador), NullLogger<PublicadorDeOfertasDeProfesionales>.Instance, hayDestino, "COP", "America/Bogota");

    internal static MedicalDoctor Profesional(string id = "ana-rios", params DayOfWeek[] dias) => new(
        Id: id, FullName: "Dra. Ana Ríos", Specialty: "Medicina Interna", LicenseNumber: "RM-48213", Rating: 0, YearsExperience: 14,
        AvatarUrl: null, WorkingDays: dias.Length > 0 ? dias : [DayOfWeek.Monday, DayOfWeek.Wednesday],
        SlotStartHour: 8, SlotEndHour: 16, SlotMinutes: 30);

    private static HttpResponseMessage Rechazo(HttpStatusCode estado, string codigo) => new(estado)
    {
        Content = new StringContent($$"""{"code":"{{codigo}}","detail":"no","transient":false}""", Encoding.UTF8, "application/problem+json"),
    };

    // ─── Lo que manda ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Publica_la_pieza_a_precio_cero_y_la_agenda_de_la_ficha_en_la_zona_del_sitio()
    {
        var orquestador = new OrquestadorFalso();

        Assert.Equal(EstadoDeLaOfertaDeProfesional.Hecha, await Publicador(orquestador).PublicarAsync(Profesional()));

        var (ruta, llave, cuerpo) = Assert.Single(orquestador.Llamadas);
        Assert.Equal("/v1/ofertas", ruta);
        Assert.StartsWith("oferta-", llave);
        using var json = JsonDocument.Parse(cuerpo);
        var raiz = json.RootElement;
        Assert.Equal("ana-rios", raiz.GetProperty("pieza").GetString());
        Assert.Equal(0m, raiz.GetProperty("precio").GetDecimal());
        Assert.Equal("COP", raiz.GetProperty("moneda").GetString());
        var agenda = raiz.GetProperty("agenda");
        Assert.Equal(1, agenda.GetProperty("cupos").GetInt32());
        Assert.Equal(30, agenda.GetProperty("franjaMinutos").GetInt32());
        Assert.Equal("America/Bogota", agenda.GetProperty("zona").GetString());
        Assert.Equal(
            [("Monday", "08:00", "16:00"), ("Wednesday", "08:00", "16:00")],
            agenda.GetProperty("horario").EnumerateArray()
                .Select(b => (b.GetProperty("dia").GetString(), b.GetProperty("desde").GetString(), b.GetProperty("hasta").GetString())));
    }

    [Fact]
    public async Task Retirar_lo_que_no_esta_publicado_no_es_un_fallo_y_un_rechazo_no_lanza()
    {
        var orquestador = new OrquestadorFalso { Responde = _ => Rechazo(HttpStatusCode.NotFound, PublicadorDeOfertasDeProfesionales.NoExiste) };
        Assert.Equal(EstadoDeLaOfertaDeProfesional.Hecha, await Publicador(orquestador).RetirarAsync("ana-rios"));
        Assert.Equal("/v1/ofertas/ana-rios/retirar", Assert.Single(orquestador.Llamadas).Ruta);

        orquestador.Responde = _ => Rechazo(HttpStatusCode.Conflict, "salud.oferta_no_actualizable");
        Assert.Equal(EstadoDeLaOfertaDeProfesional.Fallida, await Publicador(orquestador).PublicarAsync(Profesional()));
    }

    [Fact]
    public async Task Sin_orquestador_configurado_no_sale_a_la_red()
    {
        var orquestador = new OrquestadorFalso();
        var publicador = Publicador(orquestador, hayDestino: false);

        Assert.Equal(EstadoDeLaOfertaDeProfesional.SinDestino, await publicador.PublicarAsync(Profesional()));
        Assert.Equal(EstadoDeLaOfertaDeProfesional.SinDestino, await publicador.RetirarAsync("ana-rios"));
        Assert.Empty(orquestador.Llamadas);
    }

    // ─── Que cabe en la oferta común de Bff.Salud ──────────────────────────────────────────────────────────────────

    private static readonly Lazy<JsonDocument> Contrato = new(() => JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "docs", "contracts", "openapi", "Synergos.Bff.Salud.json"))));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static JsonElement Esquema(string nombre)
        => Contrato.Value.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(nombre);

    private static void CabeEn(JsonElement enviado, string esquema)
    {
        var propiedades = Esquema(esquema).GetProperty("properties");
        foreach (var campo in enviado.EnumerateObject())
        {
            Assert.True(propiedades.TryGetProperty(campo.Name, out _), $"«{campo.Name}» no está en {esquema} del contrato de Bff.Salud.");
        }
    }

    /// <summary>
    /// Un campo con otro nombre lo rechazaría el orquestador y el profesional quedaría sin agenda: el cuerpo, la agenda y
    /// cada bloque del horario caben en lo que publica <c>PublishDeclaredOffer</c> de <c>Bff.Salud</c>.
    /// </summary>
    [Fact]
    public async Task Lo_que_manda_cabe_en_la_oferta_comun_de_Bff_Salud_y_retirar_existe()
    {
        var orquestador = new OrquestadorFalso();
        var publicador = Publicador(orquestador);
        await publicador.PublicarAsync(Profesional());
        await publicador.RetirarAsync("ana-rios");

        var rutas = Contrato.Value.RootElement.GetProperty("paths");
        Assert.Equal("PublishDeclaredOffer", rutas.GetProperty("/v1/ofertas").GetProperty("post").GetProperty("operationId").GetString());
        Assert.Equal("RetireDeclaredOffer", rutas.GetProperty("/v1/ofertas/{pieza}/retirar").GetProperty("post").GetProperty("operationId").GetString());
        using var cuerpo = JsonDocument.Parse(orquestador.Llamadas[0].Cuerpo);
        CabeEn(cuerpo.RootElement, "PublicarOfertaDeclaradaRequest");
        CabeEn(cuerpo.RootElement.GetProperty("agenda"), "AgendaDeLaOfertaDto");
        CabeEn(cuerpo.RootElement.GetProperty("agenda").GetProperty("horario")[0], "BloqueDeLaOfertaDto");
    }

    // ─── Los manejadores: al publicar y al retirar ─────────────────────────────────────────────────────────────────

    private readonly IDoctorDirectory _directorio = Substitute.For<IDoctorDirectory>();
    private readonly IPublicadorDeOfertasDeProfesionales _ofertas = Substitute.For<IPublicadorDeOfertasDeProfesionales>();
    private readonly IContentService _contenidos = Substitute.For<IContentService>();

    private static IOptionsMonitor<CatalogSettings> Fuente(string fuente)
    {
        var fuentes = Substitute.For<IOptionsMonitor<CatalogSettings>>();
        fuentes.CurrentValue.Returns(new CatalogSettings
        {
            Sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Salud"] = fuente },
        });
        return fuentes;
    }

    private OfertaDeProfesionalAlPublicar AlPublicar(string fuente = "cms")
        => new(_directorio, _ofertas, Fuente(fuente), NullLogger<OfertaDeProfesionalAlPublicar>.Instance);

    private OfertaDeProfesionalAlRetirar AlRetirar(string fuente = "cms")
        => new(_ofertas, _contenidos, Fuente(fuente), NullLogger<OfertaDeProfesionalAlRetirar>.Instance);

    private static IContent Nodo(string alias, string? slug, int id = 0)
    {
        var tipo = Substitute.For<ISimpleContentType>();
        tipo.Alias.Returns(alias);
        var nodo = Substitute.For<IContent>();
        nodo.Id.Returns(id);
        nodo.ContentType.Returns(tipo);
        nodo.GetValue<string>("professionalSlug", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>()).Returns(slug);
        return nodo;
    }

    [Fact]
    public async Task Con_el_directorio_en_el_contenido_publica_al_profesional_tal_como_lo_sirve_el_directorio()
    {
        var profesional = Profesional();
        _directorio.GetAsync("ana-rios", Arg.Any<CancellationToken>()).Returns(profesional);

        await AlPublicar().HandleAsync(new ContentPublishedNotification(
            [Nodo("professionalPage", "ana-rios"), Nodo("pageBase", "ana-rios")], new EventMessages()), default);

        await _ofertas.Received(1).PublicarAsync(profesional, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Con_la_muestra_o_sin_que_el_directorio_lo_vea_o_sin_dias_no_toca_la_oferta()
    {
        _directorio.GetAsync("sin-dias", Arg.Any<CancellationToken>()).Returns(Profesional("sin-dias") with { WorkingDays = [] });
        _directorio.GetAsync("ana-rios", Arg.Any<CancellationToken>()).Returns(Profesional());

        await AlPublicar("demo").HandleAsync(new ContentPublishedNotification([Nodo("professionalPage", "ana-rios")], new EventMessages()), default);
        await AlPublicar().HandleAsync(new ContentPublishedNotification(
            [Nodo("professionalPage", "no-lo-ve"), Nodo("professionalPage", "sin-dias"), Nodo("professionalPage", null)], new EventMessages()), default);

        await _ofertas.DidNotReceiveWithAnyArgs().PublicarAsync(default!, default);
        await _ofertas.DidNotReceiveWithAnyArgs().RetirarAsync(default!, default);
    }

    [Fact]
    public async Task Al_despublicar_un_nodo_retira_la_oferta_de_sus_profesionales_y_un_fallo_no_tumba_la_accion()
    {
        var carpeta = Nodo("pageBase", null, id: 7);
        _contenidos.GetPagedDescendants(Arg.Is(7), Arg.Is(0L), Arg.Any<int>(), out Arg.Any<long>(), Arg.Any<IQuery<IContent>?>(), Arg.Any<Ordering?>())
            .Returns(x =>
            {
                x[3] = 2L;
                return new[] { Nodo("professionalPage", "ana-rios"), Nodo("professionalPage", "carlos-mejia") };
            });
        _ofertas.RetirarAsync("ana-rios", Arg.Any<CancellationToken>()).Returns<EstadoDeLaOfertaDeProfesional>(_ => throw new HttpRequestException("caído"));

        await AlRetirar().HandleAsync(new ContentUnpublishedNotification([carpeta], new EventMessages()), default);

        await _ofertas.Received(1).RetirarAsync("ana-rios", Arg.Any<CancellationToken>());
        await _ofertas.Received(1).RetirarAsync("carlos-mejia", Arg.Any<CancellationToken>());
    }
}
