using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El modo <c>Http</c> del registry <b>no arranca sin URL base</b> (#56).
/// </summary>
/// <remarks>
/// <para><b>El defecto que evita.</b> <c>CLAUDE.md</c> §11 manda encender el CDN con dos
/// variables. Con <c>SYNERGOS_CDN_MODE=Http</c> puesta y <c>SYNERGOS_CDN_URL</c> olvidada, el
/// compose pasa <c>PublicBaseUrl</c> <b>presente y vacía</b> —no ausente—, así que pisa el default
/// <c>/cdn-bundles</c>; la URL sale relativa y el <c>HttpClient</c> del registry no tiene
/// <c>BaseAddress</c>. Un default protege de la ausencia, no del vacío.</para>
///
/// <para><b>Y el arranque quedaba verde</b>: el warmup sólo resuelve el cliente y atrapa
/// <c>Exception</c> entera, así que el contenedor subía, <c>/health</c> contestaba y la prueba de
/// humo pasaba. Es la tercera vez que este repo se corta con la misma forma — la llave de firma de
/// <c>Api.Identity</c> (HU #14, rebanada 3) y el <c>TimeProvider</c> de la ADR 0132 fueron las
/// otras dos.</para>
///
/// <para><b>Por qué la regla vive en <c>BundleRegistrySettings</c> y no en el composer.</b> Una
/// regla dentro del lambda del cableado no se puede probar sin levantar el host — es lo que costó
/// una vuelta en <c>BookingController</c> (#36) y en la emisión de tokens (#14, rebanada 2). Acá
/// se prueba el comportamiento directamente, y aparte se comprueba que el composer la llame.</para>
/// </remarks>
public sealed class BundleRegistrySetupTests
{
    private static string Composer()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);

        var ruta = Path.Combine(dir!.FullName, "Synergos.CMS.Web", "Composers", "SeamComposer.Platform.cs");
        Assert.True(File.Exists(ruta), $"No existe {ruta}: revisar este gate.");
        return File.ReadAllText(ruta);
    }

    [Theory] // el caso del ticket es el primero: presente y vacía, que es lo que manda el compose
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Sin_URL_base_el_modo_Http_no_se_cablea(string? url)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => BundleRegistrySettings.ExigirUrlPublicaAbsoluta(url));

        Assert.Contains("SYNERGOS_CDN_URL", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Una relativa tampoco vale, y el default lo es.</summary>
    /// <remarks>
    /// <c>/cdn-bundles</c> es el default de la clase y es el <b>correcto</b> para
    /// <c>FileSystem</c>, donde lo sirve el propio sitio. En <c>Http</c> no se puede pedir: el
    /// cliente no tiene <c>BaseAddress</c>. Por eso se exige absoluta sólo en este modo.
    /// </remarks>
    [Theory]
    [InlineData("/cdn-bundles")]
    [InlineData("synergos-ui.example.com")]
    [InlineData("ftp://cdn.example.com")]
    public void Una_URL_que_no_es_http_absoluta_tampoco_vale(string url)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => BundleRegistrySettings.ExigirUrlPublicaAbsoluta(url));

        Assert.Contains(url, ex.Message, StringComparison.Ordinal);
    }

    [Theory] // happy: lo que el despliegue pone de verdad
    [InlineData("https://synergos-ui.synergos-labs.workers.dev")]
    [InlineData("https://cdn.example.com/")]
    [InlineData("http://localhost:8080")]
    public void Una_URL_http_absoluta_pasa(string url)
        => BundleRegistrySettings.ExigirUrlPublicaAbsoluta(url);

    /// <summary>
    /// El composer la llama, y <b>antes</b> de registrar el cliente.
    /// </summary>
    /// <remarks>
    /// El orden es la mitad del arreglo: validar después de <c>AddHttpClient</c> seguiría
    /// registrando un cliente inservible, y validar dentro del lambda de configuración volvería a
    /// ser perezoso — que es exactamente el defecto de #14 rebanada 3, donde el fallo llegaba
    /// tarde y con el despliegue ya dado por bueno.
    /// </remarks>
    [Fact]
    public void El_composer_valida_en_la_rama_Http_y_antes_de_registrar_el_cliente()
    {
        var codigo = Composer();

        var http = codigo.IndexOf("\"Http\", StringComparison.OrdinalIgnoreCase", StringComparison.Ordinal);
        Assert.True(http > 0, "Cambió la forma del composer: revisar este gate.");

        var cierre = codigo.IndexOf("StubBundleRegistryClient", http, StringComparison.Ordinal);
        Assert.True(cierre > http, "No se encontró el final de la rama Http: revisar este gate.");

        var rama = codigo[http..cierre];

        var valida = rama.IndexOf("ExigirUrlPublicaAbsoluta", StringComparison.Ordinal);
        Assert.True(valida >= 0,
            "La rama Http del registry no valida PublicBaseUrl. Sin eso, un despliegue con "
            + "SYNERGOS_CDN_MODE=Http y sin SYNERGOS_CDN_URL arranca verde y no renderiza ningún "
            + "<synergos-*> (#56).");

        var registra = rama.IndexOf("AddHttpClient", StringComparison.Ordinal);
        Assert.True(registra >= 0, "La rama Http ya no registra el cliente: revisar este gate.");

        Assert.True(valida < registra,
            "La validación de PublicBaseUrl va DESPUÉS de registrar el cliente. Tiene que ir "
            + "antes: si no, se registra un cliente que no puede pedir nada y el fallo vuelve a "
            + "llegar en el primer render.");
    }
}
