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
    public void La_convencion_del_repo_hermano_COMPONE_lo_que_este_test_ya_rechazaba()
    {
        // El fixture es literalmente lo que `Synergos.UI` publica desde su #58, y por eso está
        // acá: la REGLA vive en este árbol y la CAUSA en el otro, así que el test de la colisión
        // llevaba meses verde mientras el repo hermano publicaba exactamente el caso que rechaza
        // — nombres AGNÓSTICOS (`@synergos/core`) con destinos ESPECÍFICOS de Angular.
        //
        // La salida que tomó el hermano sale de la regla de arriba: Angular publica el alias
        // heredado Y su gemelo calificado apuntando al MISMO fichero —así se deduplica— y toda
        // plataforma nueva publica sólo el suyo. El alias no se retira nunca mientras haya
        // bundles publicados que lo importen por nombre.
        //
        // Sin este test, lo único que dice que la convención funciona vive en el otro repo.
        var (mapa, conflicto) = ImportMapComposer.Componer(new[]
        {
            Mapa("angular",
                ("@synergos/core", "/synergos/runtime/angular/21.1.6/sg-core.js"),
                ("@synergos/angular-core", "/synergos/runtime/angular/21.1.6/sg-core.js"),
                ("rxjs", "/synergos/runtime/angular/21.1.6/rxjs.js")),
            Mapa("react",
                ("@synergos/react-core", "/synergos/runtime/react/1.0.0/sg-core.js"),
                ("react", "/synergos/runtime/react/1.0.0/react.js")),
        });

        Assert.Null(conflicto);
        Assert.NotNull(mapa);

        // El alias heredado sigue resolviendo — es lo que importan los 127 bundles ya publicados.
        Assert.Equal("/synergos/runtime/angular/21.1.6/sg-core.js", mapa!.Imports["@synergos/core"]);
        // Y los dos calificados conviven, que es lo que el nombre agnóstico impedía.
        Assert.Equal("/synergos/runtime/angular/21.1.6/sg-core.js", mapa.Imports["@synergos/angular-core"]);
        Assert.Equal("/synergos/runtime/react/1.0.0/sg-core.js", mapa.Imports["@synergos/react-core"]);
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

    // ── Qué hace el cliente HTTP con un conflicto (Synergos.UI#58) ───────────

    [Fact]
    public void Un_conflicto_NUEVO_no_puede_tirar_el_mapa_que_ya_funcionaba()
    {
        // Esto es una regla sobre el CLIENTE y se afirma acá porque es donde se
        // razona la composición. El cliente conserva el vigente; este test fija el
        // PORQUÉ, que es lo que se me fue en #127:
        //
        // un mapa vigente sólo puede existir si cuando se compuso NO tenía conflicto
        // —con conflicto, `Componer` devuelve null y no hay vigente que guardar—.
        // Luego todo conflicto es NUEVO, lo introduce quien acaba de publicar, y
        // tirar el bueno apaga también a quien ya funcionaba.
        var soloAngular = ImportMapComposer.Componer(new[]
        {
            Mapa("angular", ("@synergos/core", "/synergos/runtime/angular/21.1.6/sg-core.js")),
        });
        Assert.Null(soloAngular.Conflicto);
        Assert.NotNull(soloAngular.Mapa);

        // Y el día que llega la segunda plataforma con el MISMO nombre agnóstico:
        var conLaSegunda = ImportMapComposer.Componer(new[]
        {
            Mapa("angular", ("@synergos/core", "/synergos/runtime/angular/21.1.6/sg-core.js")),
            Mapa("react", ("@synergos/core", "/synergos/runtime/react/18.3.1/sg-core.js")),
        });
        Assert.NotNull(conLaSegunda.Conflicto);
        Assert.Null(conLaSegunda.Mapa);

        // La conclusión que el cliente tiene que respetar: había uno bueno antes,
        // así que hay algo que conservar. Si `Componer` pudiera devolver conflicto
        // sobre un conjunto de UNA plataforma, esta regla no se sostendría.
        Assert.NotNull(soloAngular.Mapa);
    }

    [Fact]
    public void El_nombre_calificado_por_framework_es_lo_que_evita_el_conflicto()
    {
        // La salida de Synergos.UI#58, probada acá para que el otro árbol tenga
        // contra qué escribirla: Angular publica el nombre agnóstico Y el suyo
        // calificado —el mismo destino, así que se deduplica— y las plataformas
        // nuevas publican sólo el calificado. Nunca colisionan.
        var (mapa, conflicto) = ImportMapComposer.Componer(new[]
        {
            Mapa("angular",
                ("@synergos/core", "/synergos/runtime/angular/21.1.6/sg-core.js"),
                ("@synergos/angular-core", "/synergos/runtime/angular/21.1.6/sg-core.js")),
            Mapa("react", ("@synergos/react-core", "/synergos/runtime/react/18.3.1/sg-core.js")),
        });

        Assert.Null(conflicto);
        Assert.Equal(3, mapa!.Imports.Count);
    }
}
