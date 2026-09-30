using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="ShareBarResolutor"/>: las redes llegan como la LISTA <c>platforms</c> y el
/// destino como <c>shareLink</c> —la vista mandaba el texto <c>platformsCsv</c> y <c>shareUrl</c> y
/// la barra ignoraba las dos cosas (D1)—.
/// </summary>
public sealed class ShareBarResolutorTests
{
    private readonly ILogger<ShareBarResolutor> _log = Substitute.For<ILogger<ShareBarResolutor>>();

    private ShareBarResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void Las_redes_viajan_como_lista_y_el_destino_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("platforms", new[] { "whatsapp", "linkedin" }),
            ("shareLink", ElementoFalso.Enlace("https://synergos.local/eventos/feria-del-libro")),
            ("shareTitle", "Feria del libro 2026"))).Props);

        Assert.Equal(new[] { "platforms", "shareLink", "shareTitle" }, cable.Keys);
        var redes = (JsonElement)cable["platforms"]!;
        Assert.Equal(JsonValueKind.Array, redes.ValueKind);
        Assert.Equal(new[] { "whatsapp", "linkedin" }, redes.EnumerateArray().Select(r => r.GetString()));
        Assert.Equal("https://synergos.local/eventos/feria-del-libro", cable["shareLink"]!.ToString());
    }

    [Fact]
    public void Twitter_viaja_con_el_nombre_que_le_da_el_elemento_y_las_demas_como_las_marco_el_editor()
    {
        var redes = Resolutor().Resolver(ElementoFalso.Con(
            ("platforms", new[] { "twitter", "Email", "reddit" }))).Props.Platforms;

        Assert.Equal(new[] { "x", "email", "reddit" }, redes);
    }

    [Fact]
    public void Un_destino_vacio_no_viaja_y_se_anota()
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(
            ("shareLink", ElementoFalso.Enlace("")),
            ("shareTitle", "T"))).Props;

        Assert.Null(props.ShareLink);
        Assert.Equal("T", props.ShareTitle);
        Assert.Equal(1, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("platforms", new[] { "facebook", "twitter" }), ("shareTitle", "T"));

        Assert.Equal(Resolutor().Resolver(elemento).Props.Platforms, Resolutor().Resolver(elemento).Props.Platforms);
    }
}
