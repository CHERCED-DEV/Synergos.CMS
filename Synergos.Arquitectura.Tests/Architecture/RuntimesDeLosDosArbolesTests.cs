using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Los dos árboles corren en DOS runtimes, y cada imagen lleva el de su árbol (ADR 0140, F2).
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta, si el compilador ya ve casi todo.</b> Desde la F2 el backend va
/// en net10.0 y el CMS se queda en net8.0 por el pin de Umbraco 13 (ADR 0001). El compilador
/// cuida una dirección —un net8.0 no puede referenciar un net10.0 (NU1201)—, y por eso ve una
/// capacidad devuelta a net8.0 (referencia Core y Shared) o el árbol del CMS entero subido a
/// net10.0 (hoy rompe con SYSLIB0060); medido las dos veces. La otra dirección queda abierta:
/// <c>Synergos.Core</c>, que no referencia nada, vuelto a net8.0 compila la integradora en cero
/// avisos —un net10.0 referencia un net8.0 sin quejarse— y sólo lo ve este gate. Y la versión
/// del <c>FROM … AS runtime</c> no la lee nadie: <c>ContainerBuildTests</c> y
/// <c>PoliticaDeBuildEnLaImagenTests</c> leen <c>Dockerfile.service</c> pero no ese dato. Una
/// imagen <c>aspnet:8.0</c> con ensamblados net10.0 se construye igual (el publish se hace en la
/// etapa del SDK) y no arranca; sin Docker en local, sólo lo vería el job <c>arranca</c> de
/// <c>images.yml</c>, que prueba 2 de las 25.</para>
///
/// <para><b>Sólo lee el disco</b>: los csproj y los dos Dockerfile. Los proyectos se descubren
/// (<see cref="Proyectos"/>) y no se nombran, salvo <c>Synergos.CMS.Web</c>, que es la referencia
/// del runtime del CMS, y <c>Synergos.Arquitectura.Tests</c>, que es la excepción con nombre: mira
/// los dos árboles y por eso corre en el mayor de los dos runtimes.</para>
///
/// <para><b>Red de seguridad</b>: si el descubrimiento devuelve menos proyectos de los que hay, o
/// un Dockerfile no tiene su etapa <c>runtime</c>, el gate falla. Pasar sobre una lista vacía es
/// el verde que el #136 midió.</para>
/// </remarks>
public sealed class RuntimesDeLosDosArbolesTests
{
    /// <summary>Hoy son 29: 2 del núcleo, 20 capacidades, 6 orquestadores y Servicios.Tests.</summary>
    private const int MinimoDelBackend = 29;

    /// <summary>Hoy son 5: Web, Application, Interfaces, CMS.Tests y Benchmarks.</summary>
    private const int MinimoDelCms = 5;

    /// <summary>El runtime del CMS mientras siga el pin de Umbraco 13 (ADR 0001).</summary>
    private const string RuntimeDelCms = "net8.0";

    /// <summary>
    /// <c>TargetFramework</c> o <c>TargetFrameworks</c>: un multi-target tiene que salir con su
    /// lista entera, para que «net8.0;net10.0» no se lea como un net10.0.
    /// </summary>
    private static readonly Regex Tfm = new(
        @"<TargetFrameworks?>\s*(?<v>[^<]+?)\s*</TargetFrameworks?>", RegexOptions.Compiled);

    /// <summary>
    /// La etapa de RUNTIME de un Dockerfile. Se mira la línea entera y sin comentarios, para que
    /// una nota que cite «aspnet:8.0» no cuente como la imagen.
    /// </summary>
    private static readonly Regex FromRuntime = new(
        @"^FROM\s+\S*dotnet/aspnet:(?<v>\d+\.\d+)\S*\s+AS\s+runtime$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VersionDelTfm = new(@"^net(?<v>\d+\.\d+)$", RegexOptions.Compiled);

    private sealed record Csproj(string Nombre, string Relativa, string? Tfm, bool TienePrograma);

    private static IReadOnlyList<Csproj> TodosLosCsproj()
    {
        var raiz = Proyectos.Raiz();
        return Proyectos.Nombres(string.Empty)
            .Select(n =>
            {
                var dir = Proyectos.Dir(n);
                var ruta = Path.Combine(dir, n + ".csproj");
                var m = Tfm.Match(File.ReadAllText(ruta));
                return new Csproj(
                    n,
                    Path.GetRelativePath(raiz, ruta).Replace('\\', '/'),
                    m.Success ? m.Groups["v"].Value : null,
                    File.Exists(Path.Combine(dir, "Program.cs")));
            })
            .ToList();
    }

    private static bool BajoBackend(Csproj c) => c.Relativa.StartsWith("backend/", StringComparison.Ordinal);

    private static string Lista(IEnumerable<Csproj> csproj)
        => string.Join(Environment.NewLine, csproj.Select(c => $"  {c.Relativa}: {c.Tfm ?? "(sin TargetFramework)"}"));

    /// <summary>La versión de la etapa <c>runtime</c> de un Dockerfile de la raíz, o rojo.</summary>
    private static string RuntimeDe(string dockerfile)
    {
        var ruta = Path.Combine(Proyectos.Raiz(), dockerfile);
        Assert.True(File.Exists(ruta), $"No existe {dockerfile} en la raíz del repo.");

        var etapas = File.ReadAllLines(ruta)
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith('#'))
            .Select(l => FromRuntime.Match(l))
            .Where(m => m.Success)
            .Select(m => m.Groups["v"].Value)
            .ToList();

        Assert.True(etapas.Count == 1,
            $"{dockerfile} tiene {etapas.Count} etapa(s) `FROM …/dotnet/aspnet:<versión> AS runtime` " +
            "y tiene que tener una. Sin ella este gate no puede cruzar la imagen con el " +
            "TargetFramework, y pasar en verde sería no mirar.");
        return etapas[0];
    }

    private static string VersionDe(string tfm, string quien)
    {
        var m = VersionDelTfm.Match(tfm);
        Assert.True(m.Success,
            $"{quien} declara «{tfm}», que no es un `netX.Y` de un solo runtime: este gate no sabe " +
            "con qué imagen aspnet cruzarlo.");
        return m.Groups["v"].Value;
    }

    /// <summary>
    /// Todo csproj bajo <c>backend/</c> declara el MISMO <c>TargetFramework</c>.
    /// </summary>
    /// <remarks>
    /// Una pieza que referencia el núcleo no puede bajar de runtime sin que lo diga NU1201; lo que
    /// el compilador no ve es que baje el que no referencia nada (<c>Synergos.Core</c> en net8.0
    /// compila todo en verde, medido) o que alguien añada un multi-target. Un único TFM es además
    /// lo que permite que <c>Dockerfile.service</c> siga siendo UNO para las 25.
    /// </remarks>
    [Fact]
    public void Todo_csproj_del_backend_declara_el_mismo_TargetFramework()
    {
        var backend = TodosLosCsproj().Where(BajoBackend).ToList();

        Assert.True(backend.Count >= MinimoDelBackend,
            $"Se descubrieron {backend.Count} csproj bajo backend/ y hay al menos {MinimoDelBackend}. " +
            "El descubrimiento dejó de ver: revisar este gate antes que los csproj.");

        var tfms = backend.Select(c => c.Tfm).Distinct(StringComparer.Ordinal).ToList();
        Assert.True(tfms.Count == 1 && tfms[0] is not null,
            $"Los {backend.Count} csproj del backend declaran {tfms.Count} TargetFramework distintos y " +
            "tiene que ser uno (ADR 0140, F2): con dos, una pieza compila y su imagen —que es la " +
            $"misma para todas— lleva un runtime que no es el suyo.{Environment.NewLine}{Lista(backend)}");
    }

    /// <summary>
    /// El árbol del CMS —todo csproj fuera de <c>backend/</c> salvo Arquitectura.Tests— sigue en
    /// net8.0 (ADR 0001).
    /// </summary>
    /// <remarks>
    /// Umbraco 13 es net8.0; pasar el CMS a otro runtime es la decisión que la ADR 0001 reserva a un
    /// ADR que la suceda, y el día que exista este gate se mueve en el MISMO commit. Mientras tanto,
    /// que el backend haya subido no puede arrastrar al CMS por un buscar-y-reemplazar.
    /// </remarks>
    [Fact]
    public void El_arbol_del_CMS_sigue_en_net8_por_la_ADR_0001()
    {
        var cms = TodosLosCsproj()
            .Where(c => !BajoBackend(c) && c.Nombre != "Synergos.Arquitectura.Tests")
            .ToList();

        Assert.True(cms.Count >= MinimoDelCms && cms.Any(c => c.Nombre == "Synergos.CMS.Web"),
            $"Se descubrieron {cms.Count} csproj del árbol del CMS y hay al menos {MinimoDelCms}, " +
            "Synergos.CMS.Web entre ellos. El descubrimiento dejó de ver.");

        var fuera = cms.Where(c => c.Tfm != RuntimeDelCms).ToList();
        Assert.True(fuera.Count == 0,
            $"El árbol del CMS va en {RuntimeDelCms} por el pin de Umbraco 13 (ADR 0001), y " +
            $"{fuera.Count} csproj declaran otra cosa:{Environment.NewLine}{Lista(fuera)}{Environment.NewLine}" +
            "Subir el CMS de runtime es un ADR nuevo que suceda a la 0001, no un efecto de subir el backend.");
    }

    /// <summary>
    /// La imagen de los servicios lleva el runtime del backend: el <c>FROM … AS runtime</c> de
    /// <c>Dockerfile.service</c> coincide con el TFM de cada pieza que esa imagen construye.
    /// </summary>
    [Fact]
    public void La_imagen_de_los_servicios_lleva_el_runtime_del_backend()
    {
        var imagen = RuntimeDe("Dockerfile.service");

        // Lo que construye Dockerfile.service: las piezas del backend con Program.cs.
        var piezas = TodosLosCsproj().Where(c => BajoBackend(c) && c.TienePrograma).ToList();
        Assert.True(piezas.Count >= 24,
            $"Se descubrieron {piezas.Count} piezas del backend con Program.cs y hay 24 " +
            "(20 capacidades y 4 orquestadores). El descubrimiento dejó de ver.");

        var otras = piezas
            .Where(c => c.Tfm is null || VersionDe(c.Tfm, c.Relativa) != imagen)
            .ToList();
        Assert.True(otras.Count == 0,
            $"Dockerfile.service arranca las piezas sobre aspnet:{imagen} y {otras.Count} declaran otro " +
            "runtime. El publish se hace en la etapa del SDK, así que la imagen se construye igual y " +
            $"no arranca.{Environment.NewLine}{Lista(otras)}");
    }

    /// <summary>
    /// La imagen del CMS lleva el runtime de <c>Synergos.CMS.Web</c>.
    /// </summary>
    [Fact]
    public void La_imagen_del_CMS_lleva_el_runtime_de_CMS_Web()
    {
        var imagen = RuntimeDe("Dockerfile");

        var web = TodosLosCsproj().SingleOrDefault(c => c.Nombre == "Synergos.CMS.Web");
        Assert.True(web?.Tfm is not null,
            "No se descubrió Synergos.CMS.Web con su TargetFramework: sin él no hay contra qué " +
            "cruzar la imagen del CMS, y pasar en verde sería no mirar.");
        var version = VersionDe(web!.Tfm!, web.Relativa);

        Assert.True(version == imagen,
            $"El Dockerfile del CMS arranca sobre aspnet:{imagen} y Synergos.CMS.Web declara " +
            $"{web.Tfm}. Cada imagen sigue a su árbol: la del CMS al de Umbraco 13 (ADR 0001), la de " +
            "los servicios al del backend (ADR 0140).");
    }
}
