using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Composers;

namespace Synergos.CMS.Tests.Composers;

/// <summary>
/// Qué palabra enciende la analítica de búsqueda contra <c>Api.Sessions</c>, y qué pasa con una
/// que no se reconoce (#177).
/// </summary>
/// <remarks>
/// El despliegue escribía <c>Http</c>, el composer reconocía <c>Sessions</c> y cualquier otra
/// palabra caía EN SILENCIO al disco: en producción la capacidad no recibía ni un evento. La
/// decisión es un método con nombre para poder mirarla sin componer; la composición de verdad la
/// mira <c>ModosDelComposeTests</c>.
/// </remarks>
public sealed class ModoDeAnaliticaDeBusquedaTests
{
    [Theory] // vacío: sin configurar, el default del POCO — el camino del clon limpio
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sin_configurar_es_el_default_del_POCO(string? configurado)
    {
        Assert.Equal(new SearchAnalyticsSettings().Mode, SeamComposer.ModoDeAnaliticaDeBusqueda(configurado));
        Assert.Equal("FileSystem", SeamComposer.ModoDeAnaliticaDeBusqueda(configurado));
    }

    [Theory] // feliz: las dos palabras, sin importar mayúsculas ni espacios, devuelven la canónica
    [InlineData("Api", "Api")]
    [InlineData("api", "Api")]
    [InlineData(" API ", "Api")]
    [InlineData("FileSystem", "FileSystem")]
    [InlineData("filesystem", "FileSystem")]
    public void Las_palabras_del_modo_se_reconocen_en_su_forma_canonica(string configurado, string esperado)
        => Assert.Equal(esperado, SeamComposer.ModoDeAnaliticaDeBusqueda(configurado));

    [Theory] // filtro: la del compose viejo, la del composer viejo y una cualquiera NO caen al disco
    [InlineData("Http")]
    [InlineData("Sessions")]
    [InlineData("Bff")]
    [InlineData("Stub")]
    public void Un_modo_desconocido_lanza_nombrando_los_validos(string configurado)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SeamComposer.ModoDeAnaliticaDeBusqueda(configurado));

        Assert.Contains("Synergos:SearchAnalytics:Mode", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"'{configurado}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("FileSystem", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Api", ex.Message, StringComparison.Ordinal);
    }

    [Fact] // idempotente: el default del POCO es uno de los modos que se reconocen
    public void El_default_del_POCO_es_un_modo_reconocido()
        => Assert.Contains(new SearchAnalyticsSettings().Mode, SeamComposer.ModosDeAnaliticaDeBusqueda);
}
