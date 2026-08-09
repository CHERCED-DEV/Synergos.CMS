namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Contra qué avanza un expediente, y contra qué NO (HU #44).
/// </summary>
/// <remarks>
/// <para>Tres cosas que el compilador no puede vigilar y que, rotas, no rompen nada
/// visiblemente:</para>
///
/// <list type="number">
///   <item>que la tabla de transiciones no vuelva a estar de este lado — copiarla es peor que no
///   haberla mudado, porque un trámite avanzaría distinto según a quién se le pregunte;</item>
///   <item>que el camino HTTP no caiga al motor en proceso cuando la capacidad no responde;</item>
///   <item>que el stub siga siendo el default, que es el camino del clon limpio.</item>
/// </list>
/// </remarks>
public sealed class GobWiringTests
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

    private static string Cliente() => SinComentarios(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "Services", "HttpCaseWorkflowService.cs"));

    private static string Composer() => SinComentarios(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "Composers", "SeamComposer.EventsPropertiesGov.cs"));

    /// <summary>
    /// La tabla de transiciones NO vuelve a este lado.
    /// </summary>
    /// <remarks>
    /// <para>Es la HU entera. El destino de un <c>outcome</c> se lee de la definición que sirve
    /// <c>Api.Workflow</c>; una tabla local «para saber a dónde lleva approve» dejaría el proceso
    /// en dos sitios, y el día que uno cambiara sólo cambiaría uno.</para>
    ///
    /// <para>Se mira que el cliente no nombre los estados internos: si los conociera, ya estaría
    /// decidiendo con ellos. Los que sí puede nombrar salen de traducir lo que la capacidad
    /// devuelve — y esa traducción vive en <c>GovStatusSlugs</c>, no acá.</para>
    /// </remarks>
    [Fact]
    public void El_cliente_NO_lleva_la_tabla_de_transiciones()
    {
        var cliente = Cliente();

        foreach (var estado in new[] { "CaseStatus.Resuelto", "CaseStatus.Rechazado", "CaseStatus.Subsanacion" })
        {
            Assert.DoesNotContain(estado, cliente, StringComparison.Ordinal);
        }

        // Y el destino se resuelve leyendo las transiciones de la definición.
        Assert.Contains("definicion.Transitions", cliente, StringComparison.Ordinal);
        Assert.Contains("v1/definitions/", cliente, StringComparison.Ordinal);
    }

    /// <summary>
    /// Va a la CAPACIDAD, no a un orquestador.
    /// </summary>
    /// <remarks>
    /// Decidir es UN paso y no hay plata en medio: no queda nada a medias que deshacer. Un BFF
    /// sería una saga de un paso — la máquina de compensar sin nada que compensar. Es la misma
    /// decisión que la visita al inmueble (#33a) y la contraria a la entrada de un evento (#35),
    /// y la pregunta que las separa no es cuántas capacidades toca.
    /// </remarks>
    [Fact]
    public void Se_habla_con_la_capacidad_y_no_con_un_BFF()
    {
        var cliente = Cliente();

        Assert.DoesNotContain("Bff", cliente, StringComparison.Ordinal);
        Assert.DoesNotContain("saga", cliente, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("v1/instances", cliente, StringComparison.Ordinal);
    }

    /// <summary>
    /// Con la capacidad caída NO se decide con la tabla local.
    /// </summary>
    /// <remarks>
    /// Caer al stub en silencio convertiría una caída en decisiones tomadas con un proceso que
    /// quizá ya no es el vigente, y nadie se enteraría — lo mismo que la HU #27 impidió con los
    /// cobros. Lo que no existe es el stub sirviendo sin avisar.
    /// </remarks>
    [Fact]
    public void El_camino_HTTP_no_cae_al_motor_en_proceso()
    {
        var cliente = Cliente();

        Assert.DoesNotContain("StubCaseWorkflowService", cliente, StringComparison.Ordinal);

        // Y ningún `catch` devuelve un expediente. El único legítimo es el que trata un cuerpo de
        // error ilegible —y ése cae al código de estado, no a una decisión—; uno que devolviera
        // sería exactamente el silencio que esto vigila: la caída se vería como un trámite
        // resuelto. Se mira el bloque entero de cada catch, no la línea.
        var bloques = cliente.Split("catch", StringSplitOptions.None).Skip(1);
        foreach (var bloque in bloques)
        {
            // El cierre del bloque está a la sangría de un cuerpo de método (8 espacios): cortar
            // en la de la clase se comería el código de después y el gate miraría de más.
            var hasta = bloque.IndexOf("\n        }", StringComparison.Ordinal);
            var cuerpo = hasta > 0 ? bloque[..hasta] : bloque;
            Assert.DoesNotContain("return", cuerpo, StringComparison.Ordinal);
            Assert.DoesNotContain("_recorder", cuerpo, StringComparison.Ordinal);
        }
    }

    /// <summary>El stub sigue siendo el default: un clon limpio decide sin levantar nada.</summary>
    [Fact]
    public void El_stub_sigue_siendo_el_default()
    {
        var composer = Composer();

        var condicion = composer.IndexOf("\"Synergos:Gob:Mode\"", StringComparison.Ordinal);
        Assert.True(condicion > 0, "El composer ya no decide el modo de Gobierno: revisar este gate.");

        // La rama condicional es la que cablea; el else, el motor en proceso.
        var rama = composer[condicion..];
        var otra = rama.IndexOf("else", StringComparison.Ordinal);
        Assert.True(otra > 0, "Sin rama else no hay camino por defecto: el clon limpio se quedaría sin motor.");

        Assert.Contains("HttpCaseWorkflowService(", rama[..otra], StringComparison.Ordinal);
        Assert.Contains("StubCaseWorkflowService(", rama[otra..], StringComparison.Ordinal);

        // Y se compara contra "Api", no contra "Bff": el destino es la capacidad.
        Assert.Contains("\"Api\"", rama[..otra], StringComparison.Ordinal);
    }

    /// <summary>La sección se ENLAZA, o la clave de la definición se queda en su default.</summary>
    /// <remarks>
    /// Sin <c>Configure&lt;GobSettings&gt;</c> el cliente recibe uno recién construido, y lo que
    /// no viaja por el <c>HttpClient</c> —la clave de la definición y el <c>Kind</c> del
    /// expediente— se queda en su valor por defecto <b>en silencio</b>. Un despliegue que publicó
    /// <c>gov.tramite.v2</c> seguiría decidiendo contra la v1 sin que nada avisara.
    /// </remarks>
    [Fact]
    public void La_seccion_de_Gobierno_se_enlaza()
    {
        Assert.Contains("Configure<GobSettings>(", Composer(), StringComparison.Ordinal);
        Assert.Contains("\"Synergos:Gob\"", Composer(), StringComparison.Ordinal);
    }
}
