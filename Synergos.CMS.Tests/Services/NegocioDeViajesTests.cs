using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Tests.Services.SynHost;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Puerta;
using Synergos.CMS.Web.Services.SynHost;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Por dónde reservan el portal de viajes y el asistente de hotel (ADR 0140; línea 006, T015): lo DECLARA el CMS con la
/// regla de <see cref="CaminoDeCompra"/> sobre <c>viajes.reserva</c>, con «directa» por defecto (P5).
/// </summary>
/// <remarks>
/// <para><b>Al revés que Alquiler y Eventos, el valor base no está vacío.</b> El despliegue ya le da destino a
/// <c>Bff.Viajes</c> (<c>Synergos:Viajes:BaseUrl</c>, que la ruta directa usa con <c>Mode=Bff</c>), así que con la regla
/// sola la puerta abriría en todos los sitios el día que se integre. Ningún sitio va por la puerta hasta que la declara,
/// después de verificarla en vivo.</para>
///
/// <para>Las dos rutas se conservan: la puerta (<c>/api/flujos/viajes.reserva/…</c>) y la directa (<c>/api/travel/…</c>,
/// <c>/api/booking/…</c>).</para>
/// </remarks>
public sealed class NegocioDeViajesTests
{
    private static readonly Guid Bogota = Guid.Parse("8b0e2f6a-1c3d-4e5f-9a7b-6c5d4e3f2a10");
    private static readonly Guid Medellin = Guid.Parse("2d4f6a8c-0e1b-4c3d-8e7f-9a0b1c2d3e4f");

    [Fact]
    public void Por_defecto_los_dos_elementos_reservan_por_la_directa_aunque_la_puerta_abra()
    {
        Assert.Equal(new NegocioDeTravelShell("/api/travel", CaminoDeCompra.Directa), new TravelShellFeatureSettings().Para(null));
        Assert.Equal(new NegocioDeBookingWizard("/api/booking", CaminoDeCompra.Directa), new BookingWizardFeatureSettings().Para(null));

        var abierta = Caminos(abierto: true, conDestino: true);
        Assert.Equal(CaminoDeCompra.Directa, Portal(new TravelShellFeatureSettings().Para(null), abierta).BookingPath);
        Assert.Equal(CaminoDeCompra.Directa, Asistente(new BookingWizardFeatureSettings().Para(null), abierta).BookingPath);
    }

    /// <summary>
    /// Un sitio pasa a la puerta declarándola; los demás siguen por la directa. El asistente de hotel decide sobre el MISMO
    /// flujo: una estadía es un viaje de un ítem.
    /// </summary>
    [Fact]
    public void Un_sitio_que_declara_la_puerta_va_por_ella_y_los_demas_siguen_por_la_directa()
    {
        var abierta = Caminos(abierto: true, conDestino: true);

        var portal = Seccion<TravelShellFeatureSettings>(new() { [$"Sitios:{Bogota}:BookingPath"] = " Puerta " });
        Assert.Equal(CaminoDeCompra.Puerta, Portal(portal.Para(Bogota), abierta).BookingPath);
        Assert.Equal(CaminoDeCompra.Directa, Portal(portal.Para(Medellin), abierta).BookingPath);

        var asistente = Seccion<BookingWizardFeatureSettings>(new() { [$"Sitios:{Bogota}:BookingPath"] = "puerta" });
        Assert.Equal(CaminoDeCompra.Puerta, Asistente(asistente.Para(Bogota), abierta).BookingPath);
        Assert.Equal(CaminoDeCompra.Directa, Asistente(asistente.Para(null), abierta).BookingPath);
    }

    /// <summary>Un sitio con el valor vacío vuelve a la regla: la puerta si abre, la directa si no.</summary>
    [Fact]
    public void Un_sitio_con_el_valor_vacio_vuelve_a_la_regla()
    {
        var seccion = Seccion<TravelShellFeatureSettings>(new() { [$"Sitios:{Bogota}:BookingPath"] = "" });

        Assert.Null(seccion.Para(Bogota).BookingPath);
        Assert.Equal(CaminoDeCompra.Puerta, Portal(seccion.Para(Bogota), Caminos(abierto: true, conDestino: true)).BookingPath);
        Assert.Equal(CaminoDeCompra.Directa, Portal(seccion.Para(Bogota), Caminos(abierto: true, conDestino: false)).BookingPath);
    }

    /// <summary>
    /// Lo que declara <c>Synergos:Puerta:Flujos:viajes.reserva:Camino</c> (núcleo N3) sólo cuenta donde el sitio vuelve a
    /// la regla: el «directa» por defecto del sitio gana sobre él.
    /// </summary>
    [Fact]
    public void El_camino_de_la_puerta_solo_cuenta_donde_el_sitio_vuelve_a_la_regla()
    {
        var puertaEnLaPuerta = Caminos(abierto: true, conDestino: true, camino: CaminoDeCompra.Puerta);

        Assert.Equal(CaminoDeCompra.Directa, Portal(new TravelShellFeatureSettings().Para(null), puertaEnLaPuerta).BookingPath);
        Assert.Equal(CaminoDeCompra.Puerta, Portal(new NegocioDeTravelShell("/api/travel"), puertaEnLaPuerta).BookingPath);
    }

    /// <summary>Mal escrito no se ignora: caería a la regla y el elemento reservaría por un camino que nadie eligió.</summary>
    [Theory]
    [InlineData(TravelShellFeatureSettings.Seccion, "BookingPath", "puertas")]
    [InlineData(BookingWizardFeatureSettings.Seccion, "Sitios:8b0e2f6a-1c3d-4e5f-9a7b-6c5d4e3f2a10:BookingPath", "orquestador")]
    public void Un_camino_mal_escrito_no_deja_arrancar_y_dice_la_regla(string seccion, string clave, string valor)
    {
        using var sp = seccion == TravelShellFeatureSettings.Seccion
            ? Proveedor<TravelShellFeatureSettings, NegocioDeTravelShell>(seccion, clave, valor)
            : Proveedor<BookingWizardFeatureSettings, NegocioDeBookingWizard>(seccion, clave, valor);

        var error = Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("BookingPath", error.Message, StringComparison.Ordinal);
        Assert.Contains("o no estar (y decide la regla", error.Message, StringComparison.Ordinal);
    }

    /// <summary>La ruta del aviso por sitio (D-006-AVISO): vacía en la base, la del sitio donde se reservó.</summary>
    [Fact]
    public void La_ruta_del_aviso_es_del_sitio_que_la_declara_y_en_los_demas_rige_la_del_flujo()
    {
        var asistente = Seccion<BookingWizardFeatureSettings>(new() { [$"Sitios:{Bogota}:AvisoRuta"] = " /hoteles/?viaje={id} " });

        Assert.Equal("/hoteles/?viaje={id}", asistente.Para(Bogota).AvisoRuta);
        Assert.Null(asistente.Para(Medellin).AvisoRuta);
        Assert.Null(new BookingWizardFeatureSettings().Para(null).AvisoRuta);
    }

    [Theory]
    [InlineData("https://evil.test/?viaje={id}")]
    [InlineData("//evil.test/?viaje={id}")]
    [InlineData("hoteles/?viaje={id}")]
    public void Una_ruta_del_aviso_que_no_es_del_sitio_no_deja_arrancar(string ruta)
    {
        using var sp = Proveedor<BookingWizardFeatureSettings, NegocioDeBookingWizard>(
            BookingWizardFeatureSettings.Seccion, "Sitios:8b0e2f6a-1c3d-4e5f-9a7b-6c5d4e3f2a10:AvisoRuta", ruta);

        var error = Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("AvisoRuta", error.Message, StringComparison.Ordinal);
        Assert.Contains("tiene que ser una ruta del sitio", error.Message, StringComparison.Ordinal);
    }

    // ── Ayudantes ────────────────────────────────────────────────────────────────────────────

    private static TSeccion Seccion<TSeccion>(Dictionary<string, string?> claves)
        => new ConfigurationBuilder().AddInMemoryCollection(claves).Build().Get<TSeccion>()
           ?? throw new InvalidOperationException("la sección no se enlazó");

    private static ICaminoDeLosFlujos Caminos(bool abierto, bool conDestino, string? camino = null)
    {
        var ajustes = new PuertaSettings();
        if (abierto)
        {
            ajustes.Flujos[TravelShellResolutor.FlujoDeLaReserva] =
                new FlujoDeLaPuertaSettings { Acceso = AccesoDeLaPuerta.Miembro, Camino = camino };
        }

        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(ajustes);
        var puerta = new PuertaDeLosFlujos(TablaDeLaPuerta.Incrustada(), monitor, new DestinosDeLaPuerta(conDestino ? ["Viajes"] : []));
        return new CaminoDeLosFlujos(puerta, monitor, NullLogger<CaminoDeLosFlujos>.Instance);
    }

    private static TravelShellProps Portal(NegocioDeTravelShell negocio, ICaminoDeLosFlujos caminos)
    {
        var lector = Substitute.For<INegocioDelSitio<NegocioDeTravelShell>>();
        lector.Actual().Returns(negocio);
        return new TravelShellResolutor(ElementoFalso.Fallback, lector, caminos, NullLogger<TravelShellResolutor>.Instance)
            .Resolver(ElementoFalso.Con(("heading", "Viajes"))).Props;
    }

    private static BookingWizardProps Asistente(NegocioDeBookingWizard negocio, ICaminoDeLosFlujos caminos)
    {
        var lector = Substitute.For<INegocioDelSitio<NegocioDeBookingWizard>>();
        lector.Actual().Returns(negocio);
        return new BookingWizardResolutor(ElementoFalso.Fallback, lector, caminos, NullLogger<BookingWizardResolutor>.Instance)
            .Resolver(ElementoFalso.Con(("destinationLabel", "Cartagena"))).Props;
    }

    /// <summary>Como lo registra el composer (<c>AddSeccionDeNegocio</c>); sin Umbraco, el sitio es ninguno.</summary>
    private static ServiceProvider Proveedor<TSeccion, TNegocio>(string seccion, string clave, string valor)
        where TSeccion : class, ISeccionDeNegocio<TNegocio>
        where TNegocio : class
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{seccion}:{clave}"] = valor })
            .Build();
        var sinUmbraco = Substitute.For<IUmbracoContextAccessor>();
        sinUmbraco.TryGetUmbracoContext(out Arg.Any<IUmbracoContext>()!).Returns(false);

        return new ServiceCollection()
            .AddSingleton<IConfiguration>(config)
            .AddLogging()
            .AddSingleton(new SitioDeLaPeticion(sinUmbraco))
            .AddSeccionDeNegocio<TSeccion, TNegocio>(config.GetSection(seccion))
            .BuildServiceProvider();
    }
}
