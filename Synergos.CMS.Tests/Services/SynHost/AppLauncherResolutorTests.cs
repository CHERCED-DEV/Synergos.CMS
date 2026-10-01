using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="AppLauncherResolutor"/>, la funcionalidad del piloto de la ADR 0136 (#186):
/// <c>heading</c>/<c>subheading</c> llegan como <c>title</c>/<c>subtitle</c>, el TEXTO <c>apps</c>
/// como lista tipada, y nada de microcopia ni de <c>configOverride</c> (una funcionalidad no lo
/// recibe, ADR 0135 §6).
/// </summary>
public sealed class AppLauncherResolutorTests
{
    private const string Apps =
        """[{"id":"tienda","name":"Tienda","tagline":"Catálogo y checkout","status":"live","url":"/tienda","icon":"bag","industry":"Retail","persona":"Comprador","capabilities":["Catálogo","Pagos"],"demoMode":"deeplink"},{"name":"Gobierno","capabilities":"Trámites, Citas"}]""";

    private readonly ILogger<AppLauncherResolutor> _log = Substitute.For<ILogger<AppLauncherResolutor>>();

    private AppLauncherResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void El_contenido_viaja_con_los_nombres_que_lee_el_elemento_y_las_apps_como_lista()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("heading", "Explora las apps"),
            ("subheading", "Un motor, muchos productos"),
            ("apps", Apps))).Props);

        Assert.Equal(new[] { "title", "subtitle", "apps" }, cable.Keys);
        var apps = (JsonElement)cable["apps"]!;
        Assert.Equal(2, apps.GetArrayLength());
        Assert.Equal("Tienda", apps[0].GetProperty("name").GetString());
        Assert.Equal("Retail", apps[0].GetProperty("industry").GetString());
        Assert.Equal(new[] { "Catálogo", "Pagos" }, apps[0].GetProperty("capabilities").EnumerateArray().Select(c => c.GetString()));
        // Las capacidades escritas con comas llegan como lista, que es lo que el elemento aceptaba.
        Assert.Equal(new[] { "Trámites", "Citas" }, apps[1].GetProperty("capabilities").EnumerateArray().Select(c => c.GetString()));
        Assert.False(apps[1].TryGetProperty("status", out _));
    }

    [Fact]
    public void Una_app_sin_nombre_no_viaja_y_se_anota()
    {
        var apps = Resolutor().Resolver(ElementoFalso.Con(
            ("apps", """[{"id":"x","tagline":"sin nombre"},{"name":"Blogs"}]"""))).Props.Apps;

        Assert.Equal(new[] { new AppDelLanzador("Blogs") }, apps);
        Assert.Equal(1, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Una_funcionalidad_no_recibe_configOverride_ni_la_microcopia_que_entraba_por_ahi()
    {
        // Antes `searchLabel`, `ctaLabel`… sólo podían llegar tecleándolos como JSON en el bloque.
        var solicitud = SolicitudSynHost.Para(
            Resolutor().Resolver(ElementoFalso.Con(("heading", "Apps"))),
            """{"title":"pisado","ctaLabel":"Abrir"}""",
            CultureInfo.GetCultureInfo("es-CO"));

        Assert.Null(solicitud.ConfigOverrideJson);
        Assert.Equal(new[] { "AppLauncher", "Common.States" }, solicitud.Diccionario);
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("apps", Apps));

        var una = Resolutor().Resolver(elemento).Props.Apps!;
        var otra = Resolutor().Resolver(elemento).Props.Apps!;
        Assert.Equal(
            JsonSerializer.Serialize(una, SolicitudSynHost.Cable),
            JsonSerializer.Serialize(otra, SolicitudSynHost.Cable));
    }
}
