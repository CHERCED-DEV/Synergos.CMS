using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Las invariantes del despliegue automático (HU #19): qué se comprueba, contra qué, y qué pasa
/// cuando falla.
/// </summary>
/// <remarks>
/// <para><b>Todo lo que vigila esta clase deja el despliegue en VERDE cuando se rompe.</b> Esa es
/// la razón de que exista: un pipeline de despliegue no tiene quien lo pruebe: si el humo miente,
/// lo que se ve es un action verde, que es exactamente lo que se quería ver.</para>
///
/// <list type="bullet">
///   <item><b>Un humo contra <c>localhost</c></b> pasa siempre. Contra el propio runner no hay
///   nada que pueda fallar — ni DNS, ni certificado, ni proxy, ni el servidor. Es el fallo más
///   fácil de escribir del oficio, y el único síntoma es que nunca falla.</item>
///
///   <item><b>Un humo que sólo mira que responda</b> no distingue «el sitio está en pie» de «el
///   sitio está en pie con lo que acabo de subir». Un reinicio fallido deja viva la versión
///   anterior y el despliegue se da por bueno habiendo desplegado nada.</item>
///
///   <item><b>Un <c>down</c> con <c>--volumes</c></b> borra los veinte almacenes, la base del CMS
///   y la biblioteca de medios. El despliegue sigue en verde: los contenedores arrancan, sanos,
///   vacíos.</item>
///
///   <item><b>Un rollback sin <c>exit 1</c></b> deja el sitio en pie y el action verde — o sea,
///   nadie se entera de que lo que se quiso desplegar no está.</item>
/// </list>
/// </remarks>
public sealed class DeployPipelineTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Leer(params string[] partes)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(partes).ToArray()));

    /// <summary>El fichero sin comentarios — no se mide la prosa que explica la regla.</summary>
    /// <remarks>
    /// Ya pasó una vez con <c>ComposeStackTests</c>: el comentario que explica por qué no se usa
    /// <c>localhost</c> contiene la palabra, y el gate cayó sobre su propia documentación. Un gate
    /// que se dispara con lo que lo explica termina desactivado.
    /// </remarks>
    private static string SinComentarios(params string[] partes)
        => string.Join('\n', File.ReadAllLines(Path.Combine(new[] { RepoRoot() }.Concat(partes).ToArray()))
            .Select(l => l.TrimStart())
            .Where(l => !l.StartsWith('#') && !l.StartsWith("//")));

    // ── El humo ─────────────────────────────────────────────────────────────

    [Fact]
    public void El_humo_no_apunta_nunca_a_la_maquina_que_lo_corre()
    {
        // EL GATE PRINCIPAL DE ESTA CLASE. Un humo contra localhost pasa siempre y el action se
        // pone verde con el sitio caído.
        var humo = SinComentarios("tools", "humo-publico.sh");

        Assert.DoesNotContain("localhost", humo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", humo, StringComparison.Ordinal);
        Assert.DoesNotContain("0.0.0.0", humo, StringComparison.Ordinal);
    }

    [Fact]
    public void El_humo_recibe_el_dominio_de_fuera_y_no_lo_lleva_escrito()
    {
        // Un dominio cableado en el script convierte el humo en algo que sólo sirve para un
        // despliegue, y hace imposible probarlo contra otro sin editar el fichero.
        var humo = Leer("tools", "humo-publico.sh");

        Assert.Contains("DOMINIO=\"${1:?", humo, StringComparison.Ordinal);
        Assert.Contains("https://${DOMINIO}", humo, StringComparison.Ordinal);
    }

    [Fact]
    public void El_humo_comprueba_QUE_VERSION_esta_contestando()
    {
        // «Responde» no es «se actualizó». Sin esta comprobación, un reinicio que falla en
        // silencio deja viva la versión anterior y el despliegue se da por bueno.
        var humo = SinComentarios("tools", "humo-publico.sh");

        Assert.Contains("_health", humo, StringComparison.Ordinal);
        Assert.Contains("ESPERADO", humo, StringComparison.Ordinal);
        Assert.Contains("\"version\"", humo, StringComparison.Ordinal);
    }

    [Fact]
    public void El_health_del_CMS_publica_la_version_que_el_humo_lee()
    {
        // Los dos lados del mismo contrato, en repos... en ficheros distintos: si el controller
        // deja de emitir `version`, el humo se queda sin nada que comparar y —peor— lo haría en
        // silencio, comparando cadena vacía con cadena vacía.
        var controller = Leer("Synergos.CMS.Web", "Controllers", "HealthController.cs");

        Assert.Contains("SYNERGOS_BUILD_SHA", controller, StringComparison.Ordinal);
        Assert.Contains("version =", controller, StringComparison.Ordinal);

        // Y que la imagen lo inyecte: sin el ARG, el controller siempre diría "desconocida" y la
        // comprobación del humo no podría acertar nunca.
        var dockerfile = Leer("Dockerfile");
        Assert.Contains("ARG VERSION", dockerfile, StringComparison.Ordinal);
        Assert.Contains("SYNERGOS_BUILD_SHA=${VERSION}", dockerfile, StringComparison.Ordinal);

        var images = Leer(".github", "workflows", "images.yml");
        Assert.Contains("VERSION=${{ github.sha }}", images, StringComparison.Ordinal);
    }

    [Fact]
    public void El_humo_verifica_desde_fuera_que_el_arbol_de_servicios_no_esta_expuesto()
    {
        // `ComposeStackTests` vigila el FICHERO; esto vigila la REALIDAD. Un firewall mal puesto o
        // un `ports:` añadido a mano en el servidor no se ven en el repo.
        var humo = SinComentarios("tools", "humo-publico.sh");

        Assert.Contains("/v1/", humo, StringComparison.Ordinal);
        Assert.Contains("hay una capacidad alcanzable desde internet", humo, StringComparison.Ordinal);
    }

    // ── Lo que corre en el servidor ─────────────────────────────────────────

    [Fact]
    public void El_despliegue_para_antes_de_arrancar()
    {
        // No es una preferencia: JsonCollectionStore tiene un lock de PROCESO, y un despliegue
        // "sin caída" son dos instancias a la vez pisándose el almacén, sin dar error.
        var remoto = SinComentarios("tools", "deploy-remoto.sh");

        var posDown = remoto.IndexOf("$COMPOSE down", StringComparison.Ordinal);
        var posUp = remoto.IndexOf("$COMPOSE up", StringComparison.Ordinal);

        Assert.True(posDown >= 0, "El despliegue no para el stack: sería un arranque encima del anterior.");
        Assert.True(posUp >= 0, "El despliegue no arranca el stack.");
        Assert.True(posDown < posUp, "El `up` va ANTES del `down`: eso es un rolling deploy, que corrompe el almacén.");
    }

    [Fact]
    public void El_despliegue_no_puede_borrar_los_volumenes()
    {
        // Un `--volumes` de más borra los veinte almacenes, la DB del CMS y la biblioteca de
        // medios — y el despliegue queda VERDE, porque los contenedores arrancan perfectos y
        // vacíos. No hay vuelta atrás que lo arregle: las imágenes se reconstruyen, los datos no.
        var remoto = SinComentarios("tools", "deploy-remoto.sh");

        Assert.DoesNotContain("--volumes", remoto, StringComparison.Ordinal);
        Assert.DoesNotContain("down -v", remoto, StringComparison.Ordinal);
        Assert.DoesNotContain("volume rm", remoto, StringComparison.Ordinal);
        Assert.DoesNotContain("volume prune", remoto, StringComparison.Ordinal);
    }

    [Fact]
    public void El_despliegue_comprueba_que_lo_que_corre_es_la_etiqueta_pedida()
    {
        // El fallo silencioso: una imagen no se pudo bajar, Docker reusa la que tenía, todo
        // reporta sano — y corre la versión anterior. Sano no es actualizado.
        var remoto = SinComentarios("tools", "deploy-remoto.sh");

        Assert.Contains("tag.actual", remoto, StringComparison.Ordinal);
        Assert.Contains("NO están en $SHA", remoto, StringComparison.Ordinal);
    }

    // ── El workflow ─────────────────────────────────────────────────────────

    [Fact]
    public void El_workflow_usa_el_humo_y_vuelve_atras_cuando_falla()
    {
        var deploy = Leer(".github", "workflows", "deploy.yml");

        Assert.Contains("tools/humo-publico.sh", deploy, StringComparison.Ordinal);

        // La vuelta atrás tiene que estar CONDICIONADA al fallo del humo. Sin la condición, o no
        // corre nunca o corre siempre — las dos son inútiles de formas distintas.
        Assert.Contains("steps.humo.outcome == 'failure'", deploy, StringComparison.Ordinal);
    }

    [Fact]
    public void Una_vuelta_atras_deja_el_action_ROJO()
    {
        // Volver sin ponerse rojo deja un despliegue "exitoso" que no desplegó nada: el sitio
        // sigue en pie con la versión vieja y nadie se entera. Las dos cosas, o ninguna sirve.
        var deploy = Leer(".github", "workflows", "deploy.yml");

        var inicio = deploy.IndexOf("name: Vuelta atrás", StringComparison.Ordinal);
        Assert.True(inicio > 0, "No hay paso de vuelta atrás en el workflow.");

        var paso = deploy[inicio..];
        var fin = paso.IndexOf("\n      - name:", StringComparison.Ordinal);
        if (fin > 0) paso = paso[..fin];

        // No basta con que HAYA un `exit 1` en el paso. Lo destapó mutar este mismo gate: el paso
        // tiene dos salidas —«no hay a qué volver» y «se volvió»— y borrando la segunda el gate
        // seguía verde porque encontraba la primera. O sea, vigilaba la rama que NO importa.
        //
        // Lo que hay que exigir es que la rama que SÍ restauró el sitio también salga en rojo:
        // es justo la que uno estaría tentado de dar por buena, porque el sitio quedó en pie.
        var trasRestaurar = paso.IndexOf("se restauró", StringComparison.Ordinal);
        Assert.True(trasRestaurar > 0, "La vuelta atrás no dice que restauró nada.");

        Assert.Contains("exit 1", paso[trasRestaurar..], StringComparison.Ordinal);
    }

    [Fact]
    public void Dos_despliegues_no_pueden_correr_a_la_vez_ni_cancelarse()
    {
        // Dos `down`/`up` simultáneos sobre los mismos volúmenes es la forma más rápida de
        // corromper el estado. Y cancelar el que está corriendo es peor que encolarlo: lo deja a
        // medias, con parte del stack abajo.
        var deploy = Leer(".github", "workflows", "deploy.yml");

        Assert.Contains("concurrency:", deploy, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: false", deploy, StringComparison.Ordinal);
    }

    [Fact]
    public void El_workflow_no_lleva_ningun_secreto_escrito()
    {
        // Lo que se busca no es un secreto concreto —no hay ninguno— sino la FORMA de haberlo
        // escrito: un valor literal donde debería haber una referencia a `secrets.`.
        var deploy = Leer(".github", "workflows", "deploy.yml");

        foreach (var nombre in new[] { "DEPLOY_SSH_KEY", "DEPLOY_HOST", "DEPLOY_USER" })
        {
            Assert.Contains($"secrets.{nombre}", deploy, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("BEGIN OPENSSH PRIVATE KEY", deploy, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_", deploy, StringComparison.Ordinal);
    }

    [Fact]
    public void Sin_servidor_configurado_el_despliegue_se_salta_en_vez_de_ponerse_rojo()
    {
        // Un rojo permanente que todos saben ignorar entrena a ignorar los rojos de verdad.
        // Mientras el VPS no exista, esto tiene que saltarse solo — y encenderse solo el día que
        // aparezcan los secretos, sin que nadie tenga que acordarse de editar el workflow.
        var deploy = Leer(".github", "workflows", "deploy.yml");

        Assert.Contains("steps.config.outputs.listo", deploy, StringComparison.Ordinal);
        Assert.Contains("El paso a paso está en", deploy, StringComparison.Ordinal);
    }

    // ── Los scripts, como ficheros ──────────────────────────────────────────

    [Fact]
    public void Los_scripts_del_despliegue_existen_y_paran_al_primer_error()
    {
        // Sin `set -e`, un `docker compose pull` que falla no detiene el script: se sigue al
        // `down`, y el sitio se cae para desplegar algo que no se pudo bajar.
        foreach (var script in new[] { "deploy-remoto.sh", "humo-publico.sh", "bootstrap-servidor.sh",
                                       "importar-schema.sh", "provisionar.sh" })
        {
            var ruta = Path.Combine(RepoRoot(), "tools", script);
            Assert.True(File.Exists(ruta), $"Falta tools/{script}");
            Assert.Contains("set -euo pipefail", File.ReadAllText(ruta), StringComparison.Ordinal);
        }
    }

    // ── El import de uSync: un paso, y que los documentos no digan otra cosa ─

    /// <summary>
    /// Ficheros de configuración que la APP lee al arrancar — los que deciden si importa sola.
    /// </summary>
    private static IEnumerable<string> ConfiguracionDeLaApp()
    {
        yield return Path.Combine(RepoRoot(), "compose.prod.yml");

        // Sólo los appsettings que la app CARGA de verdad: `appsettings.json` y sus variantes
        // por entorno. Los `appsettings-schema*.json` quedan fuera y no es una exención de
        // conveniencia — son el JSON Schema que publican los paquetes para el IntelliSense del
        // editor: DESCRIBEN la opción (con su default `None`) y no la ponen, ASP.NET no los
        // carga nunca, y ninguno es editable por nosotros. Sin este corte el gate se dispara con
        // la documentación de uSync y acusa a la app de algo que la app no hace, que es la clase
        // de gate que alguien acaba borrando en vez de arreglar.
        var cargados = new Regex(@"^appsettings(\.[A-Za-z0-9_]+)?\.json$");
        var vistos = 0;

        foreach (var f in Directory.EnumerateFiles(
                     Path.Combine(RepoRoot(), "Synergos.CMS.Web"), "appsettings*.json")
                     .Where(f => cargados.IsMatch(Path.GetFileName(f))))
        {
            vistos++;
            yield return f;
        }

        // Una red de seguridad contra el corte de arriba: si el filtro dejara de casar, el gate
        // pasaría en verde sin haber mirado ninguna configuración.
        Assert.True(vistos > 0,
            "No se encontró ningún appsettings cargable en Synergos.CMS.Web: el filtro de este "
            + "gate dejó de casar y estaría vigilando el vacío.");
        foreach (var f in Directory.EnumerateFiles(
                     Path.Combine(RepoRoot(), "Synergos.CMS.Web"), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                              && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            yield return f;
        }
    }

    /// <summary>
    /// La app NO importa uSync al arrancar, y si algún día lo hiciera hay cuatro sitios que
    /// cambiar a la vez.
    /// </summary>
    /// <remarks>
    /// <para><b>Cuatro documentos afirmaban lo contrario</b> (#114): el doc del despliegue
    /// —«importa 880 ítems… <i>es lo que hace que no haya que correr el import a mano</i>»—,
    /// <c>deploy-remoto.sh</c>, el <c>Caddyfile</c> y, por arrastre, lo que el operador espera.
    /// Medido contra una base vacía, el arranque dice <c>uSync: Startup Complete 0ms</c>. La
    /// prueba de que ya se sabía la da el propio gate de CI: <c>usync-rebuild-check.mjs</c>
    /// inyecta <c>ImportAtStartup=All</c> por variable de entorno <b>porque la app no lo
    /// hace</b>.</para>
    ///
    /// <para><b>Y un Umbraco vacío no falla</b>: sirve «No published content» con 200 y HTML de
    /// verdad, así que el humo lo daba por bueno. Un servidor nuevo se habría desplegado en verde
    /// con el sitio en blanco — la forma que este repo ya nombró tres veces.</para>
    ///
    /// <para><b>Por qué no se activa y ya:</b> <c>appsettings.json</c> tiene encendidos
    /// <c>ContentHandler</c> y <c>MediaHandler</c> (ADR 0129), así que <c>All</c> re-importaría el
    /// contenido del repo <b>en cada arranque</b> y cada reinicio sería una vuelta atrás
    /// editorial, en silencio. Y <c>Settings</c> —la vía de escape que ADR 0008 deja prevista— no
    /// resuelve nada: deja el sitio con schema y sin contenido, o sea el mismo cartel.</para>
    ///
    /// <para>El gate no vigila la prosa de los documentos, que es infinita: vigila <b>el hecho</b>
    /// del que la prosa hablaba. Si alguien enciende el import, esto se pone rojo y el mensaje
    /// dice qué hay que revisar.</para>
    /// </remarks>
    [Fact]
    public void El_import_de_uSync_NO_ocurre_al_arrancar()
    {
        var encendido = ConfiguracionDeLaApp()
            .Where(f => File.ReadAllText(f).Contains("ImportAtStartup", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(RepoRoot(), f))
            .ToList();

        Assert.True(encendido.Count == 0,
            $"Algo le dice a la app que importe uSync al arrancar: {string.Join(", ", encendido)}. "
            + "ADR 0008 lo prohíbe y hay una razón viva: con ContentHandler encendido (ADR 0129), "
            + "importar en cada arranque REVIERTE lo que un editor publicó, en silencio. "
            + "Si de verdad se decide cambiarlo, va con ADR sucesor y hay que reescribir "
            + "docs/despliegue/00-montar-el-entorno.md §5.bis y tools/deploy-remoto.sh, que hoy "
            + "dicen que es un paso a mano.");
    }

    [Fact]
    public void El_paso_del_import_tiene_herramienta_y_esta_documentado()
    {
        // La otra mitad: si el import es un paso, tiene que estar escrito DONDE se monta el
        // servidor. Antes no aparecía en ninguna parte, y el doc que debía nombrarlo afirmaba
        // justo lo contrario.
        var doc = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "despliegue", "00-montar-el-entorno.md"));

        Assert.Contains("importar-schema.sh", doc, StringComparison.Ordinal);
        Assert.Contains("provisionar.sh", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void El_humo_distingue_el_sitio_del_cartel_de_Umbraco_VACIO()
    {
        // Sin esto el humo da por bueno un servidor sin schema: Umbraco vacío contesta 200 con
        // `<html>` de verdad, así que las dos comprobaciones que había pasaban. Medido en el
        // stack compuesto: 200, 1926 bytes, `<title>Umbraco: No published content</title>`.
        var humo = Leer("tools", "humo-publico.sh");

        Assert.Contains("No published content", humo, StringComparison.Ordinal);
    }
}
