using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="AccordionResolutor"/>: el TEXTO <c>itemsJson</c> (<c>title</c>/<c>content</c>)
/// llega como la LISTA <c>items</c> (<c>title</c>/<c>body</c>) que el elemento lee — con el texto,
/// el acordeón colocado hidrataba sin secciones (D1).
/// </summary>
public sealed class AccordionResolutorTests
{
    private const string Secciones =
        """[{"title":"Envíos","content":"De 2 a 5 días."},{"title":"Devoluciones","body":"30 días.","open":true}]""";

    private readonly ILogger<AccordionResolutor> _log = Substitute.For<ILogger<AccordionResolutor>>();

    private AccordionResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void Las_secciones_viajan_como_lista_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("itemsJson", Secciones),
            ("allowMultiple", true))).Props);

        Assert.Equal(new[] { "items", "allowMultiple" }, cable.Keys);
        var items = (JsonElement)cable["items"]!;
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("Envíos", items[0].GetProperty("title").GetString());
        Assert.Equal("De 2 a 5 días.", items[0].GetProperty("body").GetString());
        Assert.Equal("30 días.", items[1].GetProperty("body").GetString());
        Assert.False(items[1].TryGetProperty("open", out _));
        Assert.True(((JsonElement)cable["allowMultiple"]!).GetBoolean());
    }

    [Fact]
    public void Una_seccion_sin_titulo_o_un_json_roto_no_viajan_y_se_anotan()
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(
            ("itemsJson", """[{"content":"sin título"},{"title":"FAQ"}]"""),
            ("allowMultiple", false))).Props;
        var roto = Resolutor().Resolver(ElementoFalso.Con(("itemsJson", "[{\"title\":"))).Props;

        Assert.Equal(new[] { new AccordionSection("FAQ") }, props.Items);
        Assert.Null(props.AllowMultiple);
        Assert.Null(roto.Items);
        Assert.Equal(2, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("itemsJson", Secciones));

        Assert.Equal(Resolutor().Resolver(elemento).Props.Items, Resolutor().Resolver(elemento).Props.Items);
    }
}
