using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Tests.Services.SynHost;
using Synergos.CMS.Web.Services.Puerta;
using Synergos.CMS.Web.Services.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Lo del CMS en la reserva de un servicio (011-booking-reserva): la sección de negocio con su camino
/// (<c>puerta</c>/<c>apagada</c>), el resolver del elemento —que lee el servicio de la ficha reservable que eligió el
/// editor— y la entrada de la puerta con el enlace del aviso.
/// </summary>
public sealed class ReservaDelCmsTests
{
    private static readonly Guid Masaje = Guid.Parse("51e3c7f8-96f8-4458-a650-5adec6126174");
    private static readonly Guid Bogota = Guid.Parse("8b0e2f6a-1c3d-4e5f-9a7b-6c5d4e3f2a10");

    // ── La sección (T011) ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Sin_declarar_nada_el_sitio_reserva_por_la_puerta()
        => Assert.Equal(new NegocioDeReserva(CaminoDeReserva.Puerta), new ReservaFeatureSettings().Para(null));

    [Fact]
    public void Un_sitio_apaga_la_reserva_y_los_demas_la_conservan()
    {
        var seccion = Seccion(new() { [$"Sitios:{Bogota}:Camino"] = " Apagada " });

        Assert.Equal(CaminoDeReserva.Apagada, seccion.Para(Bogota).Camino);
        Assert.Equal(CaminoDeReserva.Puerta, seccion.Para(Guid.NewGuid()).Camino);
        Assert.Empty(seccion.Problemas());
    }

    [Theory]
    [InlineData("directa")]
    [InlineData("puertas")]
    public void Una_palabra_que_no_es_ninguna_de_las_dos_no_deja_arrancar(string mala)
    {
        var problema = Assert.Single(Seccion(new() { ["Camino"] = mala }).Problemas());
        Assert.Contains(mala, problema, StringComparison.Ordinal);
    }

    // ── El camino que llega al elemento (T012, T034) ─────────────────────────────────────────

    [Theory]
    [InlineData(CaminoDeReserva.Puerta, true, CaminoDeReserva.Puerta)]
    [InlineData(CaminoDeReserva.Puerta, false, CaminoDeReserva.NoDisponible)]
    [InlineData(CaminoDeReserva.Apagada, true, CaminoDeReserva.Apagada)]
    [InlineData(CaminoDeReserva.Apagada, false, CaminoDeReserva.Apagada)]
    public void Lo_declarado_gana_y_sin_puerta_no_hay_reserva(string declarado, bool laPuertaAbre, string llega)
        => Assert.Equal(llega, Resolver(Declarado(declarado), Puerta(laPuertaAbre), Bloque()).Camino);

    // ── El servicio sale de la ficha (T012) ──────────────────────────────────────────────────

    [Fact]
    public void El_servicio_viaja_con_la_clave_de_su_ficha_y_lo_que_ella_declara()
    {
        var props = Resolver(Declarado(CaminoDeReserva.Puerta), Puerta(true), Bloque());

        Assert.Equal("Reserva tu cita", props.Heading);
        Assert.Equal("/booking/contacto/", props.EnlaceAlternativo);
        Assert.Equal("America/Bogota", props.Zona);
        var servicio = Assert.IsType<ServicioReservable>(props.Servicio);
        Assert.Equal(Masaje.ToString("D"), servicio.Id);
        Assert.Equal("Masaje terapéutico · 60 min", servicio.Nombre);
        Assert.Equal(60, servicio.DuracionMinutos);
        Assert.Equal(["Monday", "Wednesday", "Saturday"], servicio.Dias);
        Assert.Equal(("08:00", "18:00"), (servicio.Abre, servicio.Cierra));
    }

    [Fact]
    public void Una_ficha_no_marcada_reservable_no_viaja_como_servicio()
        => Assert.Null(Resolver(Declarado(CaminoDeReserva.Puerta), Puerta(true), Bloque(reservable: false)).Servicio);

    [Theory]
    [InlineData(0, "08:00", "18:00")]
    [InlineData(60, "8", "18:00")]
    [InlineData(60, "18:00", "08:00")]
    public void Una_ficha_reservable_incompleta_no_ofrece_horas(int duracion, string abre, string cierra)
        => Assert.Null(Resolver(Declarado(CaminoDeReserva.Puerta), Puerta(true), Bloque(duracion: duracion, abre: abre, cierra: cierra)).Servicio);

    [Fact]
    public void Sin_ficha_elegida_y_fuera_de_una_ficha_reservable_no_hay_servicio()
    {
        Assert.Null(Resolver(Declarado(CaminoDeReserva.Puerta), Puerta(true), ElementoFalso.Con(("heading", "Reserva tu cita"))).Servicio);
        var pagina = ElementoFalso.Ficha(Guid.NewGuid(), "Reservar");
        Assert.Null(Resolver(Declarado(CaminoDeReserva.Puerta), Puerta(true), ElementoFalso.Con(("heading", "Reserva tu cita")), pagina).Servicio);
    }

    [Fact]
    public void Sin_ficha_elegida_vale_la_ficha_reservable_que_contiene_el_bloque()
    {
        var ficha = ElementoFalso.Ficha(Masaje, "Masaje terapéutico · 60 min",
            ("reservable", true), ("duracionMinutos", 60), ("diasDeAtencion", new[] { "lunes" }), ("abre", "08:00"), ("cierra", "18:00"));

        var servicio = Resolver(Declarado(CaminoDeReserva.Puerta), Puerta(true), ElementoFalso.Con(("heading", "Reserva tu cita")), ficha).Servicio;

        Assert.Equal(Masaje.ToString("D"), Assert.IsType<ServicioReservable>(servicio).Id);
    }

    // ── La puerta y el enlace del aviso (T010, T014) ─────────────────────────────────────────

    [Fact]
    public void La_puerta_abre_la_reserva_a_miembros_con_su_clase_de_sujeto_y_el_enlace_que_lee_el_elemento()
    {
        var flujo = AppSettings().GetProperty("Synergos").GetProperty("Puerta").GetProperty("Flujos").GetProperty(CaminoDeReserva.Flujo);

        Assert.Equal("Miembro", flujo.GetProperty("Acceso").GetString());
        Assert.Equal("reservas.cliente", flujo.GetProperty("SujetoKind").GetString());
        var ruta = flujo.GetProperty("Aviso").GetProperty("Ruta").GetString()!;
        Assert.Contains("reserva={id}", ruta.Split('?').Last().Split('&'));
        Assert.NotEmpty(TablaDeLaPuerta.Incrustada().DelFlujo(CaminoDeReserva.Flujo));
    }

    // ── Montaje ──────────────────────────────────────────────────────────────────────────────

    private static ReservaFeatureSettings Seccion(Dictionary<string, string?> claves)
        => new ConfigurationBuilder().AddInMemoryCollection(claves).Build().Get<ReservaFeatureSettings>()!;

    private static INegocioDelSitio<NegocioDeReserva> Declarado(string camino)
    {
        var negocio = Substitute.For<INegocioDelSitio<NegocioDeReserva>>();
        negocio.Actual().Returns(new NegocioDeReserva(camino));
        return negocio;
    }

    private static IPuertaDeLosFlujos Puerta(bool abre)
    {
        var puerta = Substitute.For<IPuertaDeLosFlujos>();
        puerta.Abre(CaminoDeReserva.Flujo).Returns(abre);
        return puerta;
    }

    private static IPublishedElement Bloque(
        bool reservable = true, int duracion = 60, string abre = "08:00", string cierra = "18:00")
        => ElementoFalso.Con(
            ("heading", "Reserva tu cita"),
            ("servicio", ElementoFalso.Ficha(Masaje, "Masaje terapéutico · 60 min",
                ("reservable", reservable), ("duracionMinutos", duracion),
                ("diasDeAtencion", new[] { "lunes", "miércoles", "sábado", "festivo" }), ("abre", abre), ("cierra", cierra))),
            ("enlaceAlternativo", ElementoFalso.Enlace("/booking/contacto/", "Escríbenos")));

    private static ReservaProps Resolver(
        INegocioDelSitio<NegocioDeReserva> negocio, IPuertaDeLosFlujos puerta, IPublishedElement bloque, IPublishedContent? pagina = null)
        => new ReservaResolutor(ElementoFalso.Fallback, negocio, puerta, Options.Create(new ListadosSettings()), Contexto(pagina),
                NullLogger<ReservaResolutor>.Instance)
            .Resolver(bloque).Props;

    /// <summary>El contexto de una petición que pinta <paramref name="pagina"/> (sin página: fuera de una petición).</summary>
    private static IUmbracoContextAccessor Contexto(IPublishedContent? pagina)
    {
        var accesor = Substitute.For<IUmbracoContextAccessor>();
        if (pagina is null) return accesor;
        var peticion = Substitute.For<IPublishedRequest>();
        peticion.PublishedContent.Returns(pagina);
        var contexto = Substitute.For<IUmbracoContext>();
        contexto.PublishedRequest.Returns(peticion);
        accesor.TryGetUmbracoContext(out Arg.Any<IUmbracoContext?>()).Returns(x => { x[0] = contexto; return true; });
        return accesor;
    }

    private static JsonElement AppSettings()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln"))) dir = dir.Parent;
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "Synergos.CMS.Web", "appsettings.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return doc.RootElement.Clone();
    }
}
