using System.Text.Json;
using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Ninguna credencial vive en el árbol, y la que el despliegue necesita llega del ENTORNO de
/// forma que no pueda quedarse vacía en silencio (#150).
/// </summary>
/// <remarks>
/// <para><b>Qué costó esto.</b> <c>appsettings.Docker.json</c> traía
/// <c>"UnattendedUserPassword": "…"</c> junto a <c>InstallUnattended: true</c>, y
/// <c>ASPNETCORE_ENVIRONMENT: Docker</c> es el perfil con el que corre <b>producción</b>. O sea
/// que la cuenta de administrador del backoffice del sitio desplegado se creaba con una
/// contraseña publicada en un repo público, junto con su correo. El mismo literal estaba en
/// <b>siete</b> ficheros versionados.</para>
///
/// <para><b>Y el propio repo ya lo prohibía.</b> <c>.env.example</c> abre con «NINGÚN SECRETO
/// ENTRA AL REPO. NI UNO» — o sea que la regla estaba escrita, bien escrita, y no la cruzaba
/// nadie. Es la forma de <c>feedback_a_named_list_beats_a_count</c> aplicada a una prohibición:
/// una prosa que nadie mide se desvía igual que una cifra.</para>
///
/// <para><b>Es el gemelo del #113</b>, que encontró UNA clave que el perfil de Docker traía mal
/// para producción —los catorce endpoints de <c>DevController</c> alcanzables desde internet— y
/// la apagó en el compose. La pregunta que no se hizo entonces es la que cierra la familia:
/// <i>¿qué MÁS trae ese perfil que no sirve para producción?</i></para>
///
/// <para><b>Los tres dientes, y por qué hacen falta los tres.</b> El primero mira el ÁRBOL y es
/// el defecto tal cual. El segundo mira el COMPOSE y caza que vuelva por la otra puerta: escribir
/// el literal en <c>compose-gen.mjs</c> en vez de en un <c>appsettings</c> sería el mismo secreto
/// publicado, en otro fichero. El tercero caza lo que ninguno de los dos ve —una credencial que
/// llega <b>vacía</b>—, que es la forma que este repo ya pagó con la llave de firma de
/// <c>Api.Identity</c>: un servidor que arranca verde, contesta <c>/health</c> y rechaza a la
/// primera persona que intente entrar.</para>
///
/// <para><b>Lo que este gate NO puede hacer</b>, y va dicho: rotar lo que ya se publicó. El
/// literal que estuvo en el árbol está quemado, y <c>.env.example</c> lo dice con todas las
/// letras — «si alguno se pega por error en un commit… SE ROTA, no se borra el mensaje». Un
/// árbol limpio a partir de hoy no desquema lo de ayer.</para>
///
/// <para><b>Red de seguridad</b>: si no se ven appsettings, o el compose no devuelve filas, el
/// gate <b>falla</b> en vez de pasar sobre una lista vacía — el modo de fallo del #136, que se
/// hereda porque un verde no se mira.</para>
/// </remarks>
public sealed class CredencialesFueraDelArbolTests
{
    /// <summary>
    /// Cómo se reconoce una credencial por el nombre de su hoja.
    /// </summary>
    /// <remarks>
    /// Se mira el ÚLTIMO segmento y por su final, no la ruta entera con un <c>Contains</c>: eso
    /// último marcaría <c>IdentityTokens:ActiveKeyId</c> y <c>LifetimeMinutes</c> —que no son
    /// secretos— y el gate nacería con un muro de excepciones, que es como se deja de leer
    /// (§7). <c>Key</c> a secas SÍ entra, aunque cueste la única entrada del censo: es lo que
    /// atrapa la familia más ancha, y una excepción es barata mientras sea una.
    /// </remarks>
    private static readonly string[] SufijosDeCredencial =
        ["Password", "Secret", "Token", "Key", "Credential", "Credentials"];

    /// <summary>
    /// Lo que PARECE una credencial por su nombre y no lo es, con su razón.
    /// </summary>
    /// <remarks>
    /// Vigilado en los DOS sentidos: una entrada que ya no corresponde rompe, porque un censo
    /// que sobra deja de leerse y acaba tapando lo que vino a vigilar
    /// (<c>feedback_a_census_entry_is_how_a_defect_survives_its_own_gate</c>). Y la razón de
    /// cada una contesta «por qué esto NO es un secreto», no «por qué todavía está acá»: lo
    /// segundo sería un ticket sin abrir disfrazado de exención.
    /// </remarks>
    private static readonly (string Ruta, string Razon)[] NoSonCredenciales =
    [
        ("Synergos:Branding:Key",
            "identifica la marca activa —`default`—, no autentica nada: es el mismo valor que " +
            "IBrandingProvider resuelve y que sale en el HTML servido"),
    ];

    private static string Raiz() => Proyectos.Raiz();

    private static bool PareceCredencial(string ruta)
    {
        var hoja = ruta.Split(':', '_').LastOrDefault(s => s.Length > 0) ?? ruta;
        return SufijosDeCredencial.Any(s => hoja.EndsWith(s, StringComparison.Ordinal));
    }

    /// <summary>Los appsettings versionados del host web, sin los schemas del vendedor.</summary>
    private static IReadOnlyList<string> Appsettings()
        => Directory
            .EnumerateFiles(Proyectos.Dir("Synergos.CMS.Web"), "appsettings*.json", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).Contains("schema", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    /// <summary>Toda hoja de texto de un appsettings, con su ruta en notación de sección.</summary>
    private static IEnumerable<(string Ruta, string Valor)> Hojas(JsonElement nodo, string prefijo = "")
    {
        switch (nodo.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in nodo.EnumerateObject())
                {
                    var ruta = prefijo.Length == 0 ? prop.Name : $"{prefijo}:{prop.Name}";
                    foreach (var h in Hojas(prop.Value, ruta)) yield return h;
                }
                break;

            case JsonValueKind.String:
                yield return (prefijo, nodo.GetString() ?? string.Empty);
                break;
        }
    }

    /// <summary>Las variables de entorno que el compose declara OBLIGATORIAS con <c>:?</c>.</summary>
    private static IReadOnlySet<string> Exigidas(string compose)
        => Regex.Matches(compose, @"\$\{([A-Za-z_][A-Za-z0-9_]*):\?", RegexOptions.None, TimeSpan.FromSeconds(2))
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Las filas <c>Clave: valor</c> del bloque <c>environment:</c> del compose.</summary>
    private static IReadOnlyList<(string Clave, string Valor)> FilasDelCompose(string compose)
        => Regex.Matches(
                compose,
                @"^\s{6}(?<k>[A-Za-z][A-Za-z0-9_]*):\s*(?<v>\S.*?)\s*$",
                RegexOptions.Multiline, TimeSpan.FromSeconds(2))
            .Select(m => (m.Groups["k"].Value, m.Groups["v"].Value))
            .ToList();

    private static string Compose() => File.ReadAllText(Path.Combine(Raiz(), "compose.prod.yml"));

    [Fact]
    public void El_gate_ve_lo_que_mide()
    {
        // Sin esto, un descubrimiento roto deja los tres dientes en verde sobre listas vacías.
        var settings = Appsettings();
        var filas = FilasDelCompose(Compose());

        Assert.True(
            settings.Count >= 3,
            $"Sólo se vieron {settings.Count} appsettings del host web y hay al menos tres " +
            "(base, Development y Docker). El descubrimiento está roto.");

        Assert.True(
            filas.Count >= 50,
            $"El bloque `environment:` de compose.prod.yml devolvió {filas.Count} filas y tiene " +
            "cientos. Si el parseo se rompió, los dientes de abajo pasan sin mirar nada.");

        Assert.Contains(filas, f => PareceCredencial(f.Clave));
    }

    [Fact]
    public void Ningun_appsettings_versionado_declara_una_credencial()
    {
        var censadas = NoSonCredenciales.Select(c => c.Ruta).ToHashSet(StringComparer.Ordinal);
        var malas = new List<string>();

        foreach (var fichero in Appsettings())
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(fichero));

            foreach (var (ruta, valor) in Hojas(doc.RootElement))
            {
                if (!PareceCredencial(ruta)) continue;
                if (censadas.Contains(ruta)) continue;
                if (string.IsNullOrWhiteSpace(valor)) continue;   // declarada y vacía: eso es correcto

                malas.Add($"{Path.GetFileName(fichero)} → `{ruta}` = «{valor}»");
            }
        }

        Assert.True(
            malas.Count == 0,
            "Hay credenciales escritas en el árbol:\n  " + string.Join("\n  ", malas) + "\n\n" +
            "`.env.example` lo dice en su cabecera: NINGÚN SECRETO ENTRA AL REPO, NI UNO. Y este " +
            "repo ya lo pagó — `appsettings.Docker.json` es el perfil con el que corre " +
            "PRODUCCIÓN, así que la contraseña del administrador del backoffice estuvo publicada " +
            "junto con su correo (#150). Sale del entorno: la pone `compose.prod.yml` con `:?` y " +
            "el arranque lanza si falta. Si de verdad no es un secreto, va al censo " +
            "`NoSonCredenciales` con su razón.");
    }

    [Fact]
    public void El_censo_no_declara_lo_que_ya_no_esta()
    {
        // El diente de vuelta. Sin él, una entrada del censo sobreviviría al valor que eximía y
        // se quedaría tapando una credencial futura que se llamara igual (#137).
        var rutas = new HashSet<string>(StringComparer.Ordinal);

        foreach (var fichero in Appsettings())
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(fichero));
            foreach (var (ruta, _) in Hojas(doc.RootElement)) rutas.Add(ruta);
        }

        var sobran = NoSonCredenciales.Where(c => !rutas.Contains(c.Ruta)).Select(c => c.Ruta).ToList();

        Assert.True(
            sobran.Count == 0,
            $"El censo exime rutas que ya no están en ningún appsettings: {string.Join(", ", sobran)}. " +
            "Una excepción que sobra deja de leerse, y la siguiente credencial que se llame igual " +
            "pasa por ella sin que nadie mire.");
    }

    [Fact]
    public void El_compose_de_produccion_no_escribe_ninguna_credencial_a_mano()
    {
        // La otra puerta: escribir el literal en `compose-gen.mjs` en vez de en un appsettings
        // sería el mismo secreto publicado, en otro fichero y sin que el primer diente lo vea.
        var literales = FilasDelCompose(Compose())
            .Where(f => PareceCredencial(f.Clave))
            .Where(f => !f.Valor.Contains("${", StringComparison.Ordinal))
            .Where(f => f.Valor is not ("\"\"" or "''"))
            .Select(f => $"`{f.Clave}` = {f.Valor}")
            .ToList();

        Assert.True(
            literales.Count == 0,
            "compose.prod.yml escribe credenciales a mano en vez de tomarlas del entorno:\n  " +
            string.Join("\n  ", literales) + "\n\n" +
            "El compose se genera (`tools/compose-gen.mjs`) y va al repo, así que un literal ahí " +
            "está tan publicado como uno en un appsettings. Toda credencial entra como " +
            "`${VARIABLE:?falta VARIABLE}`.");
    }

    [Fact]
    public void Ninguna_credencial_del_despliegue_puede_llegar_VACIA_sin_que_alguien_lo_decida()
    {
        // El tercer modo de fallo, y el más silencioso: la variable existe, nadie la rellena, y el
        // servidor arranca verde con la credencial en blanco. Es la llave de firma de
        // `Api.Identity` otra vez — contesta /health y rechaza al primero que intente entrar.
        var compose = Compose();
        var exigidas = Exigidas(compose);
        var mudas = new List<string>();

        foreach (var (clave, valor) in FilasDelCompose(compose))
        {
            if (!PareceCredencial(clave)) continue;

            foreach (Match m in Regex.Matches(
                         valor, @"\$\{(?<v>[A-Za-z_][A-Za-z0-9_]*)(?<mod>[:\-?][^}]*)?\}",
                         RegexOptions.None, TimeSpan.FromSeconds(2)))
            {
                var variable = m.Groups["v"].Value;
                var mod = m.Groups["mod"].Value;

                // Tres formas de estar bien, y las tres son decisiones de alguien:
                //   `:?`  — obligatoria aquí.
                //   `:-`  — opcional DECLARADA: la funcionalidad se apaga sin ella y el código lo
                //           dice al arrancar (Wompi, Resend, el sello de Academy…).
                //   sin nada, pero la MISMA variable exigida en otro sitio del fichero: docker
                //           compose resuelve el fichero entero, así que falta una vez y falla.
                if (mod.StartsWith(":?", StringComparison.Ordinal)) continue;
                if (mod.StartsWith(":-", StringComparison.Ordinal)) continue;
                if (exigidas.Contains(variable)) continue;

                mudas.Add($"`{clave}` usa `${{{variable}}}`");
            }
        }

        Assert.True(
            mudas.Count == 0,
            "Estas credenciales del compose pueden llegar VACÍAS sin que nada falle:\n  " +
            string.Join("\n  ", mudas.Distinct()) + "\n\n" +
            "Una credencial en blanco no rompe el arranque: el servidor sirve, contesta /health " +
            "y rechaza a la primera persona que intente usar eso — la forma de fallo que este " +
            "repo ya pagó con la llave de firma de `Api.Identity`. O la variable se exige acá " +
            "con `:?`, o se declara opcional con `:-` porque la funcionalidad se apaga sin ella, " +
            "o la misma variable ya se exige en otra fila del fichero.");
    }
}
