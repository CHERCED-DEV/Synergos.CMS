using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Que todo orquestador que llame a una ruta donde la capacidad RESUELVE la afirmación de
/// identidad declare una — y que no la declare donde nadie la lee (#147).
/// </summary>
/// <remarks>
/// <para><b>Lo encontró un proceso vivo, y ningún test lo habría visto.</b> Desde la HU #14
/// <c>POST /v1/payments</c> resuelve la afirmación antes de tocar su almacén y rechaza con
/// <c>payments.access_requires_identity</c> a quien no declare ninguna. <b>Ninguno de los cinco
/// orquestadores la mandaba</b>, así que ni la tienda, ni la cita clínica, ni la entrada, ni el
/// viaje, ni el alquiler podían autorizar un cobro contra la capacidad viva: 400 en el primer
/// paso que mueve plata, con las tres suites en verde. Lo que lo escondía es que los tests de
/// los cinco hablan con una <c>CapacidadesFalsas</c> que no aplica las reglas de la capacidad —
/// un doble no puede rechazar por una regla que no conoce, así que probaba el doble y no el
/// contrato (<c>verify_with_live_processes</c>).</para>
///
/// <para><b>Y la guía afirmaba lo contrario</b>: «<c>Api.Orders</c> y <c>Api.Payments</c> hoy NO
/// llevan puerta de identidad por ahí… sería abrirles un campo que nadie puede llenar». La
/// puerta estaba puesta y era incondicional. Se corrigió en el mismo commit
/// (<c>docs_written_ahead_of_the_code_are_a_defect_with_a_clean_face</c>).</para>
///
/// <para><b>El criterio es ESTRUCTURAL y no el <c>record</c>.</b> Lo primero que probé fue
/// «la petición declara <c>string? Assertion</c>», y marca <c>/v1/grants/check</c> —que es una
/// LECTURA con POST y reusa el <c>RevokeRequest</c> de al lado sin resolver nada—, o sea marca
/// al bueno, que es como un gate enseña a ignorarse (#158). Lo que de verdad produce el 400 es
/// que el endpoint reciba un <c>IdentityTokenGate</c>: eso está en su firma, no en una
/// convención.</para>
///
/// <para><b>Se cruza en los DOS sentidos.</b> Faltar donde hace falta es el defecto; sobrar
/// donde nadie la lee es su espejo y no es inocuo — <c>System.Text.Json</c> descarta en silencio
/// lo que el <c>record</c> no declara, que es la forma exacta de G-7.</para>
///
/// <para><b>Las dos listas salen del disco</b> y ninguna está escrita acá: una lista a mano se
/// queda corta el día que una capacidad más ponga su puerta, y ése es justo el día en que este
/// gate tendría que hablar.</para>
/// </remarks>
public sealed class AfirmacionDeOrquestadorTests
{
    /// <summary>Lo medido al escribirlo: 9 rutas con puerta, 50 POST, 5 cruces.</summary>
    /// <remarks>
    /// Las redes van <b>por debajo</b> de lo medido y no por encima: puesta encima, la red salta
    /// antes que el cruce y la mutación no llega nunca a lo que se quiere probar (#159).
    /// </remarks>
    private const int MinimoRutasConPuerta = 5;
    private const int MinimoLlamadas = 30;
    private const int MinimoCruces = 3;

    private static readonly Regex ConPuerta = new(
        @"\.MapPost\(\s*""(/v1/[^""]*)""\s*,\s*(?:async\s*)?\((.*?)\)\s*=>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex Llamada = new(
        @"Post<[^>]*>\(\s*\w+\s*,\s*\$?""(v1/[^""]*)""\s*,\s*(new\s*\{|null)",
        RegexOptions.Compiled);

    /// <summary>
    /// La fuente sin comentarios.
    /// </summary>
    /// <remarks>
    /// Seguro barato: medido, quitar esta línea deja los tres tests en VERDE, porque los dos
    /// regex están anclados a una llamada y no a una subcadena suelta. Se deja porque el día que
    /// alguien explique este gate en un <c>&lt;remarks&gt;</c> del fichero vigilado —citando
    /// <c>assertion =</c>, que es lo natural— el gate se creería su propia prosa.
    /// </remarks>
    private static string Sin(string fuente)
        => Regex.Replace(Regex.Replace(fuente, @"/\*.*?\*/", " ", RegexOptions.Singleline), @"//[^\n]*", " ");

    /// <summary>Una ruta comparable entre los dos lados: sin barra inicial y sin nombres de parámetro.</summary>
    /// <remarks>
    /// La capacidad escribe <c>{id}</c> y el orquestador <c>{holdId}</c> sobre la MISMA ruta:
    /// cruzar el literal daría cero coincidencias y el gate pasaría sobre el vacío.
    /// </remarks>
    private static string Normalizar(string ruta)
        => Regex.Replace(ruta.TrimStart('/'), @"\{[^}]*\}", "{}");

    private static IReadOnlyList<string> RutasConPuerta()
    {
        var rutas = new List<string>();
        foreach (var dir in Proyectos.Todos("Synergos.Api."))
        {
            var endpoints = Path.Combine(dir, "Endpoints");
            if (!Directory.Exists(endpoints)) continue;

            foreach (var f in Directory.EnumerateFiles(endpoints, "*.cs"))
            {
                foreach (Match m in ConPuerta.Matches(Sin(File.ReadAllText(f))))
                {
                    // Lo que produce el 400: la capacidad pide el verificador en la firma del
                    // endpoint. Sin él no hay afirmación que resolver y declararla sobraría.
                    if (m.Groups[2].Value.Contains("IdentityTokenGate", StringComparison.Ordinal))
                    {
                        rutas.Add(Normalizar(m.Groups[1].Value));
                    }
                }
            }
        }

        return rutas;
    }

    /// <summary>Cada POST de cada orquestador: quién, a qué ruta y con qué cuerpo.</summary>
    private static IReadOnlyList<(string Orquestador, string Ruta, string Cuerpo)> Llamadas()
    {
        var llamadas = new List<(string, string, string)>();
        foreach (var dir in Proyectos.Todos("Synergos.Bff."))
        {
            var clients = Path.Combine(dir, "Clients");
            if (!Directory.Exists(clients)) continue;

            var orq = Path.GetFileName(dir);
            foreach (var f in Directory.EnumerateFiles(clients, "*.cs"))
            {
                var fuente = Sin(File.ReadAllText(f));
                foreach (Match m in Llamada.Matches(fuente))
                {
                    // El CUERPO de esta llamada y no el fichero: `assertion` puede estar veinte
                    // líneas más abajo, en otro método, y `Contains` sobre el fichero entero se
                    // leería como «lo manda» — que es el addendum #14 de
                    // `an_exemption_needs_a_signature_behind_it`, cometido dentro de su gate.
                    var cuerpo = m.Groups[2].Value.StartsWith("null", StringComparison.Ordinal)
                        ? string.Empty
                        : Recortar(fuente, m.Index + m.Length);
                    llamadas.Add((orq, Normalizar(m.Groups[1].Value), cuerpo));
                }
            }
        }

        return llamadas;
    }

    /// <summary>El objeto anónimo del cuerpo, contando llaves desde la que acaba de abrirse.</summary>
    private static string Recortar(string fuente, int desde)
    {
        var profundidad = 1;
        for (var i = desde; i < fuente.Length; i++)
        {
            if (fuente[i] == '{') profundidad++;
            else if (fuente[i] == '}' && --profundidad == 0) return fuente[desde..i];
        }

        return fuente[desde..];
    }

    [Fact]
    public void Un_orquestador_que_llama_a_una_ruta_CON_puerta_declara_su_afirmacion()
    {
        var conPuerta = RutasConPuerta();
        var llamadas = Llamadas();

        // Las tres redes: si un descubrimiento deja de ver, las listas salen vacías y el cruce
        // pasa en verde sin mirar nada — el modo de fallo del #136, que se hereda en vez de
        // arreglarse.
        Assert.True(conPuerta.Count >= MinimoRutasConPuerta,
            $"Sólo se vieron {conPuerta.Count} rutas con puerta de identidad; el parseo de "
            + "`Endpoints/` dejó de ver. Un cruce sobre una lista vacía pasa en verde.");
        Assert.True(llamadas.Count >= MinimoLlamadas,
            $"Sólo se vieron {llamadas.Count} llamadas POST de orquestadores; el parseo de "
            + "`Clients/` dejó de ver.");

        var puerta = conPuerta.ToHashSet(StringComparer.Ordinal);
        var cruces = llamadas.Where(l => puerta.Contains(l.Ruta)).ToList();
        Assert.True(cruces.Count >= MinimoCruces,
            $"Sólo {cruces.Count} llamadas de orquestador tocan una ruta con puerta. O se "
            + "normalizó mal la ruta, o alguien dejó de llamarlas; en los dos casos este gate "
            + "está mirando al vacío.");

        var mudas = cruces
            .Where(l => !l.Cuerpo.Contains("assertion", StringComparison.Ordinal))
            .Select(l => $"{l.Orquestador} → POST /{l.Ruta}")
            .ToList();

        Assert.True(mudas.Count == 0,
            "Estas llamadas van a una ruta donde la capacidad RESUELVE la afirmación de "
            + "identidad y no declaran ninguna, así que la capacidad las rechaza con "
            + "`access_requires_identity` ANTES de tocar su almacén — el paso no ocurre, y "
            + "ningún test con doble lo ve:\n  " + string.Join("\n  ", mudas)
            + "\n→ `assertion = OrchestratorAssertion.Declared` dentro del cuerpo de la llamada.");
    }

    [Fact]
    public void Y_NO_la_declara_donde_la_capacidad_no_la_lee()
    {
        var puerta = RutasConPuerta().ToHashSet(StringComparer.Ordinal);

        var sobrantes = Llamadas()
            .Where(l => !puerta.Contains(l.Ruta))
            .Where(l => l.Cuerpo.Contains("assertion", StringComparison.Ordinal))
            .Select(l => $"{l.Orquestador} → POST /{l.Ruta}")
            .ToList();

        Assert.True(sobrantes.Count == 0,
            "Estas llamadas declaran una afirmación de identidad en una ruta cuyo endpoint no "
            + "resuelve ninguna. `System.Text.Json` descarta en silencio lo que el `record` no "
            + "declara, así que no falla: deja escrito que se afirmó algo que nadie leyó — la "
            + "forma exacta de G-7.\n  " + string.Join("\n  ", sobrantes));
    }

    [Fact]
    public void Lo_declarado_sale_de_UN_sitio_y_no_de_un_literal_en_cada_orquestador()
    {
        var literales = Llamadas()
            .Where(l => l.Cuerpo.Contains("assertion", StringComparison.Ordinal))
            .Where(l => !l.Cuerpo.Contains("OrchestratorAssertion.Declared", StringComparison.Ordinal))
            .Select(l => $"{l.Orquestador} → POST /{l.Ruta}")
            .ToList();

        // Cinco copias de `assertion = "CmsSession"` son cinco sitios donde el día que esto
        // cambie cambia uno solo, y las otras cuatro siguen mandando una cadena que la capacidad
        // ya no acepta — un 400 en el primer cobro, otra vez. La razón entera vive en
        // `OrchestratorAssertion`, y el `nameof` de ahí rompe el build si el enum se renombra.
        Assert.True(literales.Count == 0,
            "Estas llamadas escriben la afirmación a mano en vez de tomarla de "
            + "`OrchestratorAssertion.Declared`:\n  " + string.Join("\n  ", literales));
    }
}
