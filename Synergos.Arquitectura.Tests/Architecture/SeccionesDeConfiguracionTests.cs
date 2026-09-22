using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Ninguna sección de configuración del CMS está a UNA edición de otra (#154).
/// </summary>
/// <remarks>
/// <para><b>Qué pasó.</b> El secreto con el que se firma el QR de una entrada vivía en
/// <c>Synergos:Events</c> y la transacción del mismo vertical en <c>Synergos:Eventos</c> — dos
/// secciones a <b>una letra</b>, enlazadas por composers distintos. Un dedazo entre ellas
/// <b>no falla</b>: el binder de .NET descarta en silencio lo que no mapea
/// (<c>feedback_a_key_in_the_wrong_section_is_a_key_nobody_reads</c>, #138), así que el POCO se
/// queda en su default. Y las dos mitades del riesgo eran silenciosas: escribir
/// <c>Synergos__Events__ApiKey</c> deja al cliente saliendo a la red sin llave —401 a la primera
/// persona que compre— y escribir el secreto de firma bajo la sección del eje 2 deja la llave sin
/// poder rotarse, que no lo nota nadie hasta que hay dos instalaciones.</para>
///
/// <para><b>Por qué es un trinquete ABSOLUTO y no una línea base.</b> Medido antes de escribirlo:
/// de las 43 secciones que el CMS enlaza, <b>ese par era el único</b> a distancia ≤ 2. Con el
/// #154 cerrado la cifra es cero, así que exigirlo es gratis hoy — que es la condición que el
/// #134 puso para un umbral absoluto y la que el #140 no cumplía. Con deuda habría hecho falta
/// una línea base.</para>
///
/// <para><b>Se mide con Damerau-Levenshtein y no con Levenshtein a secas</b>, porque la
/// transposición es un dedazo de verdad —<c>Evnetos</c>— y cuesta una edición al teclear y dos al
/// contar. El umbral es <b>1</b>: a dos ediciones ya hay pares legítimos concebibles, y un gate
/// que pida un cambio que no arregla nada se desactiva.</para>
///
/// <para><b>Y se parsea sobre la fuente SIN comentarios.</b> Esta clase, el POCO y el composer
/// nombran <c>Synergos:Events</c> para explicar qué se retiró; un gate que leyera el texto crudo
/// se engañaría con su propia explicación
/// (<c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c>).</para>
/// </remarks>
public sealed class SeccionesDeConfiguracionTests
{
    /// <summary>A partir de aquí dos nombres se parecen lo bastante para confundirse.</summary>
    private const int DistanciaMinima = 2;

    private static string Dir(params string[] partes) => Proyectos.Ruta(partes);

    /// <summary>El fichero SIN comentarios — ver el <c>remarks</c> de la clase.</summary>
    private static string SinComentarios(string ruta)
        => string.Join('\n', File.ReadAllLines(ruta).Select(l =>
        {
            var t = l.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal)
                || t.StartsWith('*')
                || t.StartsWith("/*", StringComparison.Ordinal))
            {
                return string.Empty;
            }
            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? l[..i] : l;
        }));

    /// <summary>
    /// Las secciones que el árbol del CMS ENLAZA, derivadas de los <c>GetSection("…")</c>.
    /// </summary>
    /// <remarks>
    /// Sólo cuentan los literales. La sección retirada del #154 se alcanza por una constante
    /// —el composer la lee para <b>rechazarla</b>, no para enlazarla— así que está correctamente
    /// fuera de esta lista; y si alguien la volviera a enlazar con un literal, este gate la vería.
    /// </remarks>
    private static IReadOnlyList<string> SeccionesEnlazadas()
        => Directory
            .EnumerateFiles(Dir("Synergos.CMS.Web"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => Regex.Matches(SinComentarios(f), @"GetSection\(""(Synergos:[^""]+)""\)")
                .Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    /// <summary>Damerau-Levenshtein, con transposición de adyacentes.</summary>
    private static int Distancia(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var coste = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + coste);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            }
        }

        return d[a.Length, b.Length];
    }

    [Fact]
    public void Ninguna_seccion_esta_a_UNA_edicion_de_otra()
    {
        var secciones = SeccionesEnlazadas();

        Assert.True(secciones.Count >= 30,
            "El descubrimiento de secciones no ve nada (" + secciones.Count + "): si los composers "
            + "dejaron de usar GetSection con literal, este gate pasa en verde sin mirar nada.");

        var vecinas = new List<string>();
        for (var i = 0; i < secciones.Count; i++)
        {
            for (var j = i + 1; j < secciones.Count; j++)
            {
                var d = Distancia(secciones[i], secciones[j]);
                if (d < DistanciaMinima)
                {
                    vecinas.Add($"{secciones[i]} ≈ {secciones[j]} (d={d})");
                }
            }
        }

        Assert.True(vecinas.Count == 0,
            "Estas secciones de configuración se confunden entre sí: " + string.Join("; ", vecinas)
            + ". Un dedazo entre dos nombres así NO falla —el binder descarta en silencio lo que no "
            + "mapea— así que el servidor se comporta como uno sin configurar. Si son dos asuntos "
            + "del mismo vertical, la del sub-asunto va ANIDADA bajo la del vertical, como "
            + "Synergos:Gob:Notifications (#154).");
    }

    /// <summary>
    /// El rechazo de la sección retirada está ENCHUFADO en el composer, no sólo escrito.
    /// </summary>
    /// <remarks>
    /// Mide la <b>llamada dentro del cuerpo de <c>Compose</c></b> y no la mención en el fichero,
    /// que es lo que enseñó el addendum #14 de
    /// <c>feedback_an_exemption_needs_a_signature_behind_it</c>: el método y su llamada viven en el
    /// mismo fichero, así que un <c>Contains</c> sobre el fichero entero se lee como «lo usa» y
    /// pasa en verde con la llamada quitada.
    /// </remarks>
    [Fact]
    public void El_rechazo_de_la_seccion_retirada_esta_CABLEADO()
    {
        var codigo = SinComentarios(Dir("Synergos.CMS.Web", "Composers", "OptionsComposer.cs"));

        var abre = codigo.IndexOf("public void Compose(", StringComparison.Ordinal);
        Assert.True(abre > 0, "No se encontró el cuerpo de Compose en OptionsComposer.");

        // El corte va al siguiente MIEMBRO de la clase, buscado por su indentación y no por un
        // modificador concreto. La primera versión de este gate recortaba hasta
        // `"private static void"` y se quedó ciega en el mismo commit, al pasar el guardia a
        // `internal` para poder probarlo: el `Assert` de abajo la delató en vez de dejarla
        // midiendo el fichero entero, que es para lo que está.
        var siguienteMiembro = Regex.Match(codigo[abre..], @"\n    (?:public|internal|private|protected)\s");
        var cierra = siguienteMiembro.Success ? abre + siguienteMiembro.Index + 1 : -1;
        Assert.True(cierra > abre,
            "No se pudo recortar el cuerpo de Compose: si el fichero cambió de forma, este gate "
            + "mediría el fichero entero y pasaría en verde con la llamada quitada.");

        var cuerpo = codigo[abre..cierra];

        Assert.Contains("ExigirQueNadieUseLaSeccionRetirada(", cuerpo, StringComparison.Ordinal);

        // Y lo que ese rechazo tiene que mirar: la sección retirada Y su vecina PLANA, que vive
        // bajo la sección correcta y por eso no la ve un barrido de la retirada.
        var guarda = codigo[cierra..];
        Assert.Contains("\"Synergos:Events\"", guarda, StringComparison.Ordinal);
        Assert.Contains("\"Synergos:Eventos:TicketSigningSecret\"", guarda, StringComparison.Ordinal);
    }
}
