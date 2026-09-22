using Microsoft.Extensions.Configuration;
using Synergos.CMS.Web.Composers;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El secreto de firma de las entradas cambió de sección en el #154, y un despliegue que siga
/// poblando la vieja <b>no arranca</b>.
/// </summary>
/// <remarks>
/// <para><b>Por qué esto necesita test propio y no basta el gate de cableado.</b> El gate
/// (<c>SeccionesDeConfiguracionTests</c>) comprueba que la llamada está en el composer; lo que
/// nadie había visto es al guardia <b>lanzando</b>. Un rechazo escrito y nunca visto fallar es lo
/// que <c>feedback_mutate_every_gate</c> describe: no está vigilando nada.</para>
///
/// <para><b>Y el caso que importa no es «la clave está»: es «la clave tiene VALOR».</b> Un
/// <c>.env</c> de ejemplo con la línea vacía es lo normal —así está en <c>.env.example</c>— y
/// parar por eso convertiría un arranque bueno en rojo. Lo que hay que rechazar es un valor que
/// alguien puso creyendo que se leía.</para>
/// </remarks>
public class SeccionRetiradaDeEntradasTests
{
    private static IConfiguration Config(params (string Clave, string? Valor)[] pares)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(pares.Select(p => new KeyValuePair<string, string?>(p.Clave, p.Valor)))
            .Build();

    [Fact]
    public void La_seccion_retirada_con_valor_NO_arranca()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            OptionsComposer.ExigirQueNadieUseLaSeccionRetirada(
                Config(("Synergos:Events:TicketSigningSecret", "un-secreto-de-antes"))));

        // El mensaje tiene que decir el nombre NUEVO: un rojo que sólo dice «esto ya no va» manda
        // a alguien a buscar en el árbol dónde fue.
        Assert.Contains("Synergos:Eventos:Ticket:SigningSecret", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Synergos:Events:TicketSigningSecret", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cualquier_clave_de_la_seccion_retirada_cuenta_y_no_solo_la_que_se_movio()
    {
        // La sección ENTERA quedó retirada, así que un typo que no se le ocurra a nadie hoy
        // —`Synergos__Events__ApiKey`, el que dejaría al cliente del eje 2 sin llave— tiene que
        // parar igual. Enumerar sólo el nombre que se movió dejaría ése pasando.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            OptionsComposer.ExigirQueNadieUseLaSeccionRetirada(
                Config(("Synergos:Events:ApiKey", "clave-que-nadie-lee"))));

        Assert.Contains("Synergos:Events:ApiKey", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void La_vecina_PLANA_bajo_la_seccion_buena_tambien_para()
    {
        // Ésta es la que un barrido de la sección retirada NO ve: vive bajo `Synergos:Eventos`,
        // que es la sección correcta, y es exactamente lo que teclea quien lee un documento viejo
        // y corrige la letra que falta.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            OptionsComposer.ExigirQueNadieUseLaSeccionRetirada(
                Config(("Synergos:Eventos:TicketSigningSecret", "corregí-la-letra-y-sigue-mal"))));

        Assert.Contains("Synergos:Eventos:TicketSigningSecret", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Una_clave_VACIA_no_para_el_arranque()
    {
        // `.env.example` deja las dos líneas vacías a propósito, y docker-compose las pasa
        // presentes-y-vacías. Parar por eso sería un gate que enseña a ignorarse.
        OptionsComposer.ExigirQueNadieUseLaSeccionRetirada(
            Config(("Synergos:Events:TicketSigningSecret", string.Empty),
                   ("Synergos:Eventos:TicketSigningSecret", "   ")));
    }

    [Fact]
    public void La_seccion_NUEVA_no_para_nada()
    {
        OptionsComposer.ExigirQueNadieUseLaSeccionRetirada(
            Config(("Synergos:Eventos:Ticket:SigningSecret", "el-bueno"),
                   ("Synergos:Eventos:Mode", "Bff")));
    }
}
