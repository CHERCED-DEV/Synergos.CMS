using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="FabResolutor"/>: el destino y el nombre accesible llegan como
/// <c>actionLink</c> y <c>label</c> —la vista mandaba <c>actionUrl</c>/<c>ariaLabel</c> y el botón
/// no llevaba a ningún sitio (D1)—.
/// </summary>
public sealed class FabResolutorTests
{
    private readonly ILogger<FabResolutor> _log = Substitute.For<ILogger<FabResolutor>>();

    private FabResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void El_enlace_y_el_nombre_viajan_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("iconKey", "chat"),
            ("actionLink", ElementoFalso.Enlace("https://wa.me/573001234567", "WhatsApp", "_blank")),
            ("position", "bottom-left"),
            ("ariaLabel", "Escribinos por WhatsApp"))).Props);

        Assert.Equal(new[] { "iconKey", "actionLink", "target", "position", "label" }, cable.Keys);
        Assert.Equal("https://wa.me/573001234567", cable["actionLink"]!.ToString());
        Assert.Equal("_blank", cable["target"]!.ToString());
        Assert.Equal("Escribinos por WhatsApp", cable["label"]!.ToString());
    }

    [Fact]
    public void Sin_ariaLabel_el_nombre_accesible_es_el_texto_del_enlace()
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(
            ("actionLink", ElementoFalso.Enlace("/contacto", "Contactanos")))).Props;

        Assert.Equal(new FabProps(null, "/contacto", null, null, "Contactanos"), props);
    }

    [Fact]
    public void Un_enlace_sin_destino_no_viaja_y_se_anota()
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(
            ("iconKey", "help"),
            ("actionLink", ElementoFalso.Enlace("#", "Ayuda")))).Props;

        Assert.Null(props.ActionLink);
        Assert.Null(props.Label);
        Assert.Equal(1, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("iconKey", "plus"), ("actionLink", ElementoFalso.Enlace("/x")));

        Assert.Equal(Resolutor().Resolver(elemento).Props, Resolutor().Resolver(elemento).Props);
    }
}
