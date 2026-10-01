using System.Text.Json;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La pieza que pasa las URLs de un import map publicado a la base pública del CDN (#189). El mapa
/// viaja EN LÍNEA dentro de la página, así que lo que no se reubica lo resuelve el navegador contra
/// el origen del CMS: 404, y nada hidrata.
/// </summary>
public sealed class ReubicacionDelImportMapTests
{
    private const string Mapa = "/synergos/runtime/angular/latest/import-map.json";

    /// <summary>El mismo fichero del runtime, escrito como lo publica cada camino.</summary>
    public static TheoryData<string, string> LasTresFormas => new()
    {
        // publish:runtime sin --base — el CDN de la máquina del arquitecto.
        { "absoluta", "https://synergos-static-local/synergos/runtime/angular/21.1.6/ng-core.js" },
        // build:cdn, con --base=/synergos — el artefacto que se despliega.
        { "relativa a la raíz", "/synergos/runtime/angular/21.1.6/ng-core.js" },
        // relativa al propio mapa, que vive en latest/.
        { "relativa", "../21.1.6/ng-core.js" },
    };

    [Theory]
    [MemberData(nameof(LasTresFormas))]
    public void Las_tres_formas_del_mismo_fichero_llegan_a_la_misma_url_bajo_cdn_bundles(string forma, string publicada)
    {
        Assert.True(
            ReubicacionDelImportMap.Reubicar(publicada, "/cdn-bundles", Mapa)
                == "/cdn-bundles/synergos/runtime/angular/21.1.6/ng-core.js",
            $"La forma «{forma}» no se reubicó: el navegador la resolvería contra el origen del CMS.");
    }

    [Theory]
    [MemberData(nameof(LasTresFormas))]
    public void Las_tres_formas_del_mismo_fichero_llegan_a_la_misma_url_en_otro_origen(string forma, string publicada)
    {
        Assert.True(
            ReubicacionDelImportMap.Reubicar(publicada, "https://cdn.ejemplo.co/", Mapa)
                == "https://cdn.ejemplo.co/synergos/runtime/angular/21.1.6/ng-core.js",
            $"La forma «{forma}» no se reubicó al CDN de otro origen.");
    }

    [Theory]
    [InlineData("./ng-core.js", "/cdn-bundles/synergos/runtime/angular/latest/ng-core.js")]
    [InlineData("ng-core.js", "/cdn-bundles/synergos/runtime/angular/latest/ng-core.js")]
    [InlineData("//otro-host/synergos/x.js", "/cdn-bundles/synergos/x.js")]
    [InlineData("https://synergos-static-local/synergos/x.js?v=2", "/cdn-bundles/synergos/x.js?v=2")]
    public void Lo_relativo_se_resuelve_contra_el_sitio_del_mapa_y_lo_absoluto_conserva_su_ruta(
        string publicada, string esperada)
        => Assert.Equal(esperada, ReubicacionDelImportMap.Reubicar(publicada, "/cdn-bundles", Mapa));

    [Theory]
    [InlineData("data:text/javascript,export default 1")]
    [InlineData("")]
    public void Lo_que_no_tiene_host_que_cambiar_se_conserva(string publicada)
        => Assert.Equal(publicada, ReubicacionDelImportMap.Reubicar(publicada, "/cdn-bundles", Mapa));

    [Fact]
    public void El_mapa_entero_se_reubica_entrada_por_entrada()
    {
        using var doc = JsonDocument.Parse("""
            { "@angular/core": "/synergos/runtime/angular/21.1.6/ng-core.js",
              "rxjs": "https://synergos-static-local/synergos/runtime/angular/21.1.6/rxjs.js" }
            """);

        var mapa = ReubicacionDelImportMap.Reubicar(doc.RootElement, "/cdn-bundles", Mapa);

        Assert.Equal("/cdn-bundles/synergos/runtime/angular/21.1.6/ng-core.js", mapa["@angular/core"]);
        Assert.Equal("/cdn-bundles/synergos/runtime/angular/21.1.6/rxjs.js", mapa["rxjs"]);
    }

    [Fact]
    public void La_ruta_del_mapa_es_la_que_leen_los_dos_clientes()
        => Assert.Equal(Mapa, ReubicacionDelImportMap.RutaDelMapa("synergos", "angular", "latest"));
}
