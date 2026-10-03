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
        var inicio = UmbracoEventCatalogSource.InicioEnLaZona(new DateTime(2026, 8, 15, 14, 0, 0), Zona("America/Bogota"));

        Assert.Equal(new DateTimeOffset(2026, 8, 15, 19, 0, 0, TimeSpan.Zero), inicio);
    }

    [Theory] // el desfase sale de la zona Y de la fecha: +2 en verano, +1 en invierno.
    [InlineData(7, 18)]
    [InlineData(12, 19)]
    public void En_Madrid_el_desfase_depende_de_la_fecha(int mes, int horaUtc)
    {
        var inicio = UmbracoEventCatalogSource.InicioEnLaZona(new DateTime(2026, mes, 10, 20, 0, 0), Zona("Europe/Madrid"));

        Assert.Equal(new DateTimeOffset(2026, mes, 10, horaUtc, 0, 0, TimeSpan.Zero), inicio);
    }

    [Fact] // una hora que el cambio de hora se salta no tumba la agenda.
    public void Una_hora_que_no_existe_no_lanza()
    {
        // 29-mar-2026, de 02:00 a 03:00 no existe en Madrid.
        var inicio = UmbracoEventCatalogSource.InicioEnLaZona(new DateTime(2026, 3, 29, 2, 30, 0), Zona("Europe/Madrid"));

        Assert.Equal(new DateTime(2026, 3, 29), inicio.UtcDateTime.Date);
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

        Assert.Contains("InicioEnLaZona(start, Zona)", fuente, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeSpan.FromHours(", fuente, StringComparison.Ordinal);
    }
}
