using System.Text.RegularExpressions;
using Synergos.Bff.Core;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Middlewares;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// La puerta de los flujos es GENÉRICA y su tabla es la del contrato que se despliega (ADR 0140 F3).
/// </summary>
/// <remarks>
/// <para><b>Cero código por vertical.</b> La puerta abre lo que los orquestadores marcan; el día que tenga
/// un <c>if (flujo == "eventos.compra")</c>, el siguiente vertical necesita tocarla y la regla de la ADR
/// se rompió sin que nada falle. Se mira el código sin comentarios: nombrar un vertical en una nota que
/// explica un porqué no es código por vertical.</para>
///
/// <para><b>Lo incrustado es lo del disco</b>, y el contexto de la imagen no lo deja fuera: con la carpeta
/// excluida, el comodín del <c>.csproj</c> no encuentra nada, el build sale verde, la tabla queda vacía y
/// la puerta contesta 404 a todo, en silencio y sólo en la imagen.</para>
/// </remarks>
public sealed class PuertaGenericaTests
{
    private static readonly Regex Vertical = new(
        @"\b(eventos?|tienda|salud|viajes?|gob|gobierno|realty|inmobiliari\w*|academ\w*|blogs?|compras?|tickets?|ofertas?|citas?|reservas?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    private static readonly Regex ClaveDeFlujo = new(@"""[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    private static IReadOnlyList<string> FuentesDeLaPuerta()
        =>
        [
            Proyectos.Dir("Synergos.CMS.Web", "Controllers", "FlujosController.cs"),
            Proyectos.Dir("Synergos.CMS.Web", "Composers", "SeamComposer.Puerta.cs"),
            .. Directory.EnumerateFiles(Proyectos.Dir("Synergos.CMS.Web", "Services", "Puerta"), "*.cs").Order(StringComparer.Ordinal),
        ];

    private static string SinComentarios(string ruta)
    {
        var sinBloques = Regex.Replace(File.ReadAllText(ruta), @"/\*[\s\S]*?\*/", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(5));
        return string.Join('\n', sinBloques.Split('\n').Select(l =>
        {
            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i < 0 ? l : l[..i];
        }));
    }

    [Fact]
    public void La_puerta_no_nombra_ningun_flujo_ni_ningun_vertical()
    {
        var fuentes = FuentesDeLaPuerta();
        Assert.True(fuentes.Count >= 6, $"Se encontraron {fuentes.Count} ficheros de la puerta: el descubrimiento está roto.");

        var culpables = new List<string>();
        foreach (var f in fuentes)
        {
            var codigo = SinComentarios(f);
            culpables.AddRange(Vertical.Matches(codigo).Select(m => $"{Path.GetFileName(f)}: «{m.Value}»"));
            culpables.AddRange(ClaveDeFlujo.Matches(codigo).Select(m => $"{Path.GetFileName(f)}: clave de flujo {m.Value}"));
        }

        Assert.True(culpables.Count == 0,
            "La puerta nombra un vertical o un flujo; lo que es de cada flujo va en su contrato o en "
            + "Synergos:Puerta:Flujos, no en su código:" + Environment.NewLine + string.Join(Environment.NewLine, culpables));
    }

    [Fact]
    public void El_techo_de_la_puerta_es_menor_que_el_corte_generico_del_CMS()
    {
        // Igual o mayor, el TimeoutMiddleware corta antes y el 504 sale sin código de la puerta.
        Assert.True(ReenvioDeLaPuerta.Techo < TimeoutMiddleware.DefaultTimeout,
            $"La puerta espera {ReenvioDeLaPuerta.Techo.TotalSeconds} s y el CMS corta a los {TimeoutMiddleware.DefaultTimeout.TotalSeconds} s.");
    }

    [Fact]
    public void Los_contratos_incrustados_son_los_del_disco_y_estan_todos()
    {
        var ensamblado = typeof(FlujosController).Assembly;
        var incrustados = TablaDeLaPuerta.Recursos(ensamblado);
        var enDisco = Directory.EnumerateFiles(Proyectos.Dir("Synergos.CMS.Web", "docs", "contracts", "openapi"), TablaDeLaPuerta.PrefijoDelOrquestador + "*.json")
            .Select(f => TablaDeLaPuerta.PrefijoDelRecurso + Path.GetFileName(f))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(enDisco);
        Assert.Equal(enDisco, incrustados);
        foreach (var recurso in incrustados)
        {
            using var s = ensamblado.GetManifestResourceStream(recurso)!;
            using var m = new MemoryStream();
            s.CopyTo(m);
            Assert.True(m.ToArray().AsSpan().SequenceEqual(File.ReadAllBytes(
                    Proyectos.Dir("Synergos.CMS.Web", "docs", "contracts", "openapi", recurso[TablaDeLaPuerta.PrefijoDelRecurso.Length..]))),
                $"{recurso} incrustado no es el del disco: compilá Synergos.CMS.Web.");
        }

        // Y la tabla se arma con ellos: un contrato que la puerta no sabe pasar no deja arrancar.
        var tabla = TablaDeLaPuerta.Incrustada();
        Assert.Equal(incrustados.Count, tabla.Orquestadores.Count);
    }

    [Fact]
    public void El_contexto_de_la_imagen_no_deja_fuera_los_contratos()
    {
        const string contrato = "Synergos.CMS.Web/docs/contracts/openapi/Synergos.Bff.Eventos.json";
        var tramos = contrato.Split('/');
        var rutas = Enumerable.Range(1, tramos.Length).Select(n => string.Join('/', tramos.Take(n))).ToList();

        var excluyen = File.ReadAllLines(Proyectos.Ruta(".dockerignore"))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#') && !l.StartsWith('!'))
            .Where(patron => rutas.Any(r => Coincide(patron.TrimEnd('/'), r)))
            .ToList();

        Assert.True(excluyen.Count == 0,
            $".dockerignore deja fuera {contrato} ({string.Join(", ", excluyen)}): la puerta de la imagen saldría sin tabla.");
    }

    /// <summary>Un patrón de <c>.dockerignore</c> contra una ruta del contexto: <c>**</c>, <c>*</c> y <c>?</c>.</summary>
    private static bool Coincide(string patron, string ruta)
    {
        var regex = "^" + Regex.Escape(patron.TrimStart('/'))
            .Replace(@"\*\*/", "(.*/)?", StringComparison.Ordinal)
            .Replace(@"\*\*", ".*", StringComparison.Ordinal)
            .Replace(@"\*", "[^/]*", StringComparison.Ordinal)
            .Replace(@"\?", "[^/]", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(ruta, regex, RegexOptions.None, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Las_cabeceras_el_vocabulario_y_la_llave_de_la_puerta_son_los_del_orquestador()
    {
        // Se comparte el NOMBRE y no el código: el CMS no referencia Bff.Core.
        Assert.Equal(CabecerasDeLaPuerta.Sujeto, LoQuePoneLaPuerta.CabeceraDelSujeto);
        Assert.Equal(CabecerasDeLaPuerta.Negocio, LoQuePoneLaPuerta.CabeceraDelNegocio);
        Assert.Equal(CabecerasDeLaPuerta.Contacto, LoQuePoneLaPuerta.CabeceraDelContacto);
        Assert.Equal(EnLaPuertaExtensions.Operaciones, TablaDeLaPuerta.Operaciones);
        Assert.True(LoQuePoneLaPuerta.LargoDeLaLlave <= LlaveDeSaga.MaxLength,
            $"La llave reemitida mide {LoQuePoneLaPuerta.LargoDeLaLlave} y la que abre una saga admite {LlaveDeSaga.MaxLength}.");
        Assert.Equal(LoQuePoneLaPuerta.LargoDeLaLlave, LoQuePoneLaPuerta.LlaveReemitida("k:1", "a.b", "x").Length);
    }
}
