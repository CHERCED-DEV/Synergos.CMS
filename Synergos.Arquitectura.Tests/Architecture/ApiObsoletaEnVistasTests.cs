namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Ninguna vista llama a una API marcada <c>[Obsolete]</c>, porque en caliente eso no es un aviso:
/// es un 500.
/// </summary>
/// <remarks>
/// <para><b>Qué pasó.</b> <c>UmbracoHelper.GetDictionaryValue(string, string)</c> está obsoleta
/// desde Umbraco 13 («Use GetDictionaryValueOrDefault instead, scheduled for removal in v14»).
/// Eso es <c>CS0618</c>, un aviso — salvo que las vistas se compilan <b>en caliente</b> y ese
/// compilador lee del <c>deps.json</c> el <c>warningsAsErrors: true</c> del #134. Entonces el
/// aviso se vuelve error y la página revienta. Medido el 2026-09-17: <b>206 llamadas en 41
/// vistas</b>, con el sitio público entero en 500 y el build en 0 avisos.</para>
///
/// <para><b>La sobrecarga de UN argumento no está obsoleta</b>, y hay 35 llamadas vivas. Por eso
/// este gate cuenta <b>argumentos</b> en vez de buscar el nombre: prohibirlo entero pediría 35
/// cambios que no arreglan nada, y un gate que pide trabajo inútil se acaba desactivando.</para>
///
/// <para><b>Por qué un gate y no confiar en el build.</b> <c>dotnet build</c> no compila ninguna
/// vista (<c>RazorCompileOnBuild=false</c>, obligado por <c>ModelsMode=InMemoryAuto</c>), así que
/// esto no sale ahí nunca. Lo destapó el log de <c>ssr-dom-check</c> pidiendo
/// <c>/account/login</c> — y sólo después de arreglar que ese log llegara a existir.</para>
/// </remarks>
public sealed class ApiObsoletaEnVistasTests
{
    /// <summary>
    /// Las llamadas prohibidas: nombre → cuántos argumentos la vuelven obsoleta, y por qué cambio
    /// se sustituye. Crece con el catálogo sin tocar el cuerpo del test.
    /// </summary>
    private static readonly (string Nombre, int Argumentos, string Reemplazo)[] Prohibidas =
    [
        ("GetDictionaryValue", 2, "GetDictionaryValueOrDefault"),
    ];

    private static string RutaDelRepo([System.Runtime.CompilerServices.CallerFilePath] string f = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(f)!, "..", ".."));

    private static IReadOnlyList<string> Vistas()
    {
        var raiz = Path.Combine(RutaDelRepo(), "Synergos.CMS.Web", "Views");
        var todas = Directory.GetFiles(raiz, "*.cshtml", SearchOption.AllDirectories);

        // Red de seguridad: sin esto, un árbol movido deja el gate en verde sobre cero vistas.
        Assert.True(todas.Length > 50,
            $"Sólo se descubrieron {todas.Length} vistas bajo {raiz}: el gate está pasando sin mirar.");

        return todas;
    }

    /// <summary>
    /// Cuántos argumentos tiene la llamada que abre en <paramref name="apertura"/>, contando comas
    /// sólo al nivel de fuera y respetando cadenas — que es lo que hace la diferencia entre la
    /// sobrecarga obsoleta y la que no lo está.
    /// </summary>
    private static (int Argumentos, int Fin) Aridad(string texto, int apertura)
    {
        int profundidad = 0, comas = 0, i = apertura;
        char? cadena = null;
        var escape = false;

        for (; i < texto.Length; i++)
        {
            var c = texto[i];

            if (cadena is not null)
            {
                if (escape) escape = false;
                else if (c == '\\') escape = true;
                else if (c == cadena) cadena = null;
                continue;
            }

            if (c is '"' or '\'') cadena = c;
            else if (c == '(') profundidad++;
            else if (c == ')')
            {
                profundidad--;
                if (profundidad == 0) break;
            }
            else if (c == ',' && profundidad == 1) comas++;
        }

        return (comas + 1, i);
    }

    [Fact]
    public void Ninguna_vista_llama_a_la_sobrecarga_obsoleta()
    {
        var culpables = new List<string>();

        foreach (var vista in Vistas())
        {
            var texto = File.ReadAllText(vista);

            foreach (var (nombre, argumentos, reemplazo) in Prohibidas)
            {
                var i = 0;
                while (true)
                {
                    var k = texto.IndexOf(nombre + "(", i, StringComparison.Ordinal);
                    if (k < 0) break;

                    var (aridad, fin) = Aridad(texto, k + nombre.Length);
                    if (aridad == argumentos)
                    {
                        var linea = texto.Take(k).Count(c => c == '\n') + 1;
                        culpables.Add(
                            $"{Path.GetRelativePath(RutaDelRepo(), vista)}:{linea}  {nombre} con "
                            + $"{argumentos} argumentos → usá {reemplazo}");
                    }

                    i = fin + 1;
                }
            }
        }

        Assert.True(culpables.Count == 0,
            "Estas vistas llaman a una sobrecarga [Obsolete]. En caliente el CS0618 se asciende a "
            + "error por el trinquete del #134 y la página contesta 500 — con el build en 0 avisos, "
            + "porque `dotnet build` no compila ninguna vista.\n  "
            + string.Join("\n  ", culpables));
    }
}
