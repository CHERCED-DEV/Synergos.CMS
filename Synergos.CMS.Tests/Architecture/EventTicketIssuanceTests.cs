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

    [Fact]
    public void El_motor_de_compra_DELEGA_la_emision()
    {
        var codigo = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.CMS.Application", "Services", "Impl", "StubEventTicketingService.cs"));

        Assert.Contains("_issuer.Issue(", codigo, StringComparison.Ordinal);

        // Y no arma el artefacto por su cuenta. Es la mitad que de verdad importa: `new
        // EventTicket(` acá es la extracción deshecha, aunque el emisor siga existiendo al lado.
        Assert.DoesNotContain("new EventTicket(", codigo, StringComparison.Ordinal);
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
