using Microsoft.Extensions.Configuration;
using Synergos.CMS.Web.Composers;

namespace Synergos.CMS.Tests.Composers;

/// <summary>
/// La pieza que lee y valida el modo de un interruptor de despliegue (#182): sin configurar es el
/// default, una palabra conocida vuelve en su forma canónica y una desconocida LANZA nombrando las
/// válidas.
/// </summary>
/// <remarks>
/// <para>Nació del #177, que validaba UN interruptor —la analítica de búsqueda— con un método
/// propio; los otros catorce caían en silencio a su camino por defecto con cualquier palabra que
/// no fuera la que encendía. Medido componiendo antes de esta pieza: <c>Htpp</c>, <c>Api</c> en
/// uno que dice <c>Bff</c>, <c>Stub</c> en uno que dice <c>Local</c> y <c>" Api "</c> con espacios
/// cableaban EXACTAMENTE lo mismo que no configurar nada.</para>
///
/// <para>Estos tests miran la regla sin componer. Que los quince la USEN —y que el dieciséis no
/// pueda saltársela— lo mira <c>ModosDelComposeTests</c>, componiendo de verdad.</para>
/// </remarks>
public sealed class InterruptorTests
{
    private const string Clave = "Synergos:Tienda:Mode";

    private static IConfiguration Config(string? valor)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [Clave] = valor })
            .Build();

    [Theory] // vacío: sin configurar es el default — el camino del clon limpio
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sin_configurar_es_el_default(string? configurado)
    {
        Assert.Equal("Stub", Interruptor.Modo(configurado, Clave, "Stub", "Bff"));
        Assert.False(Interruptor.Encendido(configurado, Clave, "Bff", "Stub"));
        Assert.False(Interruptor.Encendido(Config(configurado), Clave, "Bff", "Stub"));
    }

    [Theory] // feliz: mayúsculas y espacios no cuentan, y vuelve SIEMPRE la palabra canónica
    [InlineData("Bff", "Bff")]
    [InlineData("bff", "Bff")]
    [InlineData(" BFF ", "Bff")]
    [InlineData("Stub", "Stub")]
    [InlineData("stub", "Stub")]
    public void Las_palabras_se_reconocen_en_su_forma_canonica(string configurado, string esperado)
        => Assert.Equal(esperado, Interruptor.Modo(configurado, Clave, "Stub", "Bff"));

    [Fact] // feliz: la forma de catorce de los quince — dos palabras, una enciende
    public void Encendido_es_estar_en_la_palabra_que_enciende()
    {
        Assert.True(Interruptor.Encendido(Config("bff"), Clave, "Bff", "Stub"));
        Assert.False(Interruptor.Encendido(Config("Stub"), Clave, "Bff", "Stub"));
    }

    [Fact] // feliz: la forma del CDN — tres palabras
    public void Un_interruptor_de_tres_palabras_las_reconoce_todas()
    {
        const string cdn = "Synergos:BundleRegistry:Mode";
        Assert.Equal("Stub", Interruptor.Modo((string?)null, cdn, "Stub", "FileSystem", "Http"));
        Assert.Equal("FileSystem", Interruptor.Modo("filesystem", cdn, "Stub", "FileSystem", "Http"));
        Assert.Equal("Http", Interruptor.Modo("HTTP", cdn, "Stub", "FileSystem", "Http"));
    }

    [Theory] // filtro: la errata, la palabra de OTRO interruptor y la vieja NO caen al default
    [InlineData("Htpp")]
    [InlineData("Api")]
    [InlineData("Local")]
    [InlineData("Http")]
    [InlineData("Sessions")]
    public void Un_modo_desconocido_lanza_nombrando_los_validos(string configurado)
    {
        var ex = Assert.Throws<ModoDesconocidoException>(() => Interruptor.Encendido(Config(configurado), Clave, "Bff", "Stub"));

        Assert.Equal(Clave, ex.Clave);
        Assert.Equal(configurado, ex.Configurado);
        Assert.Equal(["Stub", "Bff"], ex.Validos);

        // El mensaje es lo que lee el operador en el log del arranque: la clave como la escribe
        // él en el entorno, lo que puso y lo que podía poner.
        Assert.Contains("Synergos:Tienda:Mode", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Synergos__Tienda__Mode", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"'{configurado}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Stub, Bff", ex.Message, StringComparison.Ordinal);
    }

    [Fact] // filtro: es una InvalidOperationException — lo que el arranque ya trata como configuración rota
    public void El_rechazo_es_el_de_una_configuracion_invalida()
        => Assert.IsAssignableFrom<InvalidOperationException>(
            Record.Exception(() => Interruptor.Modo("Htpp", Clave, "Stub", "Bff")));

    [Theory] // idempotente: leer lo que ya se leyó da lo mismo
    [InlineData(" bff ")]
    [InlineData(null)]
    public void Leer_la_palabra_canonica_otra_vez_da_la_misma(string? configurado)
    {
        var una = Interruptor.Modo(configurado, Clave, "Stub", "Bff");
        Assert.Equal(una, Interruptor.Modo(una, Clave, "Stub", "Bff"));
    }
}
