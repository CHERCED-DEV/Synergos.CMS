using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La caché del montaje local del CDN (ADR 0140, D1). Gemela de
/// <c>tools/lib/cdn-cache-policy.spec.mjs</c> del UI: la misma tabla de casos del runtime.
/// </summary>
/// <remarks>
/// El defecto que estos tests cierran no rompe un build ni sale en un log: el runtime se
/// republicaba bajo <c>runtime/angular/21.1.6/</c> —la versión de ANGULAR, no la del contenido—
/// y el montaje la servía <c>immutable</c> por tener forma de versión. Quien ya tenía el
/// <c>sg-shared.js</c> viejo se quedaba con él hasta un año. Por eso hay más casos de lo que NO
/// se cachea largo que de lo que sí.
/// </remarks>
public sealed class PoliticaDeCacheDelCdnLocalTests
{
    private const int MaxAge = 600;

    [Theory]
    // El runtime con la huella de sus bytes en el nombre: ése no se mueve nunca.
    [InlineData("/cdn-bundles/synergos/runtime/angular/21.1.6/sg-shared.18a9decbae7fffb0.js")]
    [InlineData("/cdn-bundles/synergos/runtime/preact/10.29.8/sg-preact-core.0123456789abcdef.js")]
    [InlineData("/cdn-bundles/synergos/runtime/angular/21.1.6/ng-core.0123456789abcdef.mjs")]
    // Un elemento en su versión exacta: como siempre.
    [InlineData("/cdn-bundles/synergos/eventos/angular/0.1.10/main.js")]
    public void Lo_que_prueba_su_contenido_es_inmutable_con_el_max_age_configurado(string ruta)
        => Assert.Equal(
            $"public, max-age={MaxAge}, immutable",
            PoliticaDeCacheDelCdnLocal.CacheControl(ruta, MaxAge));

    [Theory]
    // Los planos del runtime: la carpeta es la versión del framework y se reescriben.
    [InlineData("/cdn-bundles/synergos/runtime/angular/21.1.6/sg-shared.js")]
    [InlineData("/cdn-bundles/synergos/runtime/angular/21.1.6/import-map.json")]
    [InlineData("/cdn-bundles/synergos/runtime/preact/10.29.8/sg-preact-core.js")]
    // Lo que se mueve, con huella o sin ella.
    [InlineData("/cdn-bundles/synergos/runtime/angular/latest/import-map.json")]
    [InlineData("/cdn-bundles/synergos/runtime/angular/latest/huellas.json")]
    [InlineData("/cdn-bundles/synergos/runtime/angular/latest/sg-shared.18a9decbae7fffb0.js")]
    // Lo que PARECE una huella y no lo es: 15 hex, mayúsculas, otra extensión.
    [InlineData("/cdn-bundles/synergos/runtime/angular/21.1.6/sg-shared.18a9decbae7fffb.js")]
    [InlineData("/cdn-bundles/synergos/runtime/angular/21.1.6/sg-shared.18A9DECBAE7FFFB0.js")]
    [InlineData("/cdn-bundles/synergos/runtime/angular/21.1.6/x.0123456789abcdef.css")]
    // Los punteros de los elementos, como antes.
    [InlineData("/cdn-bundles/synergos/eventos/angular/v0/main.js")]
    [InlineData("/cdn-bundles/synergos/eventos/angular/latest/main.js")]
    [InlineData("/cdn-bundles/synergos/eventos/angular/V12/main.js")]
    public void Lo_que_se_mueve_o_no_prueba_su_contenido_se_revalida(string ruta)
        => Assert.Equal(
            "public, no-cache, must-revalidate",
            PoliticaDeCacheDelCdnLocal.CacheControl(ruta, MaxAge));

    /// <summary>
    /// Program.cs delega en la política y ya no decide en línea: si la regla volviera al
    /// <c>OnPrepareResponse</c>, esta tabla probaría una pieza que nadie llama.
    /// </summary>
    [Fact]
    public void El_montaje_del_CDN_delega_el_Cache_Control_en_la_politica()
    {
        var program = File.ReadAllText(RutaDelPrograma());

        Assert.Contains("PoliticaDeCacheDelCdnLocal.CacheControl(", program, StringComparison.Ordinal);
        Assert.DoesNotContain(@"@""/v\d+(/|$)""", program, StringComparison.Ordinal);
        Assert.DoesNotContain("isMutablePointer", program, StringComparison.Ordinal);
    }

    private static string RutaDelPrograma()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidato = Path.Combine(dir, "Synergos.CMS.Web", "Program.cs");
            if (File.Exists(candidato))
            {
                return candidato;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException("No se encontró Program.cs subiendo desde " + AppContext.BaseDirectory);
    }
}
