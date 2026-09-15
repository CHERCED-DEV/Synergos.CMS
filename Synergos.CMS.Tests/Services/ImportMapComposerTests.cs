using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El import map se COMPONE por framework (#127).
/// </summary>
/// <remarks>
/// <para><b>Por qué componer y no elegir.</b> El navegador lee <b>el primer</b>
/// <c>&lt;script type="importmap"&gt;</c> de la página e ignora los siguientes: no se acumulan y
/// uno presente no se corrige con otro después. Con dos frameworks publicando, servir el mapa de
/// uno deja al otro sin resolver sus bare specifiers — y eso no falla a la vista: se queda el SSR
/// pintado y el elemento no hidrata (#126).</para>
///
/// <para><b>El fixture lleva entradas DISTINTAS en cada framework a propósito.</b> Con dos mapas
/// iguales, componerlos o quedarse con el primero da exactamente el mismo JSON y la mutación pasa
/// en verde con el defecto puesto — es la regla 7 del repo hermano.</para>
/// </remarks>
public sealed class ImportMapComposerTests
{
    private static (string, IReadOnlyDictionary<string, string>) Mapa(
        string framework, params (string Specifier, string Url)[] entradas)
        => (framework, entradas.ToDictionary(e => e.Specifier, e => e.Url, StringComparer.Ordinal));

    [Fact]
    public void Dos_frameworks_dan_UN_mapa_con_las_entradas_de_los_dos()
    {
        var (mapa, conflicto) = ImportMapComposer.Componer(new[]
        {
            Mapa("angular", ("@angular/core", "/cdn/ng/core.js"), ("rxjs", "/cdn/rxjs.js")),
            Mapa("react", ("react", "/cdn/react/react.js"), ("react-dom", "/cdn/react/dom.js")),
        });

        Assert.Null(conflicto);
        Assert.NotNull(mapa);
        Assert.Equal(4, mapa!.Imports.Count);
        Assert.Equal("/cdn/ng/core.js", mapa.Imports["@angular/core"]);
        Assert.Equal("/cdn/react/react.js", mapa.Imports["react"]);
    }

    [Fact]
    public void El_mismo_specifier_con_la_MISMA_url_no_es_conflicto_se_deduplica()
    {
        // `rxjs` puede salir del mismo sitio para dos frameworks. Tratarlo como conflicto pararía
        // el mapa por algo que no tiene nada de malo.
        var (mapa, conflicto) = ImportMapComposer.Componer(new[]
        {
            Mapa("angular", ("rxjs", "/cdn/rxjs.js")),
            Mapa("react", ("rxjs", "/cdn/rxjs.js"), ("react", "/cdn/react.js")),
        });

        Assert.Null(conflicto);
        Assert.Equal(2, mapa!.Imports.Count);
    }

    [Fact]
    public void El_mismo_specifier_con_URLS_DISTINTAS_para_el_mapa_y_nombra_a_los_dos()
    {
        // El único caso que no se puede resolver solo. Elegir uno en silencio deja al otro
        // framework cargando el runtime equivocado, y eso se lee como «ese elemento está roto».
        var (mapa, conflicto) = ImportMapComposer.Componer(new[]
        {
            Mapa("angular", ("@synergos/core", "/cdn/ng/synergos-core.js")),
            Mapa("react", ("@synergos/core", "/cdn/react/synergos-core.js")),
        });

        Assert.Null(mapa);
        Assert.NotNull(conflicto);
        Assert.Contains("@synergos/core", conflicto!, StringComparison.Ordinal);
        Assert.Contains("angular", conflicto, StringComparison.Ordinal);
        Assert.Contains("react", conflicto, StringComparison.Ordinal);
    }

    [Fact]
    public void Las_claves_salen_en_orden_ESTABLE_no_en_el_de_llegada()
    {
        // Sin orden estable, dos réplicas emiten el mismo mapa con las claves en distinto orden y
        // cualquier caché intermedia guarda dos copias de lo mismo.
        var uno = ImportMapComposer.Componer(new[]
        {
            Mapa("react", ("zod", "/z.js"), ("react", "/r.js")),
            Mapa("angular", ("@angular/core", "/c.js")),
        }).Mapa!;

        var otro = ImportMapComposer.Componer(new[]
        {
            Mapa("angular", ("@angular/core", "/c.js")),
            Mapa("react", ("react", "/r.js"), ("zod", "/z.js")),
        }).Mapa!;

        Assert.Equal(uno.Imports.Keys, otro.Imports.Keys);
        Assert.Equal(new[] { "@angular/core", "react", "zod" }, uno.Imports.Keys);
    }

    [Fact]
    public void Los_frameworks_se_DERIVAN_del_registry_sin_repetir_y_en_orden()
    {
        var declarados = ImportMapComposer.FrameworksDeclarados(new[]
        {
            new[] { "angular" },
            new[] { "react", "angular" },
            new[] { "svelte" },
            Array.Empty<string>(),
        });

        Assert.Equal(new[] { "angular", "react", "svelte" }, declarados);
    }

    [Fact]
    public void Un_framework_en_blanco_no_entra_en_la_lista()
    {
        // Una entrada del registry sin framework no puede convertirse en una ruta
        // `runtime//latest/import-map.json`, que pediría algo que no existe y ensuciaría el log.
        var declarados = ImportMapComposer.FrameworksDeclarados(new[]
        {
            new[] { "angular", "" },
            new[] { "   " },
        });

        Assert.Equal(new[] { "angular" }, declarados);
    }
}
