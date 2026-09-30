using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="CookieConsentResolutor"/>: el enlace a la política llega como
/// <c>policyLink</c> —la vista lo mandaba como <c>policyUrl</c> y el aviso salía sin él (D1)— con
/// su texto como <c>policyLabel</c>.
/// </summary>
public sealed class CookieConsentResolutorTests
{
    private readonly ILogger<CookieConsentResolutor> _log = Substitute.For<ILogger<CookieConsentResolutor>>();

    private CookieConsentResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void Los_textos_y_la_politica_viajan_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("bannerText", "Usamos cookies propias."),
            ("acceptLabel", "Acepto"),
            ("rejectLabel", "No, gracias"),
            ("settingsLabel", "Elegir"),
            ("policyLink", ElementoFalso.Enlace("/privacidad", "Nuestra política", "_blank")))).Props);

        Assert.Equal(new[] { "bannerText", "acceptLabel", "rejectLabel", "settingsLabel", "policyLink", "policyLabel" }, cable.Keys);
        Assert.Equal("/privacidad", cable["policyLink"]!.ToString());
        Assert.Equal("Nuestra política", cable["policyLabel"]!.ToString());
        Assert.False(cable.ContainsKey("policyUrl"));
    }

    [Fact]
    public void Una_politica_sin_destino_no_viaja_ni_su_texto_y_se_anota()
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(
            ("bannerText", "Usamos cookies."),
            ("policyLink", ElementoFalso.Enlace("", "Política")))).Props;

        Assert.Equal(new CookieConsentProps("Usamos cookies.", null, null, null, null, null), props);
        Assert.Equal(1, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("acceptLabel", "Sí"), ("policyLink", ElementoFalso.Enlace("/p", "P")));

        Assert.Equal(Resolutor().Resolver(elemento).Props, Resolutor().Resolver(elemento).Props);
    }
}
