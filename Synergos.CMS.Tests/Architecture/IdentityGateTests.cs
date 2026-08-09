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
}
