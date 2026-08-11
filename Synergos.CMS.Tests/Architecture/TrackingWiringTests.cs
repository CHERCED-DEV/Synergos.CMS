namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Contra qué se valida el avance de un pedido, y con qué definición (HU #46).
/// </summary>
/// <remarks>
/// <para>Lo que vigila es lo que, roto, <b>no rompe nada visiblemente</b>: que los cuatro dominios
/// sigan teniendo cada uno su definición, que leer el timeline no salga a la red, y que el stub
/// siga siendo el default.</para>
/// </remarks>
public sealed class TrackingWiringTests
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
        RepoRoot(), "Synergos.CMS.Web", "Services", "HttpOrderTrackingService.cs"));

    private static string Fabrica() => SinComentarios(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "Composers", "SeamComposer.Tracking.cs"));

    /// <summary>
    /// Los CUATRO dominios se registran, y cada uno con su propio dominio de definición.
    /// </summary>
    /// <remarks>
    /// <para>Los nombres de estado se repiten entre pipelines: <c>paid</c> está en tres,
    /// <c>confirmed</c> en dos, <c>completed</c> en dos. Con una definición compartida, la etapa
    /// de un dominio se leería contra el pipeline de otro — «enviado» convertido en
    /// «matriculado» <b>sin que nada falle</b>, que es la peor forma de romperse.</para>
    ///
    /// <para>Se lee de los composers y no de una lista escrita acá: así, un quinto dominio que
    /// llegue y reuse el nombre de otro se cae solo.</para>
    /// </remarks>
    [Fact]
    public void Cada_dominio_pide_su_propia_definicion()
    {
        var raiz = RepoRoot();
        var dominios = new List<string>();

        foreach (var composer in Directory.EnumerateFiles(
                     Path.Combine(raiz, "Synergos.CMS.Web", "Composers"), "SeamComposer.*.cs"))
        {
            var codigo = SinComentarios(composer);
            var desde = 0;
            while ((desde = codigo.IndexOf("Tracking(sp,", desde, StringComparison.Ordinal)) >= 0)
            {
                var hasta = codigo.IndexOf(')', desde);
                Assert.True(hasta > desde, $"Llamada a Tracking() sin cerrar en {Path.GetFileName(composer)}.");

                // El último argumento es el dominio.
                var args = codigo[(desde + "Tracking(sp,".Length)..hasta].Split(',');
                dominios.Add(args[^1].Trim().Trim('"'));
                desde = hasta;
            }
        }

        Assert.Equal(4, dominios.Count);
        Assert.Equal(dominios.Count, dominios.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// Leer el timeline NO sale a la red.
    /// </summary>
    /// <remarks>
    /// Se pinta en cada vista de pedido, y es la promesa entera de este cableado: con la capacidad
    /// caída, quien compró sigue viendo dónde va lo suyo. Es deliberadamente lo contrario de la
    /// HU #44 — allá el riesgo era <i>decidir</i> con un proceso que quizá ya no es el vigente;
    /// acá es <i>mostrar</i> lo que ya pasó, que no decide nada.
    /// </remarks>
    [Fact]
    public void Leer_el_timeline_no_sale_a_la_red()
    {
        var cliente = Cliente();

        var desde = cliente.IndexOf("GetTimelineAsync(string orderRef", StringComparison.Ordinal);
        Assert.True(desde > 0, "GetTimelineAsync cambió de forma: revisar este gate.");

        var hasta = cliente.IndexOf("public async Task<OrderTimeline> AdvanceAsync", desde, StringComparison.Ordinal);
        Assert.True(hasta > desde, "No se pudo delimitar la lectura: revisar este gate.");

        var cuerpo = cliente[desde..hasta];
        Assert.Contains("_local.GetTimelineAsync", cuerpo, StringComparison.Ordinal);
        foreach (var red in new[] { "http", "Client()", "v1/" })
        {
            Assert.DoesNotContain(red, cuerpo, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>El stub sigue siendo el default: un clon limpio avanza sin levantar nada.</summary>
    [Fact]
    public void El_stub_sigue_siendo_el_default()
    {
        var fabrica = Fabrica();

        var decision = fabrica.IndexOf("EsModoApi(ajustes.Value.Mode)", StringComparison.Ordinal);
        Assert.True(decision > 0, "La fábrica ya no decide el modo: revisar este gate.");

        // Sin modo Api se devuelve el motor local, antes de construir ningún cliente.
        Assert.Contains("return local;", fabrica, StringComparison.Ordinal);
        Assert.True(
            fabrica.IndexOf("return local;", StringComparison.Ordinal)
            < fabrica.IndexOf("new HttpOrderTrackingService(", StringComparison.Ordinal),
            "El camino por defecto tiene que salir ANTES de construir el cliente.");

        // Y se compara contra "Api", no contra "Bff": el destino es la capacidad.
        Assert.Contains("\"Api\"", fabrica, StringComparison.Ordinal);
    }

    /// <summary>
    /// El motor local NO se reemplaza: se envuelve.
    /// </summary>
    /// <remarks>
    /// Es lo que hace que las fechas sigan viviendo en el CMS y que la lectura no dependa de
    /// nadie. Un cliente que construyera su propio almacén dejaría dos verdades sobre el mismo
    /// pedido.
    /// </remarks>
    [Fact]
    public void El_motor_local_se_envuelve_y_no_se_reemplaza()
    {
        var cliente = Cliente();

        Assert.Contains("_local.AdvanceAsync", cliente, StringComparison.Ordinal);
        // El cliente no conoce ningún almacén: las fechas no son suyas.
        Assert.DoesNotContain("IJsonEntityStore", cliente, StringComparison.Ordinal);
        Assert.DoesNotContain("StubOrderTrackingService", cliente, StringComparison.Ordinal);
    }
}
