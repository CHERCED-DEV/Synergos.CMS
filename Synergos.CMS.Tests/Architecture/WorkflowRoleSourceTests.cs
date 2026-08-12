namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Los roles de quien dispara NO se construyen en el endpoint (defecto #48).
/// </summary>
/// <remarks>
/// <para><b>Este gate existe porque el defecto era invisible para los tests de reglas.</b>
/// <c>WorkflowRules.Resolve</c> siempre estuvo bien; lo que estaba mal era que el endpoint armaba
/// el <c>Actor</c> con lo que traía el cuerpo. Un test de la regla no puede ver eso, y el arreglo
/// se puede deshacer en una línea sin que ninguno se ponga rojo.</para>
///
/// <para><b>Y es el peor defecto posible de los de su clase</b>, porque no rompe nada: la
/// transición se aplica igual, sólo que la guarda dejó de guardar. Nadie se entera.</para>
/// </remarks>
public sealed class WorkflowRoleSourceTests
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
            if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal)
                || t.StartsWith("///", StringComparison.Ordinal))
            {
                return string.Empty;
            }
            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? l[..i] : l;
        }));
    }

    private static string Endpoints() => SinComentarios(Path.Combine(
        RepoRoot(), "Synergos.Api.Workflow", "Endpoints", "WorkflowEndpoints.cs"));

    /// <summary>
    /// El endpoint LEE la cabecera y deja que el dominio decida de dónde salen los roles.
    /// </summary>
    [Fact]
    public void El_endpoint_delega_la_resolucion_del_actor_al_dominio()
    {
        var codigo = Endpoints();

        Assert.Contains("IdentityTokens.HeaderName", codigo, StringComparison.Ordinal);
        Assert.Contains("WorkflowRules.ResolveActor(", codigo, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nadie arma un <c>Actor</c> con los roles del cuerpo.
    /// </summary>
    /// <remarks>
    /// Es la línea exacta del defecto: <c>Actor.Of(principal, req.ActorRoles…)</c>. Leer la
    /// cabecera y después armar el actor igual con lo declarado sería el mismo agujero con más
    /// código.
    /// </remarks>
    [Fact]
    public void El_endpoint_NO_arma_el_actor_con_los_roles_del_cuerpo()
    {
        var codigo = Endpoints();

        Assert.DoesNotContain("Actor.Of(", codigo, StringComparison.Ordinal);

        // Y lo que se le pasa al servicio es lo RESUELTO, no lo declarado.
        Assert.Contains("quien.Value.Actor", codigo, StringComparison.Ordinal);
        Assert.Contains("quien.Value.Verified", codigo, StringComparison.Ordinal);
    }

    /// <summary>
    /// La postura del despliegue se LEE de su sección, no se cablea.
    /// </summary>
    /// <remarks>
    /// Con el valor cableado, encender la exigencia de roles verificados sería un despliegue de
    /// la capacidad — y eso es justo lo que <c>Api.Workflow</c> existe para no obligar a hacer.
    /// </remarks>
    [Fact]
    public void La_postura_sale_de_la_configuracion()
    {
        Assert.Contains("WorkflowRoleOptions", Endpoints(), StringComparison.Ordinal);

        var programa = SinComentarios(Path.Combine(RepoRoot(), "Synergos.Api.Workflow", "Program.cs"));
        Assert.Contains("Configure<WorkflowRoleOptions>(", programa, StringComparison.Ordinal);
        Assert.Contains("AddIdentityTokens(", programa, StringComparison.Ordinal);

        // Quien sólo verifica arranca sin llave: es el camino del clon limpio.
        Assert.Contains("AddIdentityTokens(required: false)", programa, StringComparison.Ordinal);
    }
}
