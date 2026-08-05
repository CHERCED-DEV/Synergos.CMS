namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// La emisión de una entrada tiene UN SOLO dueño, como invariante ejecutable.
/// </summary>
/// <remarks>
/// <para><b>El defecto que este gate impide es de los que no duelen hasta que duelen mucho.</b>
/// La emisión estaba fundida con el motor de compra: proyectaba desde la unidad que el propio
/// checkout había persistido. Cuando un segundo camino de compra no cree esa unidad —porque el
/// cobro y el aforo se fueron a un orquestador— la salida barata es copiar cuatro líneas de
/// formato de QR al fichero nuevo. Nadie lo nota, porque las dos copias firman igual… hasta que
/// una cambia. Un QR con dos definiciones se firma de dos maneras un martes cualquiera, y las
/// entradas emitidas por el camino viejo dejan de abrir la puerta.</para>
///
/// <para><b>Lo que se vigila, entonces, es el sitio y no la forma:</b> quién puede construir un
/// <c>TicketToken</c> y quién puede escribir el prefijo del id de una entrada. Verificar NO está
/// prohibido y no debería estarlo — pasa en la puerta, sobre un token que este proceso puede no
/// haber emitido nunca, y es exactamente lo contrario de emitir.</para>
///
/// <para><b>Y el borde de los dos árboles:</b> el orquestador de Eventos mueve aforo y plata, no
/// emite el artefacto. No tiene el firmante ni debe tenerlo — la llave vive del lado del
/// contenido. Que empiece a nombrar entradas sería el primer paso hacia dos firmantes.</para>
/// </remarks>
public sealed class EventTicketIssuanceTests
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

    /// <summary>Los únicos ficheros que pueden hablar del token de un QR, y por qué.</summary>
    private static readonly string[] Autorizados =
    {
        "EventTicketIssuer.cs",        // el emisor: es su trabajo
        "HmacTicketSigner.cs",         // el firmante: lo reconstruye al VERIFICAR, no al emitir
        "TicketSigningKeyProvider.cs", // pasamanos perezoso hacia el firmante real
    };

    private static IReadOnlyList<string> CodigoDeProduccion(params string[] proyectos)
    {
        var raiz = RepoRoot();
        var ficheros = proyectos
            .SelectMany(p => Directory.EnumerateFiles(Path.Combine(raiz, p), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        // "No encontré nada" no puede parecerse a "está todo bien": si el barrido se queda sin
        // ficheros —un rename de carpeta, un path mal armado— el gate pasaría en verde vigilando
        // el vacío. Es el mismo error que ya se cometió dos veces en este repo.
        Assert.NotEmpty(ficheros);
        return ficheros;
    }

    /// <summary>El mismo fichero, <b>sin comentarios</b>.</summary>
    /// <remarks>
    /// Para lo que se PROHÍBE daría igual —un comentario solo puede provocar un rojo de más, que
    /// es el lado seguro—, pero para lo que se EXIGE es imprescindible: la prosa de estos
    /// ficheros nombra al emisor, así que un gate que buscara <c>EventTicketIssuer</c> sobre el
    /// texto crudo seguiría en verde con la delegación deshecha. Ya pasó con el registro del
    /// barrido (HU #29) y con el cliente de visitas (HU #33a).
    /// </remarks>
    private static string SinComentarios(string ruta)
        => string.Join('\n', File.ReadAllLines(ruta).Select(l =>
        {
            var t = l.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal))
            {
                return string.Empty;
            }
            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? l[..i] : l;
        }));

    [Fact]
    public void El_token_del_QR_se_construye_en_UN_SOLO_sitio()
    {
        var culpables = new List<string>();

        foreach (var fichero in CodigoDeProduccion("Synergos.CMS.Application", "Synergos.CMS.Web", "Synergos.CMS.Interfaces"))
        {
            if (Autorizados.Contains(Path.GetFileName(fichero), StringComparer.Ordinal))
            {
                continue;
            }
            if (SinComentarios(fichero).Contains("new TicketToken(", StringComparison.Ordinal))
            {
                culpables.Add(Path.GetFileName(fichero));
            }
        }

        Assert.True(culpables.Count == 0,
            "Solo el emisor arma el token que va dentro del QR. Firmar desde otro sitio es tener "
            + "dos formatos que hoy coinciden y un martes dejan de coincidir. Lo hacen: "
            + string.Join(", ", culpables));
    }

    [Fact]
    public void El_nombre_de_una_entrada_lo_pone_UN_SOLO_sitio()
    {
        var culpables = new List<string>();

        foreach (var fichero in CodigoDeProduccion("Synergos.CMS.Application", "Synergos.CMS.Web", "Synergos.CMS.Interfaces"))
        {
            if (Autorizados.Contains(Path.GetFileName(fichero), StringComparer.Ordinal))
            {
                continue;
            }
            if (SinComentarios(fichero).Contains("\"tkt_", StringComparison.Ordinal))
            {
                culpables.Add(Path.GetFileName(fichero));
            }
        }

        Assert.True(culpables.Count == 0,
            "El id de una entrada se deriva con EventTicketIssuer.TicketIdOf y en ningún otro "
            + "lado. Dos derivaciones son dos entradas distintas para el mismo apartado. Lo "
            + "hacen: " + string.Join(", ", culpables));
    }

    /// <summary>
    /// El registro proyecta a través del emisor, y el motor de compra ni siquiera proyecta.
    /// </summary>
    /// <remarks>
    /// Son dos afirmaciones y las dos importan. <c>new EventTicket(</c> en cualquiera de los dos
    /// es la extracción deshecha aunque el emisor siga existiendo al lado — un segundo formato
    /// vivo, que es justo lo que este fichero existe para impedir. Y que el motor de compra no
    /// proyecte es lo que hace que cambiar por dónde se compra no toque el artefacto.
    /// </remarks>
    [Fact]
    public void Solo_el_REGISTRO_proyecta_una_entrada()
    {
        var impl = Path.Combine(RepoRoot(), "Synergos.CMS.Application", "Services", "Impl");

        var registro = SinComentarios(Path.Combine(impl, "EventTicketLedger.cs"));
        Assert.Contains("_issuer.Issue(", registro, StringComparison.Ordinal);
        Assert.DoesNotContain("new EventTicket(", registro, StringComparison.Ordinal);

        var motorDeCompra = SinComentarios(Path.Combine(impl, "StubEventTicketingService.cs"));
        Assert.DoesNotContain("new EventTicket(", motorDeCompra, StringComparison.Ordinal);
        Assert.DoesNotContain("EventTicketFacts(", motorDeCompra, StringComparison.Ordinal);
    }

    /// <summary>
    /// La puerta lee el registro, no el motor de compra — y lee EL MISMO que se escribió.
    /// </summary>
    /// <remarks>
    /// <para><b>El defecto que esto impide no rompía nada hasta el día del cambio, y ese día
    /// rompía en silencio.</b> La cara de organizador colgaba del motor de compra concreto, así
    /// que cablear <c>IEventTicketingService</c> a otro camino habría dejado el escáner leyendo
    /// un almacén que nadie escribió: las entradas existirían, la puerta diría <c>invalid</c>, y
    /// ni el build ni un test lo habrían dicho.</para>
    ///
    /// <para>La segunda mitad es igual de importante: un registro por consumidor tiene
    /// exactamente el mismo efecto que el acople anterior. Por eso se comprueba que el composer
    /// arme UNO y se lo dé a los dos.</para>
    /// </remarks>
    [Fact]
    public void La_puerta_lee_el_registro_COMPARTIDO_y_no_el_motor_de_compra()
    {
        var cara = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.CMS.Application", "Services", "Impl", "StubEventManagementService.cs"));

        Assert.DoesNotContain("StubEventTicketingService", cara, StringComparison.Ordinal);
        Assert.Contains("EventTicketLedger", cara, StringComparison.Ordinal);

        var composer = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.CMS.Web", "Composers", "SeamComposer.EventsPropertiesGov.cs"));

        // Uno solo, y singleton: dos registros sobre el mismo almacén funcionarían hoy por
        // casualidad y dejarían de hacerlo el día que uno de ellos cachee algo.
        var construcciones = composer.Split("new EventTicketLedger(", StringSplitOptions.None).Length - 1;
        Assert.True(construcciones == 1,
            $"El registro de entradas se arma {construcciones} veces en el composer; tiene que ser una.");
        Assert.Contains("AddSingleton(sp => new EventTicketLedger(", composer, StringComparison.Ordinal);

        // Y es el que recibe la cara de organizador, no un motor de compra.
        var desde = composer.IndexOf("new StubEventManagementService(", StringComparison.Ordinal);
        Assert.True(desde >= 0, "El composer ya no arma la cara de organizador: revisar este gate.");
        var argumentos = composer[desde..(composer.IndexOf("));", desde, StringComparison.Ordinal) + 3)];
        Assert.Contains("EventTicketLedger", argumentos, StringComparison.Ordinal);
        Assert.DoesNotContain("StubEventTicketingService", argumentos, StringComparison.Ordinal);
    }

    [Fact]
    public void El_orquestador_de_Eventos_NO_nombra_entradas()
    {
        var culpables = new List<string>();

        foreach (var fichero in CodigoDeProduccion("Synergos.Bff.Eventos"))
        {
            var codigo = SinComentarios(fichero);
            if (codigo.Contains("tkt_", StringComparison.Ordinal)
                || codigo.Contains("SYN-TKT", StringComparison.Ordinal))
            {
                culpables.Add(Path.GetFileName(fichero));
            }
        }

        Assert.True(culpables.Count == 0,
            "El orquestador mueve aforo y plata; el artefacto lo emite el CMS, que es donde vive "
            + "el firmante. Nombrar entradas desde acá es el primer paso hacia dos firmantes. Lo "
            + "hacen: " + string.Join(", ", culpables));
    }
}
