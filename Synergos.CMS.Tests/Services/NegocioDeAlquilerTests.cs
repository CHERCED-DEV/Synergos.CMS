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
/// Por dónde alquila el elemento (ADR 0140; línea 005, research R9, T026): lo DECLARA el CMS con la regla de
/// <see cref="CaminoDeCompra"/> —el override de <c>Synergos:Features:Alquiler:RentalPath</c> (por sitio), o la puerta
/// cuando abre <c>alquiler.renta</c>, o la ruta directa—. Los mismos casos que Eventos (<c>e82e4a3e</c>).
/// </summary>
/// <remarks>
/// Las dos rutas se conservan: la puerta (<c>/api/flujos/alquiler.renta/…</c>) y la directa
/// (<c>/api/alquiler/quote|rentals</c>, con el motor que diga <c>Synergos:Alquiler:Mode</c>). Ese interruptor no
/// entra en la regla: dice qué motor atiende la directa, no si la puerta abre.
/// </remarks>
public sealed class NegocioDeAlquilerTests
{
    private static readonly Guid Bogota = Guid.Parse("8b0e2f6a-1c3d-4e5f-9a7b-6c5d4e3f2a10");
    private static readonly Guid Medellin = Guid.Parse("2d4f6a8c-0e1b-4c3d-8e7f-9a0b1c2d3e4f");

    [Fact]
    public void Sin_override_y_sin_puerta_el_alquiler_va_por_la_ruta_directa()
    {
        Assert.Equal(new NegocioDeAlquiler("/api/alquiler"), new AlquilerFeatureSettings().Para(null));
        Assert.Equal(CaminoDeCompra.Directa, Resuelto(Fijo(null), Puerta(abierto: false, conDestino: false)).RentalPath);
    }

    [Fact]
    public void Con_la_puerta_abierta_para_la_renta_va_por_la_puerta()
    {
        Assert.Equal(CaminoDeCompra.Puerta, Resuelto(Fijo(null), Puerta(abierto: true, conDestino: true)).RentalPath);
    }

    /// <summary>
    /// Sin orquestador (el clon limpio: sin <c>Synergos:Alquiler:BaseUrl</c>) la puerta contestaría 503 a cada
    /// alquiler, así que gana la directa; y lo mismo con destino y el flujo sin abrir.
    /// </summary>
    [Fact]
    public void Sin_orquestador_va_por_la_ruta_directa_aunque_el_flujo_este_abierto()
    {
        Assert.Equal(CaminoDeCompra.Directa, Resuelto(Fijo(null), Puerta(abierto: true, conDestino: false)).RentalPath);
        Assert.Equal(CaminoDeCompra.Directa, Resuelto(Fijo(null), Puerta(abierto: false, conDestino: true)).RentalPath);
    }

    /// <summary>Override: lo que declara el negocio gana a la regla, en los dos sentidos.</summary>
    [Theory]
    [InlineData(CaminoDeCompra.Directa, true)]
    [InlineData(CaminoDeCompra.Puerta, false)]
    public void El_camino_declarado_en_el_negocio_gana_a_la_regla(string declarado, bool puertaAbierta)
    {
        Assert.Equal(declarado, Resuelto(Fijo(declarado), Puerta(puertaAbierta, puertaAbierta)).RentalPath);
    }

    /// <summary>
    /// Por sitio, como toda clave de negocio, y escrito como se quiera (sin espacios ni mayúsculas). El sitio que no
    /// lo declara sigue la regla; uno con el valor vacío vuelve a ella aunque la base declare uno.
    /// </summary>
    [Fact]
    public void Un_sitio_declara_su_camino_los_demas_siguen_la_regla_y_vacio_vuelve_a_ella()
    {
        using var sp = Proveedor(new() { [$"Sitios:{Bogota}:RentalPath"] = " Directa " });
        Guid? sitio = Bogota;
        var lector = new NegocioDelSitio<AlquilerFeatureSettings, NegocioDeAlquiler>(
            sp.GetRequiredService<IOptionsMonitor<AlquilerFeatureSettings>>(), () => sitio,
            AlquilerFeatureSettings.Seccion, NullLogger.Instance);
        var abierta = Puerta(abierto: true, conDestino: true);

        Assert.Equal(CaminoDeCompra.Directa, Resuelto(lector, abierta).RentalPath);
        sitio = Medellin;
        Assert.Equal(CaminoDeCompra.Puerta, Resuelto(lector, abierta).RentalPath);
        sitio = null;
        Assert.Equal(CaminoDeCompra.Puerta, Resuelto(lector, abierta).RentalPath);

        var seccion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["RentalPath"] = "puerta", [$"Sitios:{Bogota}:RentalPath"] = "" })
            .Build().Get<AlquilerFeatureSettings>()!;
        Assert.Equal(CaminoDeCompra.Puerta, seccion.Para(null).RentalPath);
        Assert.Null(seccion.Para(Bogota).RentalPath);
    }

    /// <summary>Mal escrito no se ignora: caería a la regla y el elemento alquilaría por un camino que nadie eligió.</summary>
    [Theory]
    [InlineData("RentalPath", "directo")]
    [InlineData("Sitios:8b0e2f6a-1c3d-4e5f-9a7b-6c5d4e3f2a10:RentalPath", "orquestador")]
    public void Un_camino_mal_escrito_no_deja_arrancar_y_dice_la_regla(string clave, string valor)
    {
        using var sp = Proveedor(new() { [clave] = valor });

        var error = Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("RentalPath", error.Message, StringComparison.Ordinal);
        Assert.Contains("o no estar (y decide la regla", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// La puerta de verdad con los contratos incrustados: abre <c>alquiler.renta</c> sólo con el flujo en
    /// <c>Synergos:Puerta:Flujos</c> Y destino para <c>Bff.Alquiler</c> —la misma condición de su 503—, y nunca la de
    /// otro vertical.
    /// </summary>
    [Fact]
    public void La_puerta_abre_la_renta_solo_con_su_entrada_y_el_destino_de_su_orquestador()
    {
        Assert.True(Puerta(abierto: true, conDestino: true).Abre(AlquilerResolutor.FlujoDeLaRenta));
        Assert.False(Puerta(abierto: true, conDestino: false).Abre(AlquilerResolutor.FlujoDeLaRenta));
        Assert.False(Puerta(abierto: false, conDestino: true).Abre(AlquilerResolutor.FlujoDeLaRenta));
        Assert.False(Puerta(abierto: true, conDestino: true, destino: "Eventos").Abre(AlquilerResolutor.FlujoDeLaRenta));
    }

    // ── Ayudantes ────────────────────────────────────────────────────────────────────────────

    private static INegocioDelSitio<NegocioDeAlquiler> Fijo(string? camino)
    {
        var negocio = Substitute.For<INegocioDelSitio<NegocioDeAlquiler>>();
        negocio.Actual().Returns(new NegocioDeAlquiler("/api/alquiler", camino));
        return negocio;
    }

    private static IPuertaDeLosFlujos Puerta(bool abierto, bool conDestino, string destino = "Alquiler")
    {
        var ajustes = new PuertaSettings();
        if (abierto)
        {
            ajustes.Flujos[AlquilerResolutor.FlujoDeLaRenta] = new FlujoDeLaPuertaSettings { Acceso = AccesoDeLaPuerta.Miembro };
        }

        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(ajustes);
        return new PuertaDeLosFlujos(TablaDeLaPuerta.Incrustada(), monitor, new DestinosDeLaPuerta(conDestino ? [destino] : []));
    }

    private static AlquilerProps Resuelto(INegocioDelSitio<NegocioDeAlquiler> negocio, IPuertaDeLosFlujos puerta)
        => new AlquilerResolutor(ElementoFalso.Fallback, negocio, puerta, NullLogger<AlquilerResolutor>.Instance)
            .Resolver(ElementoFalso.Con(("heading", "Equipos"))).Props;

    /// <summary>Como lo registra el composer (<c>AddSeccionDeNegocio</c>); sin Umbraco, el sitio es ninguno.</summary>
    private static ServiceProvider Proveedor(Dictionary<string, string?> claves)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(claves.ToDictionary(c => $"{AlquilerFeatureSettings.Seccion}:{c.Key}", c => c.Value))
            .Build();
        var sinUmbraco = Substitute.For<IUmbracoContextAccessor>();
        sinUmbraco.TryGetUmbracoContext(out Arg.Any<IUmbracoContext>()!).Returns(false);

        return new ServiceCollection()
            .AddSingleton<IConfiguration>(config)
            .AddLogging()
            .AddSingleton(new SitioDeLaPeticion(sinUmbraco))
            .AddSeccionDeNegocio<AlquilerFeatureSettings, NegocioDeAlquiler>(config.GetSection(AlquilerFeatureSettings.Seccion))
            .BuildServiceProvider();
    }
}
