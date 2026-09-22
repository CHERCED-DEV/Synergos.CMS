using System.Text.Json;
using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El perfil con el que corre PRODUCCIÓN no trae credenciales, y quien las pone las exige del
/// entorno (#150).
/// </summary>
/// <remarks>
/// <para><b>Qué pasó.</b> <c>compose.prod.yml</c> levanta el CMS con
/// <c>ASPNETCORE_ENVIRONMENT: Docker</c>, así que producción corre con
/// <c>appsettings.Docker.json</c> — y ese fichero traía la cuenta de administrador del backoffice
/// con su correo y su contraseña, versionada en un repo <b>público</b>. El mismo literal estaba en
/// <b>siete</b> sitios; el ticket decía seis, porque la lista estaba escrita a mano
/// (<c>feedback_a_named_list_beats_a_count</c>).</para>
///
/// <para><b>Es el gemelo de #113, y eso es lo que lo vuelve caro.</b> Aquel apagó <i>una</i> clave
/// que el perfil de Docker traía mal para producción —la siembra de desarrollo, con los catorce
/// endpoints <c>[AllowAnonymous]</c> de <c>DevController</c> alcanzables desde internet— y no se
/// hizo la pregunta que cierra la familia: <b>¿qué MÁS trae ese perfil que no sirve para
/// producción?</b></para>
///
/// <para><b>Lo que Umbraco NO permite, medido y no supuesto</b> (el ticket pedía «la clave
/// presente y vacía o ausente, según lo que Umbraco tolere»). Con la clave vacía y el nombre
/// puesto, el arranque revienta: <i>«Configuration entry Umbraco:CMS:Unattended contains invalid
/// values. If any of the UnattendedUserName, UnattendedUserEmail, UnattendedUserPassword are set,
/// all of them are required.»</i> Con el correo vacío, otra excepción. Y con las <b>tres
/// ausentes</b> no revienta — que es el caso peligroso: la instalación desatendida <b>completa</b>
/// y deja el administrador con <c>userPassword = 'default'</c> y <c>userDisabled = 0</c>, o sea un
/// sitio que contesta 200 al que nadie puede entrar y sin instalador que lo arregle. Por eso el
/// <b>nombre se queda</b> en los dos perfiles: es lo que hace RUIDOSA la ausencia de los otros dos
/// en vez de silenciosa.</para>
///
/// <para><b>Lo que este gate NO hace, y por qué</b> — el ticket proponía cruzar «las claves que
/// huelen a credencial que el compose no pisa <i>con <c>:?</c></i>». Medido antes de escribirlo:
/// eso son <b>más de cuarenta</b> excepciones en un solo fichero, y todas legítimas — las decenas
/// de <c>${SYNERGOS_API_KEY}</c> repetidas son la MISMA variable que el primer servicio ya exige
/// con <c>:?</c> (una vez exigida, compose falla si falta), y los ocho <c>${…:-}</c> son secretos
/// <b>opcionales a propósito</b> cuya ausencia el código grita. Y el olor del nombre da falsos
/// positivos de fábrica: <c>IdentityTokens__LifetimeMinutes</c>, <c>Synergos__Gob__DefinitionKey</c>
/// y <c>Payments__wompi__PublicKey</c> no son credenciales. Un gate con cuarenta exenciones es el
/// muro que deja de leerse (<c>feedback_a_gate_that_asks_for_more_than_needed_never_goes_red</c>).
/// Lo que sí es airtight, y es lo que el defecto violaba, es que <b>el valor no esté escrito</b>:
/// hoy ninguna clave de credencial de <c>compose.prod.yml</c> lleva literal, así que el trinquete
/// sale ABSOLUTO y es gratis, que es la condición del #134.</para>
/// </remarks>
public sealed class PerfilDeProduccionTests
{
    /// <summary>
    /// Las tres claves de la instalación desatendida. Van juntas o no van: lo exige Umbraco.
    /// </summary>
    private const string Seccion = "Umbraco:CMS:Unattended";
    private const string Nombre = Seccion + ":UnattendedUserName";
    private const string Correo = Seccion + ":UnattendedUserEmail";
    private const string Clave = Seccion + ":UnattendedUserPassword";

    /// <summary>
    /// Qué nombre de clave se lee como «acá va un secreto».
    /// </summary>
    /// <remarks>
    /// <c>PublicKey</c> queda fuera adrede — una llave pública no es un secreto por definición, y
    /// meterla obligaría a censarla, que es empezar el muro de excepciones por el primer ladrillo.
    /// </remarks>
    private static readonly Regex OlorACredencial =
        new(@"(?<!Public)(Password|Secret|ApiKey|SigningKey|SecretKey)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// El ÚNICO literal de credencial que sobrevive, con su razón — y vigilado en los dos
    /// sentidos: si deja de estar, esta entrada rompe el build (#137).
    /// </summary>
    /// <remarks>
    /// No es un secreto de producción y el propio fichero lo dice: el default de
    /// <c>CartSettings</c> dispara un <c>Critical</c> en boot fuera de Development (ADR 0028) y
    /// cualquier cadena propia lo silencia. Su razón contesta «por qué esto NO se arregla» y no
    /// «por qué no se arregló todavía», que es lo que distingue una exención de un ticket sin
    /// abrir (<c>feedback_a_census_entry_is_how_a_defect_survives_its_own_gate</c>).
    /// </remarks>
    private static readonly Dictionary<string, string> LiteralesDeclarados = new(StringComparer.Ordinal)
    {
        ["docker-compose.yml|Synergos__Cart__SecretKey"] =
            "no es un secreto: silencia el Critical de ADR 0028 en el compose de desarrollo",
    };

    private static IReadOnlyList<string> Composes()
        => new[] { "compose.prod.yml", "docker-compose.yml" }
            .Select(f => Proyectos.Ruta(f))
            .ToList();

    /// <summary>Cada hoja del JSON como `ruta:con:puntos` → valor, para un fichero.</summary>
    private static List<(string Ruta, string Valor)> Hojas(string fichero)
    {
        var hojas = new List<(string, string)>();
        using var doc = ClavesDeUmbracoTests.Abrir(fichero);
        Bajar(doc.RootElement, string.Empty, hojas);
        return hojas;
    }

    private static void Bajar(JsonElement nodo, string prefijo, List<(string, string)> hojas)
    {
        switch (nodo.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in nodo.EnumerateObject())
                {
                    Bajar(prop.Value, prefijo.Length == 0 ? prop.Name : prefijo + ":" + prop.Name, hojas);
                }
                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in nodo.EnumerateArray())
                {
                    Bajar(item, $"{prefijo}:{i++}", hojas);
                }
                break;
            default:
                hojas.Add((prefijo, nodo.ToString()));
                break;
        }
    }

    [Fact]
    public void Ningun_appsettings_versionado_lleva_una_credencial_escrita()
    {
        var ficheros = ClavesDeUmbracoTests.Appsettings();
        Assert.True(ficheros.Count >= 2,
            $"Sólo se vieron {ficheros.Count} appsettings: si se movieron, este gate pasa en verde "
            + "sin mirar nada.");

        var todas = ficheros.SelectMany(f => Hojas(f).Select(h => (Fichero: Path.GetFileName(f), h.Ruta, h.Valor))).ToList();
        Assert.True(todas.Count >= 100,
            $"Sólo se cruzaron {todas.Count} claves: el recorrido del JSON no está bajando.");

        var escritas = todas
            .Where(x => OlorACredencial.IsMatch(x.Ruta.Split(':')[^1]))
            .Where(x => !string.IsNullOrWhiteSpace(x.Valor))
            .Select(x => $"{x.Fichero} → {x.Ruta} = «{x.Valor}»")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(escritas.Count == 0,
            "Estos appsettings traen una credencial ESCRITA, y este repo es público:\n  "
            + string.Join("\n  ", escritas)
            + "\n\n`appsettings.Docker.json` es el perfil con el que corre PRODUCCIÓN "
            + "(`ASPNETCORE_ENVIRONMENT: Docker` en compose.prod.yml), así que un literal acá es la "
            + "credencial del sitio desplegado publicada en GitHub (#150). Va al entorno, y el "
            + "compose la exige con `:?`.");
    }

    [Fact]
    public void Ninguna_credencial_del_compose_lleva_el_valor_escrito()
    {
        var vistos = new List<string>();
        var escritos = new List<string>();

        foreach (var compose in Composes())
        {
            var nombre = Path.GetFileName(compose);
            foreach (var (_, clave, valor) in ParesDeEntorno(compose))
            {
                if (!OlorACredencial.IsMatch(clave.Split("__")[^1])) continue;
                vistos.Add($"{nombre}|{clave}");

                // Del entorno está bien, escrito no. `${...}` es la única forma buena.
                if (valor.Contains("${", StringComparison.Ordinal)) continue;
                if (!escritos.Contains($"{nombre}|{clave}")) escritos.Add($"{nombre}|{clave}");
            }
        }

        Assert.True(vistos.Count >= 40,
            $"Sólo se vieron {vistos.Count} claves de credencial en los compose: el parseo no está "
            + "leyendo los bloques `environment:`.");

        var sinDeclarar = escritos.Where(e => !LiteralesDeclarados.ContainsKey(e)).OrderBy(e => e, StringComparer.Ordinal).ToList();
        Assert.True(sinDeclarar.Count == 0,
            "Estas claves de credencial llevan el valor ESCRITO en el compose: " + string.Join(", ", sinDeclarar)
            + ". Un secreto en un compose versionado es un secreto público. Va `${VAR:?falta VAR}` "
            + "— con `:?`, `docker compose up` falla antes de arrancar el contenedor, que es la "
            + "única forma de fallar que no se descubre delante de una persona (#150).");

        var sobran = LiteralesDeclarados.Keys.Where(k => !escritos.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(sobran.Count == 0,
            "Estas entradas del censo ya no corresponden a ningún literal: " + string.Join(", ", sobran)
            + ". Se borran en el mismo commit que quitó el literal — un censo vigilado en un solo "
            + "sentido se queda afirmando que el defecto sigue ahí (#137).");
    }

    [Fact]
    public void La_credencial_desatendida_va_completa_o_solo_el_nombre()
    {
        // ── Los perfiles: el nombre sí, el correo y la clave NO ──────────────────────
        foreach (var fichero in ClavesDeUmbracoTests.Appsettings())
        {
            var hojas = Hojas(fichero).ToDictionary(h => h.Ruta, h => h.Valor, StringComparer.Ordinal);
            var nombre = Path.GetFileName(fichero);

            foreach (var prohibida in new[] { Correo, Clave })
            {
                Assert.False(hojas.ContainsKey(prohibida),
                    $"{nombre} declara `{prohibida}`, y no puede: el correo y la clave del "
                    + "administrador van en el ENTORNO (#150). Este repo es público y "
                    + "`appsettings.Docker.json` es el perfil con el que corre producción.");
            }

            if (!hojas.TryGetValue(Seccion + ":InstallUnattended", out var instala)
                || !instala.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Assert.True(hojas.TryGetValue(Nombre, out var valorNombre) && !string.IsNullOrWhiteSpace(valorNombre),
                $"{nombre} enciende `InstallUnattended` y no declara `{Nombre}`. Ese nombre NO es "
                + "decoración: con las TRES claves ausentes Umbraco instala igual y deja el "
                + "administrador con `userPassword = 'default'` habilitado — un sitio en 200 al que "
                + "nadie puede entrar y sin instalador que lo arregle (medido, #150). El nombre "
                + "puesto es lo que convierte la ausencia de las otras dos en un fallo RUIDOSO.");
        }

        // ── Los compose: los dos que faltan, del entorno y con `:?` ──────────────────
        foreach (var compose in Composes())
        {
            var nombre = Path.GetFileName(compose);

            // Sólo el servicio `cms`: es el único que arranca con el perfil Docker, y pedírselo a
            // las veinticuatro capacidades sería un gate que exige más de lo que hace falta.
            var pares = ParesDeEntorno(compose)
                .Where(p => p.Servicio == "cms")
                .GroupBy(p => p.Clave, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Valor, StringComparer.Ordinal);

            Assert.NotEmpty(pares);

            foreach (var clave in new[] { Correo, Clave })
            {
                var env = clave.Replace(":", "__", StringComparison.Ordinal);
                Assert.True(pares.TryGetValue(env, out var valor),
                    $"{nombre} arranca el CMS con el perfil Docker y no le pasa `{env}`. Umbraco "
                    + "exige las tres o ninguna, así que con el nombre en el perfil y ésta ausente "
                    + "el arranque REVIENTA. Va `${...:?falta ...}`.");

                Assert.True(valor!.Contains(":?", StringComparison.Ordinal),
                    $"{nombre} pasa `{env}` pero sin `:?` — «{valor}». Sin eso, la variable ausente "
                    + "llega VACÍA y Umbraco revienta al arrancar el contenedor en vez de fallar en "
                    + "`docker compose up`, que es donde el operador lo está mirando (#150).");
            }
        }
    }

    /// <summary>
    /// Los pares <c>CLAVE: valor</c> de los bloques <c>environment:</c> de un compose.
    /// </summary>
    /// <remarks>
    /// Se descartan los comentarios antes de parsear: los de este repo explican los defectos
    /// citando las claves y los valores que hubo, así que un parseo del texto crudo se dispararía
    /// con la explicación (<c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c>).
    /// </remarks>
    private static IEnumerable<(string Servicio, string Clave, string Valor)> ParesDeEntorno(string compose)
    {
        // Se sigue el bloque por SANGRÍA, y cada par se devuelve CON SU SERVICIO. Las dos cosas
        // costaron un rojo: sin la sangría, el parseo casaba `networks:` y `volumes:` —una vez por
        // servicio— y sin el servicio, `IdentityTokens__ActiveKeyId` aparece en varias capacidades.
        // Un nombre de clave no dice en qué bloque vive ni de quién es; la sangría sí.
        var servicio = "(raíz)";
        var sangriaDelBloque = -1;

        foreach (var cruda in File.ReadAllLines(compose))
        {
            var linea = cruda.TrimStart();
            if (linea.Length == 0 || linea.StartsWith('#')) continue;

            var sangria = cruda.Length - linea.Length;

            if (linea.StartsWith("environment:", StringComparison.Ordinal))
            {
                sangriaDelBloque = sangria;
                continue;
            }

            if (sangriaDelBloque >= 0 && sangria > sangriaDelBloque)
            {
                var m = Regex.Match(linea, @"^([A-Za-z_][A-Za-z0-9_]*(?:__[A-Za-z0-9_]+)*): *(.*)$");
                if (m.Success)
                {
                    yield return (servicio, m.Groups[1].Value, m.Groups[2].Value.Trim().Trim('"', '\''));
                }
                continue;
            }

            // Fuera del bloque. A sangría 2 vive el nombre de un servicio.
            sangriaDelBloque = -1;
            var s = Regex.Match(linea, @"^([a-z][a-z0-9-]*):$");
            if (s.Success && sangria == 2) servicio = s.Groups[1].Value;
        }
    }
}
