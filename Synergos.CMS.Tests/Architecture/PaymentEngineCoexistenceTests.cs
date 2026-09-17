using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Synergos.CMS.Web.Composers;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Que sólo una de las dos mitades cobre de verdad (HU #27).
/// </summary>
/// <remarks>
/// <para><b>Desde la HU #27 la plata la mueve <c>Api.Payments</c>.</b> El motor en proceso del
/// CMS (ADR 0116) sigue vivo porque es lo que permite levantar el repo entero sin ningún
/// servicio, pero con <c>Tienda:Mode=Bff</c> la plata la tiene el orquestador — y dejar las dos
/// plomerías cobrando hace que un mismo cobro pase por proveedores distintos según un
/// interruptor.</para>
///
/// <para><b>Eso ya ocurrió y por eso el gate no es hipotético</b>: es el defecto #57, donde el
/// RMA le pedía el reembolso al proveedor local con el identificador de la saga —que ese
/// proveedor no conocía— y el caso no llegaba nunca a reembolsado <i>sin que nada fallara</i>.</para>
///
/// <para><b>Y falla al CABLEAR, no en la primera petición.</b> Es la forma de #56 (el modo
/// <c>Http</c> del CDN sin URL) y la de la llave de firma de <c>Api.Identity</c>: un despliegue
/// que arranca verde, contesta <c>/health</c> y pasa la prueba de humo <b>parece uno bueno</b>, y
/// lo desmiente la primera persona que intenta pagar. De los tres modos de fallo, ése es el
/// peor.</para>
/// </remarks>
public sealed class PaymentEngineCoexistenceTests
{
    private static IConfiguration Config(params (string Clave, string Valor)[] valores)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(valores.Select(v => new KeyValuePair<string, string?>(v.Clave, v.Valor)))
            .Build();

    private const string Publica = "Synergos:Payments:WompiPublicKey";
    private const string Integridad = "Synergos:Payments:WompiIntegritySecret";
    private const string Proveedor = "Synergos:Payments:Provider";
    private const string Router = "Synergos:Payments:Routing:Enabled";
    private const string Tienda = "Synergos:Tienda:Mode";
    private const string Modo = "Synergos:Payments:Mode";

    [Fact]
    public void Con_la_tienda_cableada_Y_llaves_reales_acá_el_arranque_FALLA()
    {
        // La mala configuración que tiene que gritar: los dos lados sabiendo cobrar.
        var malo = Assert.Throws<InvalidOperationException>(() => SeamComposer.ExigirUnaSolaPlomeria(
            Config((Tienda, "Bff"), (Publica, "pub_prod_real"), (Integridad, "prod_integrity"), (Proveedor, "Wompi"))));

        // El mensaje tiene que nombrar el remedio, no sólo el síntoma: un fallo de arranque que
        // no dice qué mover deja al operador leyendo código.
        Assert.Contains("Api.Payments", malo.Message, StringComparison.Ordinal);
        Assert.Contains("Synergos:Tienda:Mode=Stub", malo.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void El_ROUTER_encendido_cuenta_igual_que_el_proveedor_único()
    {
        // Con el router, Wompi entra como MIEMBRO en cuanto tiene llaves: una regla lo puede
        // elegir sin que `Provider` diga su nombre en ninguna parte. Un gate que sólo mirara
        // `Provider` dejaría esa puerta abierta — y es la puerta que el propio composer usa.
        Assert.Throws<InvalidOperationException>(() => SeamComposer.ExigirUnaSolaPlomeria(
            Config((Tienda, "Bff"), (Publica, "pub_prod_real"), (Integridad, "prod_integrity"), (Router, "true"))));
    }

    [Fact]
    public void Con_el_SEAM_contra_la_capacidad_unas_llaves_aqui_tampoco_valen()
    {
        // #27, la parte que quedó viva. Con Synergos:Payments:Mode=Api este lado NO COBRA: el
        // seam se lo pide a Api.Payments. Unas llaves de Wompi aquí no cobrarían nada — se
        // quedarían calladas, y una config que parece hacer algo y no hace nada es el mismo
        // defecto de `Provider=Wompi` sirviendo el stub, sólo que al revés.
        var malo = Assert.Throws<InvalidOperationException>(() => SeamComposer.ExigirUnaSolaPlomeria(
            Config((Modo, "Api"), (Publica, "pub_prod_real"), (Integridad, "prod_integrity"), (Proveedor, "Wompi"))));

        Assert.Contains("Api.Payments", malo.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void El_camino_sin_servicios_sigue_intacto()
    {
        // Es el que permite levantar el repo entero sin levantar nada más. Si esto fallara, el
        // gate habría convertido una protección en un estorbo.
        SeamComposer.ExigirUnaSolaPlomeria(
            Config((Tienda, "Stub"), (Publica, "pub_prod_real"), (Integridad, "prod_integrity"), (Proveedor, "Wompi")));
    }

    [Fact]
    public void Un_clon_limpio_con_la_tienda_cableada_arranca()
    {
        // Sin llaves de este lado no se cobra acá pase lo que pase, así que no hay conflicto.
        SeamComposer.ExigirUnaSolaPlomeria(Config((Tienda, "Bff"), (Proveedor, "Wompi")));
    }

    [Fact]
    public void Media_credencial_no_cobra_y_por_tanto_no_choca()
    {
        // `TryBuildWompi` exige LAS DOS llaves del checkout: con una sola devuelve null y este
        // lado degrada al stub. Reventar ahí sería fallar por una configuración que no cobra —y
        // un gate que grita sin motivo se acaba apagando.
        SeamComposer.ExigirUnaSolaPlomeria(
            Config((Tienda, "Bff"), (Publica, "pub_prod_real"), (Proveedor, "Wompi")));
    }

    [Fact]
    public void Las_llaves_puestas_SIN_que_nadie_las_pueda_elegir_no_chocan()
    {
        // Provider=Stub y router apagado: las llaves están pero no hay rama que construya Wompi.
        SeamComposer.ExigirUnaSolaPlomeria(
            Config((Tienda, "Bff"), (Publica, "pub_prod_real"), (Integridad, "prod_integrity"), (Proveedor, "Stub")));
    }

    /// <summary>
    /// Que la comprobación esté ENCHUFADA, no sólo escrita.
    /// </summary>
    /// <remarks>
    /// Es la lección que <c>Api.Notifications</c> pagó mutando su verificador de webhook: quitó
    /// la llamada del lambda y <b>no falló ni un test</b>, porque los suyos probaban la pieza y
    /// ninguno probaba que alguien la llamara. Los seis de arriba tienen exactamente esa forma —
    /// prueban el método, no el cableado.
    /// </remarks>
    [Fact]
    public void La_comprobacion_se_LLAMA_desde_el_composer()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);

        var fuente = File.ReadAllText(Path.Combine(
            dir!.FullName, "Synergos.CMS.Web", "Composers", "SeamComposer.PaymentEngine.cs"));

        // Sin comentarios: la prosa que documenta la regla la nombra, y un gate que lea prosa no
        // mide nada (#29 y el de #33a, dos veces en este repo).
        var sinComentarios = string.Join('\n', fuente
            .Split('\n')
            .Select(l => l.TrimStart().StartsWith("//", StringComparison.Ordinal)
                      || l.TrimStart().StartsWith("*", StringComparison.Ordinal)
                      || l.TrimStart().StartsWith("/*", StringComparison.Ordinal)
                ? string.Empty
                : (l.IndexOf("//", StringComparison.Ordinal) is var i && i >= 0 ? l[..i] : l)));

        Assert.True(
            Regex.IsMatch(sinComentarios, @"^\s*ExigirUnaSolaPlomeria\(", RegexOptions.Multiline),
            "ComposePaymentEngine ya no llama a ExigirUnaSolaPlomeria. Escrita y no llamada, la "
            + "comprobación no impide nada: el despliegue con las dos plomerías arranca verde, "
            + "contesta /health y revienta cuando una persona intenta pagar.");
    }
}
