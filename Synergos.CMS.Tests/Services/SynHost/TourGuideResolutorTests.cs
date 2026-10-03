using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="TourGuideResolutor"/>: el TEXTO <c>stepsJson</c> llega como la LISTA
/// <c>steps</c> (<c>selector</c> → <c>target</c>, <c>content</c> → <c>body</c>) — con el alias el
/// recorrido no tenía pasos (D1) — y <c>autoStart</c> sólo viaja encendido.
/// </summary>
public sealed class TourGuideResolutorTests
{
    private const string Pasos =
        """[{"selector":".site-header","title":"Bienvenido","content":"Este es el menú principal."},{"title":"Listo"}]""";

    private readonly ILogger<TourGuideResolutor> _log = Substitute.For<ILogger<TourGuideResolutor>>();

    private TourGuideResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(("autoStart", false))).Props));
    }

    [Fact]
    public void Los_pasos_viajan_como_lista_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("stepsJson", Pasos),
            ("autoStart", true))).Props);

        Assert.Equal(new[] { "steps", "autoStart" }, cable.Keys);
        var steps = (JsonElement)cable["steps"]!;
        Assert.Equal(".site-header", steps[0].GetProperty("target").GetString());
        Assert.Equal("Este es el menú principal.", steps[0].GetProperty("body").GetString());
        Assert.False(steps[0].TryGetProperty("selector", out _));
        Assert.False(steps[0].TryGetProperty("content", out _));
        Assert.False(steps[1].TryGetProperty("target", out _));
        Assert.True(((JsonElement)cable["autoStart"]!).GetBoolean());
    }

    [Fact]
    public void Un_paso_sin_titulo_ni_texto_no_viaja_y_se_anota()
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(
            ("stepsJson", """[{"selector":".x"},{"selector":".y","content":"Sólo texto"}]"""))).Props;

        Assert.Equal(new[] { new TourGuideStep(".y", null, "Sólo texto") }, props.Steps);
        Assert.Equal(1, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("stepsJson", Pasos), ("autoStart", true));

        Assert.Equal(Resolutor().Resolver(elemento).Props.Steps, Resolutor().Resolver(elemento).Props.Steps);
    }

    // #192, caso 14: el elemento pinta el lado de cada paso y el record no lo traía.
    [Fact]
    public void El_lado_de_un_paso_viaja_en_minusculas()
    {
        var paso = Resolutor().Resolver(ElementoFalso.Con(
            ("stepsJson", """[{"selector":".menu","title":"Menú","placement":"Bottom"}]"""))).Props.Steps!.Single();

        Assert.Equal("bottom", paso.Placement);
    }
}
