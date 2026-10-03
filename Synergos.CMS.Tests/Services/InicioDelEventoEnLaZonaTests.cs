using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services.Catalog;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La hora de inicio que el editor tecleó en un <c>eventPage</c> se ancla en la zona del SITIO
/// (<c>Synergos:Listados:ZonaHoraria</c>), no en un UTC−5 escrito a mano.
/// </summary>
/// <remarks>
/// <para><c>Umbraco.DateTime</c> guarda la fecha sin zona y el editor teclea la hora local.
/// <c>UmbracoEventCatalogSource</c> la anclaba con un <c>TimeSpan.FromHours(-5)</c> fijo: correcto
/// para Colombia, que no tiene horario de verano, y equivocado para cualquier otro sitio aunque su
/// configuración dijera otra zona —la que ya leen los listados—. Ahora es la misma pieza para los
/// dos.</para>
///
/// <para><b>El fixture lleva una zona CON horario de verano</b> (Madrid): un desfase fijo, sea el que
/// sea, acierta como mucho una de las dos fechas.</para>
/// </remarks>
public sealed class InicioDelEventoEnLaZonaTests
{
    private static TimeZoneInfo Zona(string iana) => new ListadosSettings { ZonaHoraria = iana }.Zona()!;

    [Fact] // happy: las 14:00 que teclea el editor en Bogotá son las 19:00Z.
    public void En_Bogota_las_14_son_las_19_UTC()
    {
        var inicio = EventContentRules.InicioEnLaZona(new DateTime(2026, 8, 15, 14, 0, 0), Zona("America/Bogota"));

        Assert.Equal(new DateTimeOffset(2026, 8, 15, 19, 0, 0, TimeSpan.Zero), inicio);
    }

    [Theory] // el desfase sale de la zona Y de la fecha: +2 en verano, +1 en invierno.
    [InlineData(7, 18)]
    [InlineData(12, 19)]
    public void En_Madrid_el_desfase_depende_de_la_fecha(int mes, int horaUtc)
    {
        var inicio = EventContentRules.InicioEnLaZona(new DateTime(2026, mes, 10, 20, 0, 0), Zona("Europe/Madrid"));

        Assert.Equal(new DateTimeOffset(2026, mes, 10, horaUtc, 0, 0, TimeSpan.Zero), inicio);
    }

    [Fact] // una hora que el cambio de hora se salta no tumba la agenda.
    public void Una_hora_que_no_existe_no_lanza()
    {
        // 29-mar-2026, de 02:00 a 03:00 no existe en Madrid.
        var inicio = EventContentRules.InicioEnLaZona(new DateTime(2026, 3, 29, 2, 30, 0), Zona("Europe/Madrid"));

        Assert.Equal(new DateTime(2026, 3, 29), inicio.UtcDateTime.Date);
    }

    [Theory] // una hora que se repite (el reloj se atrasa) es la de la PRIMERA vez: el desfase mayor.
    [InlineData(0, 4)]
    [InlineData(30, 4)]
    public void Una_hora_que_se_repite_es_la_primera_vez(int minuto, int horaUtc)
    {
        // 1-nov-2026: La Habana pasa de UTC−4 a UTC−5 a la 01:00, así que de 00:00 a 01:00 hay dos.
        var inicio = EventContentRules.InicioEnLaZona(new DateTime(2026, 11, 1, 0, minuto, 0), Zona("America/Havana"));

        Assert.Equal(new DateTimeOffset(2026, 11, 1, horaUtc, minuto, 0, TimeSpan.Zero), inicio);
    }

    [Fact] // las 23:00 del 31-12-9999 en Bogotá son el año 10000 en UTC: no caben, y no se lanza.
    public void Una_hora_que_en_UTC_no_cabe_no_lanza_y_dice_que_no()
    {
        var bogota = Zona("America/Bogota");

        Assert.False(EventContentRules.TryInicioEnLaZona(new DateTime(9999, 12, 31, 23, 0, 0), bogota, out _));
        Assert.False(EventContentRules.TryInicioEnLaZona(new DateTime(1, 1, 1), Zona("Asia/Tokyo"), out _));
        Assert.True(EventContentRules.TryInicioEnLaZona(new DateTime(9999, 12, 31, 18, 0, 0), bogota, out var cabe));
        Assert.Equal(new DateTimeOffset(9999, 12, 31, 23, 0, 0, TimeSpan.Zero), cabe);
    }

    [Fact] // la zona es la de la configuración del sitio.
    public void La_fuente_lee_la_zona_de_la_configuracion()
    {
        var listados = Substitute.For<IOptionsMonitor<ListadosSettings>>();
        listados.CurrentValue.Returns(new ListadosSettings { ZonaHoraria = "Asia/Tokyo" });

        var fuente = new UmbracoEventCatalogSource(
            Substitute.For<IUmbracoContextAccessor>(),
            Substitute.For<IOptionsMonitor<CatalogSettings>>(),
            listados,
            NullLogger<UmbracoEventCatalogSource>.Instance);

        Assert.Equal(Zona("Asia/Tokyo").Id, fuente.Zona.Id);
    }

    [Fact] // …y la ficha del evento la usa: no queda un desfase escrito a mano.
    public void La_ficha_se_ancla_con_la_zona_y_no_con_un_desfase_fijo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);

        var fuente = string.Join('\n', File.ReadAllLines(Path.Combine(
                dir!.FullName, "Synergos.CMS.Web", "Services", "Catalog", "UmbracoEventCatalogSource.cs"))
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        Assert.Contains("TryInicioEnLaZona(start, Zona, out", fuente, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeSpan.FromHours(", fuente, StringComparison.Ordinal);
    }

    /// <summary>
    /// Los dos días de la ventana de venta (#195): el schema los declara como fecha SIN hora y la
    /// fuente los lee por el mismo alias.
    /// </summary>
    /// <remarks>
    /// El alias se escribe dos veces —en el XML y en el C#— y ningún compilador cruza el eslabón: un
    /// alias mal escrito en la fuente no lanza, devuelve null, y la localidad se vendería sin
    /// ventana con la tarjeta diciendo lo contrario (<c>feedback_every_authored_field_needs_a_reader</c>).
    /// </remarks>
    [Theory]
    [InlineData("tierSaleOpens")]
    [InlineData("tierSaleCloses")]
    public void La_ventana_que_autora_el_editor_es_la_que_lee_la_fuente(string alias)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);

        var schema = System.Xml.Linq.XDocument.Load(Path.Combine(
            dir!.FullName, "Synergos.CMS.Web", "uSync", "v9", "ContentTypes", "elementeventtier.config"));
        var propiedad = Assert.Single(schema.Descendants("GenericProperty"), p => (string?)p.Element("Alias") == alias);
        Assert.Equal("Umbraco.DateTime", (string?)propiedad.Element("Type"));
        // El DataType «Date Picker» de serie: sólo día. Con hora, el editor elegiría un instante
        // en una zona que no ve.
        Assert.Equal("5046194e-4237-453c-a547-15db3a07c4e1", (string?)propiedad.Element("Definition"));
        Assert.Equal("Nothing", (string?)propiedad.Element("Variations"));

        var fuente = File.ReadAllText(Path.Combine(
            dir.FullName, "Synergos.CMS.Web", "Services", "Catalog", "UmbracoEventCatalogSource.cs"));
        Assert.Contains($"FechaDelEditor(\"{alias}\")", fuente, StringComparison.Ordinal);
    }
}
