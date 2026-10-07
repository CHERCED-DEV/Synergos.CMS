using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// La forma de cómo se publica el contrato HTTP (ADR 0140, F2): el paquete que lo genera vive en
/// UNA suite, y ningún endpoint lo declara a mano.
/// </summary>
/// <remarks>
/// <para><b>El paquete, sólo en Synergos.Servicios.Tests.</b> El documento se genera ahí, desde el
/// host real, y producción no lleva ni el paquete ni la ruta. Meter
/// <c>Microsoft.AspNetCore.OpenApi</c> en un servicio no rompe nada a la vista y trae dos cosas
/// medidas: su generador de comentarios XML (descripciones con CRLF en Windows, y cada comentario
/// editado se vuelve deriva del contrato) y una superficie nueva que proteger. Y
/// <c>Microsoft.Extensions.ApiDescription.Server</c> genera en el build ejecutando el
/// <c>Program</c> en Production, donde la llave compartida lo tumba (16 errores, medido).</para>
///
/// <para><b>Ningún <c>.Produces*</c> en el backend.</b> La respuesta se declara con el TIPO DE
/// RETORNO (<c>TypedResults</c>), que el compilador comprueba. Un <c>.Produces&lt;T&gt;(201)</c> es un
/// segundo modelo escrito a mano: medido, un endpoint que lo declaraba y devolvía otra forma dejaba
/// el documento mintiendo con todos los gates en verde; con <c>TypedResults</c> el mismo cambio no
/// compila. Se mira todo el código de producción del backend y no sólo <c>Endpoints/</c>, porque
/// <c>/health</c> se mapea en el <c>Program</c>.</para>
///
/// <para><b>Sólo lee el disco.</b> Con red de seguridad en las dos: si el descubrimiento no ve la
/// referencia que SÍ existe, o no ve los ficheros que mapean rutas, falla en vez de pasar mirando
/// nada.</para>
/// </remarks>
public sealed class ContratoHttpPublicadoTests
{
    /// <summary>La única suite que puede generar el documento.</summary>
    private const string QuienGenera = "Synergos.Servicios.Tests";

    /// <summary>Los paquetes que generan OpenAPI: en ejecución y en el build.</summary>
    private static readonly string[] PaquetesQueGeneran =
    [
        "Microsoft.AspNetCore.OpenApi",
        "Microsoft.Extensions.ApiDescription.Server",
    ];

    private static Regex Referencia(string paquete) => new(
        @"<PackageReference\s+(?:Include|Update)\s*=\s*""" + Regex.Escape(paquete) + @"""",
        RegexOptions.IgnoreCase, TimeSpan.FromSeconds(5));

    /// <summary>Una llamada a <c>.Produces</c>, <c>.ProducesProblem</c>, <c>.ProducesValidationProblem</c>…</summary>
    private static readonly Regex Produces = new(@"\.Produces\w*\s*[<(]", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    private static readonly Regex Mapea = new(@"\.Map(Get|Post|Delete|Put|Patch|Methods)\s*\(", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    private static bool FueraDelBuild(string ruta)
        => ruta.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(
            s => s.Equals("bin", StringComparison.OrdinalIgnoreCase) || s.Equals("obj", StringComparison.OrdinalIgnoreCase));

    /// <summary>La fuente sin comentarios: una nota que CITE <c>.Produces&lt;T&gt;()</c> no es una llamada.</summary>
    private static string SinComentarios(string fuente)
    {
        var s = Regex.Replace(fuente, @"/\*[\s\S]*?\*/", "", RegexOptions.None, TimeSpan.FromSeconds(5));
        return string.Join('\n', s.Split('\n').Select(l =>
        {
            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? l[..i] : l;
        }));
    }

    [Fact]
    public void El_paquete_que_genera_el_contrato_solo_va_en_la_suite_que_lo_genera()
    {
        var raiz = Proyectos.Raiz();

        // Todo csproj del repo, más lo que se aplica a TODOS: una PackageReference en
        // Directory.Build.props la heredarían las veinticuatro piezas sin tocar su csproj.
        var ficheros = Proyectos.Nombres(string.Empty)
            .Select(n => (Nombre: n, Ruta: Path.Combine(Proyectos.Dir(n), n + ".csproj")))
            .Concat(new[] { "Directory.Build.props", "Directory.Build.targets" }
                .Select(f => (Nombre: f, Ruta: Path.Combine(raiz, f)))
                .Where(f => File.Exists(f.Ruta)))
            .ToList();

        Assert.True(ficheros.Count >= 35,
            $"Se descubrieron {ficheros.Count} ficheros de proyecto y hay al menos 35: el descubrimiento dejó de ver.");

        var genera = File.ReadAllText(Path.Combine(Proyectos.Dir(QuienGenera), QuienGenera + ".csproj"));
        Assert.True(Referencia(PaquetesQueGeneran[0]).IsMatch(genera),
            $"{QuienGenera} no referencia {PaquetesQueGeneran[0]}: o el contrato dejó de generarse, o " +
            "este gate dejó de reconocer una PackageReference — y entonces lo de abajo pasaría sin mirar.");

        var fuera = (
            from f in ficheros
            where f.Nombre != QuienGenera
            let texto = File.ReadAllText(f.Ruta)
            from p in PaquetesQueGeneran
            where Referencia(p).IsMatch(texto)
            select $"  {Path.GetRelativePath(raiz, f.Ruta).Replace('\\', '/')}: {p}").ToList();

        Assert.True(fuera.Count == 0,
            $"El contrato HTTP se genera SÓLO en {QuienGenera}, desde el host real (ADR 0140). En un " +
            "servicio, el paquete trae su generador de comentarios XML (CRLF en Windows, deriva por " +
            "comentario) y una ruta que proteger; ApiDescription.Server ejecuta el Program en " +
            $"Production y la llave lo tumba:{Environment.NewLine}{string.Join(Environment.NewLine, fuera)}");
    }

    [Fact]
    public void Ningun_endpoint_del_backend_declara_su_respuesta_con_Produces()
    {
        var backend = Path.Combine(Proyectos.Raiz(), "backend");
        var suite = Proyectos.Dir(QuienGenera) + Path.DirectorySeparatorChar;

        var fuentes = Directory.EnumerateFiles(backend, "*.cs", SearchOption.AllDirectories)
            .Where(f => !FueraDelBuild(f) && !f.StartsWith(suite, StringComparison.OrdinalIgnoreCase))
            .Select(f => (Ruta: f, Texto: SinComentarios(File.ReadAllText(f))))
            .ToList();

        var mapean = fuentes.Count(f => Mapea.IsMatch(f.Texto));
        Assert.True(mapean >= 24,
            $"Sólo {mapean} ficheros del backend mapean rutas, y son al menos 24 (las 20 capacidades y " +
            "los 4 orquestadores): el descubrimiento dejó de ver.");

        var conProduces = fuentes
            .Where(f => Produces.IsMatch(f.Texto))
            .Select(f => "  " + Path.GetRelativePath(backend, f.Ruta).Replace('\\', '/'))
            .ToList();

        Assert.True(conProduces.Count == 0,
            "La respuesta de un endpoint se declara con su TIPO DE RETORNO (TypedResults), que el " +
            "compilador comprueba. Un .Produces* es un segundo modelo escrito a mano: si el endpoint " +
            "devuelve otra forma, el documento miente y todo sigue en verde (medido, ADR 0140 F2)." +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, conProduces)}");
    }
}
