using Synergos.Shared;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// La afirmación de identidad la decide la CAPACIDAD, no el llamador (HU #14).
/// </summary>
/// <remarks>
/// <para><b>Este gate existe porque una mutación no se puso roja.</b> Los tests de
/// <c>IdentityAssertions.Resolve</c> cubren la regla, pero ninguno tocaba el ENDPOINT — así que
/// quitarle la lectura de la cabecera dejaba el cableado muerto y todo en verde: la capacidad
/// volvería a creerle al llamador y nadie se enteraría.</para>
///
/// <para><b>Y es el defecto más caro de los posibles acá</b>, porque no rompe nada: el acuse se
/// sigue registrando, solo que otra vez con la fuerza que el llamador diga. El archivo volvería a
/// mentir en silencio, que es exactamente lo que #42 acaba de arreglar.</para>
/// </remarks>
public sealed class IdentityGateTests
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

    /// <summary>El fichero sin comentarios: la prosa explica el código, no lo es.</summary>
    private static string SinComentarios(string ruta)
    {
        Assert.True(File.Exists(ruta), $"No existe {ruta}: revisar este gate.");
        return string.Join('\n', File.ReadAllLines(ruta).Select(l =>
        {
            var t = l.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal))
            {
                return string.Empty;
            }
            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? l[..i] : l;
        }));
    }

    private static string Acuse()
    {
        var codigo = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.Api.Messaging", "Endpoints", "MessagingEndpoints.cs"));

        var desde = codigo.IndexOf("/v1/messages/{id}/acknowledge", StringComparison.Ordinal);
        Assert.True(desde > 0, "El endpoint del acuse cambió de forma: revisar este gate.");

        var hasta = codigo.IndexOf("        });", desde, StringComparison.Ordinal);
        Assert.True(hasta > desde, "No se pudo delimitar el endpoint del acuse: revisar este gate.");
        return codigo[desde..hasta];
    }

    [Fact]
    public void El_acuse_LEE_el_token_y_deja_que_la_capacidad_decida()
    {
        var cuerpo = Acuse();

        // Sin esto, el cableado está muerto y nada más lo nota.
        Assert.Contains("IdentityTokens.HeaderName", cuerpo, StringComparison.Ordinal);
        Assert.Contains("IdentityAssertions.Resolve(", cuerpo, StringComparison.Ordinal);
    }

    /// <summary>
    /// La afirmación que se registra NO es la que vino en el cuerpo de la petición.
    /// </summary>
    /// <remarks>
    /// Es la mitad que de verdad importa: leer la cabecera y después registrar igual lo declarado
    /// sería el mismo agujero con más código. Lo que se pasa al servicio tiene que ser lo
    /// resuelto.
    /// </remarks>
    [Fact]
    public void Lo_que_se_registra_es_lo_RESUELTO_y_no_lo_declarado()
    {
        var cuerpo = Acuse();

        Assert.Contains("svc.Acknowledge(id, who, assertion)", cuerpo, StringComparison.Ordinal);
        // `declarada` es lo que dijo el llamador: puede leerse, pero no puede ser lo que se anota.
        Assert.DoesNotContain("svc.Acknowledge(id, who, declarada)", cuerpo, StringComparison.Ordinal);
    }

    /// <summary>
    /// Los dos endpoints del consentimiento que ESCRIBEN, delimitados.
    /// </summary>
    private static string Consentimiento(string ruta)
    {
        var codigo = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.Api.Consent", "Endpoints", "ConsentEndpoints.cs"));

        var desde = codigo.IndexOf(ruta, StringComparison.Ordinal);
        Assert.True(desde > 0, $"El endpoint {ruta} cambió de forma: revisar este gate.");

        var hasta = codigo.IndexOf("        });", desde, StringComparison.Ordinal);
        Assert.True(hasta > desde, $"No se pudo delimitar {ruta}: revisar este gate.");
        return codigo[desde..hasta];
    }

    /// <summary>
    /// El consentimiento RESUELVE la identidad; no se cree lo declarado (HU #14, rebanada 5).
    /// </summary>
    /// <remarks>
    /// <para><b>Este gate existe porque la primera versión de esta rebanada pasaba en verde con
    /// el defecto puesto.</b> Reemplazar la llamada del borde por «anotar lo que declaró el
    /// llamador» no ponía rojo nada: los tests cubrían el helper compartido y el servicio, y el
    /// que decide es el <b>cableado</b> entre los dos, que no tenía quien lo mirara. Es
    /// exactamente el defecto #42 —creerle al llamador— vuelto a aparecer un piso más arriba.</para>
    ///
    /// <para><b>Y va sobre los DOS endpoints que escriben.</b> Retirar el permiso de otro es tan
    /// grave como darlo en su nombre; un gate que sólo mirara <c>grants</c> dejaría
    /// <c>grants/revoke</c> como la puerta de atrás.</para>
    /// </remarks>
    [Theory]
    [InlineData("/v1/grants\"")]
    [InlineData("/v1/grants/revoke")]
    // Eran TRES, no dos (defecto #83). El razonamiento de abajo era correcto y la LISTA se
    // escribió a mano y se quedó corta: `forget` retira TODOS los permisos de una persona y era
    // el único que no pedía nada. Es el mismo error que «los seis de Synergos.Shared» y que
    // «faltan las otras 16» — una enumeración sacada de la cabeza en vez de medida contra el
    // fichero. Por eso ahora hay además un test que CUENTA los endpoints que escriben.
    [InlineData("/v1/grants/forget")]
    public void El_consentimiento_RESUELVE_la_identidad_y_no_se_cree_lo_declarado(string ruta)
    {
        var cuerpo = Consentimiento(ruta);

        // Se LEE lo que vino, y se resuelve: sin esto el cableado está muerto.
        Assert.Contains("Afirmacion(identidad, http", cuerpo, StringComparison.Ordinal);

        // Y lo que baja al servicio es lo RESUELTO. `req.Assertion` es lo que dijo el llamador:
        // puede leerse, pero no puede ser lo que se anota.
        Assert.Contains("assertion.Value", cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain("req.Assertion)", cuerpo, StringComparison.Ordinal);
    }

    /// <summary>
    /// El helper del borde usa el resolutor compartido y no una copia propia.
    /// </summary>
    /// <remarks>
    /// La regla de «sólo se acepta a la baja» vive en <c>Synergos.Shared</c> porque la comparten
    /// las capacidades que aceptan identidad. Una copia local se desviaría de la original el día
    /// que la original cambie — y la desviación sería silenciosa, porque las dos compilan.
    /// </remarks>
    [Fact]
    public void El_consentimiento_no_reimplementa_la_regla_de_la_afirmacion()
    {
        var codigo = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.Api.Consent", "Endpoints", "ConsentEndpoints.cs"));

        Assert.Contains("IdentityAssertions.Resolve(", codigo, StringComparison.Ordinal);
        Assert.Contains("IdentityTokens.HeaderName", codigo, StringComparison.Ordinal);

        // Y NO decide por su cuenta qué se acepta sin prueba: eso es de la regla compartida.
        Assert.DoesNotContain("assertion_not_proven", codigo, StringComparison.Ordinal);
    }

    /// <summary>
    /// La sección de configuración del token se llama IGUAL en todos los servicios.
    /// </summary>
    /// <remarks>
    /// La llave de firma es la misma para quien emite y para quien verifica. Con un nombre por
    /// servicio, configurarla en uno y olvidarla en otro produce un token válido que una
    /// capacidad rechaza — de los peores síntomas de diagnosticar, porque todo «parece bien».
    /// </remarks>
    [Fact]
    public void La_seccion_del_token_es_la_MISMA_en_todos()
    {
        var raiz = RepoRoot();
        var hosts = Directory.EnumerateDirectories(raiz, "Synergos.Api.*")
            .Concat(Directory.EnumerateDirectories(raiz, "Synergos.Bff.*"))
            .Select(d => Path.Combine(d, "Program.cs"))
            .Where(File.Exists)
            .ToList();

        Assert.True(hosts.Count >= 22, $"Solo se encontraron {hosts.Count} hosts; el árbol tiene más.");

        // Y al menos uno la usa: si nadie llamara a AddIdentityTokens, lo de abajo pasaría
        // vigilando el vacío.
        var conTokens = hosts
            .Where(h => SinComentarios(h).Contains("AddIdentityTokens", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(conTokens);

        // Nadie lee una sección de tokens PROPIA. Se busca `GetSection("…Tokens…")`, que es la
        // forma de saltarse el helper compartido — y no `"Identity` a secas, porque
        // `Identity:Storage` es el almacén legítimo de Api.Identity y no tiene nada que ver.
        var culpables = hosts
            .Where(h => SinComentarios(h)
                .Split("GetSection(\"", StringSplitOptions.None)
                .Skip(1)
                .Any(fragmento => fragmento[..fragmento.IndexOf('"')]
                    .Contains("Tokens", StringComparison.OrdinalIgnoreCase)))
            .Select(h => Path.GetFileName(Path.GetDirectoryName(h)))
            .ToList();

        Assert.True(culpables.Count == 0,
            "Hay hosts que leen la llave de firma de una sección propia en vez de la compartida "
            + $"('{IdentityTokenSetup.Section}'): {string.Join(", ", culpables)}. La llave es la "
            + "misma para quien emite y para quien verifica; con dos nombres, configurar una y "
            + "olvidar la otra da un token válido que una capacidad rechaza.");
    }

    /// <summary>
    /// Y el despliegue tiene que pasar la llave por ESA misma sección.
    /// </summary>
    /// <remarks>
    /// <para><b>Este gate existe porque el compose ya se desincronizó de verdad.</b> Nombraba
    /// <c>Identity__Tokens__*</c> de cuando la sección era propia de <c>Api.Identity</c>; al
    /// pasar a la compartida, el generador se quedó atrás y nadie lo vio — el gate de arriba mira
    /// código C# y el defecto vivía en un <c>.mjs</c>.</para>
    ///
    /// <para><b>El síntoma habría sido el peor posible</b> hasta la rebanada 3: la llave llegaba
    /// a una sección que nadie lee, así que un servidor con <c>SYNERGOS_IDENTITY_SIGNING_KEY</c>
    /// bien puesta se comportaba como uno sin llave.</para>
    /// </remarks>
    [Fact]
    public void El_despliegue_pasa_la_llave_por_la_seccion_compartida()
    {
        var compose = Path.Combine(RepoRoot(), "compose.prod.yml");
        Assert.True(File.Exists(compose), "No existe compose.prod.yml: se genera con tools/compose-gen.mjs.");

        var lineas = File.ReadAllLines(compose)
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith('#') && l.Contains("Tokens", StringComparison.Ordinal)
                        && l.Contains("__", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(lineas);

        var malNombradas = lineas
            .Where(l => !l.StartsWith($"{IdentityTokenSetup.Section}__", StringComparison.Ordinal))
            .ToList();

        Assert.True(malNombradas.Count == 0,
            $"El compose pasa la llave por una sección que no es '{IdentityTokenSetup.Section}': "
            + string.Join(" · ", malNombradas)
            + ". La variable llegaría a una sección que nadie lee, así que un servidor con la "
            + "llave bien puesta se comportaría exactamente como uno sin llave.");

        // Y quien EMITE la exige: sin `:?` un despliegue sin llave arrancaría, que es justo lo
        // que la rebanada 3 acaba de impedir en el arranque.
        Assert.Contains(lineas, l =>
            l.StartsWith($"{IdentityTokenSetup.Section}__Keys__", StringComparison.Ordinal)
            && l.Contains(":?", StringComparison.Ordinal));
    }

    /// <summary>
    /// Todo endpoint de <c>Api.Consent</c> que ESCRIBE resuelve identidad — medido, no listado.
    /// </summary>
    /// <remarks>
    /// <para><b>Este gate existe porque el de arriba se quedó corto</b> (defecto #83). Su
    /// razonamiento era correcto —«los endpoints que escriben»— y su enumeración estaba escrita a
    /// mano: eran tres y decía dos, así que el derecho al olvido quedó de puerta de atrás
    /// justamente debajo del comentario que explicaba por qué no debía haberla. Es el mismo error
    /// que ya costó «los seis de <c>Synergos.Shared</c>» y «faltan las otras 16»: <b>una lista
    /// derivada de la cabeza en vez de medida contra el fichero</b>.</para>
    ///
    /// <para><b>Y «escribe» tampoco se lista: se deduce.</b> El criterio no puede ser
    /// <c>MapPost</c> —<c>/v1/grants/check</c> es una LECTURA que usa POST a propósito, para que
    /// el sujeto y el propósito no queden en la URL ni en los logs de un proxy—. Así que el gate
    /// mira a qué método del servicio llama cada endpoint y si ese método toca el almacén. Un
    /// endpoint nuevo que escriba rompe el build hasta que resuelva identidad, y uno que sólo lea
    /// no molesta a nadie — que es lo que hace que este gate sobreviva.</para>
    /// </remarks>
    [Fact]
    public void Ningun_endpoint_que_escribe_se_queda_fuera()
    {
        var endpoints = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.Api.Consent", "Endpoints", "ConsentEndpoints.cs"));
        var servicio = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.Api.Consent", "Domain", "ConsentService.cs"));

        var rutas = System.Text.RegularExpressions.Regex
            .Matches(endpoints, @"app\.MapPost\(""(?<ruta>[^""]+)""")
            .Select(m => m.Groups["ruta"].Value)
            .ToList();

        Assert.NotEmpty(rutas);
        var comprobados = 0;

        foreach (var ruta in rutas)
        {
            var i = endpoints.IndexOf($"MapPost(\"{ruta}\"", StringComparison.Ordinal);
            var fin = endpoints.IndexOf("app.Map", i + 1, StringComparison.Ordinal);
            var cuerpo = fin > i ? endpoints[i..fin] : endpoints[i..];

            var llamadas = System.Text.RegularExpressions.Regex
                .Matches(cuerpo, @"svc\.(?<metodo>[A-Z]\w*)\(")
                .Select(m => m.Groups["metodo"].Value)
                .Distinct()
                .ToList();

            if (!llamadas.Any(m => Escribe(servicio, m))) continue;

            comprobados++;
            Assert.True(cuerpo.Contains("Afirmacion(identidad, http", StringComparison.Ordinal),
                $"'{ruta}' escribe ({string.Join(", ", llamadas)}) y no resuelve identidad: " +
                "es la puerta de atrás que este gate existe para cerrar.");
        }

        // Si un renombrado dejara al gate sin nada que mirar, pasaría en verde vigilando cero.
        Assert.True(comprobados >= 3,
            $"El gate sólo encontró {comprobados} endpoints de escritura; eran al menos tres (#83).");
    }

    /// <summary>Si ese método del servicio toca el almacén.</summary>
    private static bool Escribe(string servicio, string metodo)
    {
        var i = servicio.IndexOf($" {metodo}(", StringComparison.Ordinal);
        if (i < 0) return false;

        var fin = servicio.IndexOf("\n    public ", i + 1, StringComparison.Ordinal);
        var cuerpo = fin > i ? servicio[i..fin] : servicio[i..];
        return cuerpo.Contains("_store.Put(", StringComparison.Ordinal);
    }

    /// <summary>
    /// Quién ESCRIBE un mensaje se comprueba igual que quién lo acusa (defecto #81).
    /// </summary>
    /// <remarks>
    /// <para>El mismo <c>who</c>, en la misma capacidad, estaba verificado en un endpoint y creído
    /// en el otro — dieciséis líneas más arriba. Y lo que <c>CheckPost</c> comprueba es otra cosa:
    /// si ese remitente participa del hilo, que es autorización y no autenticación.</para>
    ///
    /// <para>Importa porque la HU #62 puso el cuerpo de un acto administrativo en un mensaje de
    /// hilo, y no hay <c>PUT</c> ni <c>DELETE</c>: un acto notificado con autor falsificable es el
    /// defecto #72 sobre el documento que sostiene un plazo legal.</para>
    /// </remarks>
    [Fact]
    public void El_mensaje_RESUELVE_quien_lo_escribe()
    {
        var texto = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.Api.Messaging", "Endpoints", "MessagingEndpoints.cs"));

        var i = texto.IndexOf("MapPost(\"/v1/threads/{id}/messages\"", StringComparison.Ordinal);
        Assert.True(i > 0, "Cambió la ruta de publicar mensaje: revisar este gate.");

        var fin = texto.IndexOf("app.MapGet(", i, StringComparison.Ordinal);
        var cuerpo = fin > i ? texto[i..fin] : texto[i..];

        Assert.Contains("IdentityAssertions.Resolve(", cuerpo, StringComparison.Ordinal);

        // Y lo que baja al servicio es lo RESUELTO, no lo declarado: resolver bien y guardar lo
        // que dijo el llamador es la mitad que hizo invisible el arreglo en #72.
        Assert.Contains("assertion.Value", cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain("req.Assertion)", cuerpo, StringComparison.Ordinal);
    }

    /// <summary>
    /// Y el mensaje GUARDA con qué se afirmó su autor.
    /// </summary>
    /// <remarks>
    /// Sin esto el arreglo sería invisible: los mensajes nuevos valdrían más que los viejos y nada
    /// lo diría. Es la mitad que faltó en #42 y que #72 tuvo que añadir después.
    /// </remarks>
    [Fact]
    public void El_mensaje_guarda_con_que_se_afirmo_su_autor()
    {
        var dominio = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.Api.Messaging", "Domain", "Thread.cs"));
        Assert.Contains("IdentityAssertion? PostedWith", dominio, StringComparison.Ordinal);

        var servicio = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.Api.Messaging", "Domain", "MessagingService.cs"));

        // El acuse automático del autor lleva la afirmación RESUELTA, no una constante: con una
        // constante, un mensaje escrito presentando token quedaría anotado como si sólo lo
        // respaldara nuestra palabra.
        Assert.Contains("new Acknowledgment(from, ahora, assertion)", servicio, StringComparison.Ordinal);
        Assert.DoesNotContain("new Acknowledgment(from, ahora, IdentityAssertion.", servicio, StringComparison.Ordinal);
    }
}
