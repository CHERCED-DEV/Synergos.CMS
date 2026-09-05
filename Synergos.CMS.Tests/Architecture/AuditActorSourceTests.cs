namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El actor de un asiento NO se construye en el endpoint con lo que trae el cuerpo (#72).
/// </summary>
/// <remarks>
/// <para><b>Es el defecto #48 en la capacidad cuyo valor entero es ser creíble</b>, y el más
/// silencioso de los tres de su familia (#42, #48, éste): un asiento falso no falla — se guarda,
/// para siempre, con aspecto de prueba.</para>
///
/// <para><b>Por qué hace falta un gate y no bastan los tests de reglas.</b> <c>AuditRules</c>
/// siempre estuvo bien: rechaza el actor anónimo, la acción vacía, el detalle enorme. Lo que
/// estaba mal era que el endpoint armaba el <c>Actor</c> con <c>req.ActorRoles</c>. Un test de
/// la regla no puede ver eso, y el arreglo se deshace en una línea sin que ninguno se ponga
/// rojo.</para>
///
/// <para><b>Y acá el arreglo se puede deshacer de dos maneras</b>, así que se vigilan las dos:
/// volver a armar el actor con el cuerpo, o resolverlo bien y después no guardar con qué se
/// afirmó — que dejaría los asientos nuevos indistinguibles de los viejos.</para>
/// </remarks>
public sealed class AuditActorSourceTests
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
    private static string SinComentarios(params string[] partes)
    {
        var ruta = Path.Combine(new[] { RepoRoot() }.Concat(partes).ToArray());
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

    private static string Endpoints() => SinComentarios("Synergos.Api.Audit", "Endpoints", "AuditEndpoints.cs");

    /// <summary>El endpoint lee la cabecera y delega quién actúa.</summary>
    [Fact]
    public void El_endpoint_delega_la_resolucion_del_actor()
    {
        var codigo = Endpoints();

        Assert.Contains("IdentityTokens.HeaderName", codigo, StringComparison.Ordinal);
        Assert.Contains("IdentityAssertions.ResolveActor(", codigo, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nadie arma un <c>Actor</c> con los roles del cuerpo.
    /// </summary>
    /// <remarks>
    /// Es la línea exacta del defecto: <c>Actor.Of(principal, (req.ActorRoles ?? …).ToArray())</c>.
    /// Leer la cabecera y después armar el actor igual con lo declarado sería el mismo agujero
    /// con más código.
    /// </remarks>
    [Fact]
    public void El_endpoint_NO_arma_el_actor_con_los_roles_del_cuerpo()
    {
        var codigo = Endpoints();

        Assert.DoesNotContain("Actor.Of(", codigo, StringComparison.Ordinal);
        Assert.Contains("quien.Value.Actor", codigo, StringComparison.Ordinal);
    }

    /// <summary>
    /// Lo que se resolvió se GUARDA, o el arreglo es invisible.
    /// </summary>
    /// <remarks>
    /// Sin la afirmación en el asiento, un registro respaldado por un token y otro que sólo lleva
    /// la palabra de quien lo escribió se leen igual — y como la bitácora no se puede reescribir,
    /// ese hueco no se arregla después.
    /// </remarks>
    [Fact]
    public void La_afirmacion_resuelta_llega_al_asiento()
    {
        Assert.Contains("quien.Value.Assertion", Endpoints(), StringComparison.Ordinal);

        var entrada = SinComentarios("Synergos.Api.Audit", "Domain", "AuditEntry.cs");
        Assert.Contains("IdentityAssertion? ActedWith", entrada, StringComparison.Ordinal);

        // Nullable, y con default: los asientos anteriores dicen «no consta», que es la verdad.
        Assert.Contains("ActedWith = null", entrada, StringComparison.Ordinal);
    }

    /// <summary>
    /// La bitácora arranca SIN llave de verificación, y no por descuido.
    /// </summary>
    /// <remarks>
    /// Con <c>required: true</c>, un despliegue sin <c>Api.Identity</c> no podría auditar — y
    /// parar la bitácora cuando falla la identidad convierte una caída en un hueco en el
    /// registro, que es peor que un asiento débil. Sin llave, un token presentado se rechaza; lo
    /// que no pasa es que se ignore.
    /// </remarks>
    [Fact]
    public void Sin_llave_se_sigue_auditando()
    {
        var programa = SinComentarios("Synergos.Api.Audit", "Program.cs");

        Assert.Contains("AddIdentityTokens(required: false)", programa, StringComparison.Ordinal);
    }
}
