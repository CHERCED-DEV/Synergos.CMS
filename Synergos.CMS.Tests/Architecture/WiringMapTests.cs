using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El mapa del cableado (<c>docs/product/11-mapa-del-cableado.md</c>) como invariante ejecutable.
/// </summary>
/// <remarks>
/// <para><b>Por qué un gate y no una lista.</b> El defecto que evita es concreto y ya pasó: la
/// épica hablaba de <b>45</b> stubs y al contarlos son <b>46</b>. Nadie añadió uno — la cuenta
/// era de memoria. Un inventario escrito a mano está desactualizado a la tercera ola, y entonces
/// es peor que no tenerlo: se planifica contra él.</para>
///
/// <para><b>Lo que vigila</b> son las tres formas de que el mapa mienta:</para>
/// <list type="number">
///   <item>un <c>Stub*</c> nuevo que nadie mapeó — quedaría invisible en una lista de 47;</item>
///   <item>una entrada cuyo stub ya no existe — el mapa describiría un repo que no está;</item>
///   <item>un destino inventado — «va a <c>Api.NoExiste</c>» se lee como trabajo planificado.</item>
/// </list>
///
/// <para><b>Tosco a propósito</b>, como los demás gates del repo: no atrapa a un adversario,
/// atrapa el atajo de un martes.</para>
/// </remarks>
public sealed class WiringMapTests
{
    /// <summary>
    /// Los orquestadores que <c>CLAUDE.md</c> §11 declara sin construir.
    /// </summary>
    /// <remarks>
    /// Es la ÚNICA excepción a «el destino tiene que existir en disco», y va explícita para que
    /// sea una decisión y no un agujero: un destino que no existe y tampoco está acá rompe. El
    /// día que se construya uno, borrarlo de esta lista no rompe nada — el directorio ya está.
    /// </remarks>
    private static readonly string[] OrquestadoresPendientes =
    {
        "Synergos.Bff.Viajes", "Synergos.Bff.Eventos", "Synergos.Bff.Realty",
        "Synergos.Bff.Gob", "Synergos.Bff.Academy", "Synergos.Bff.Social",
    };

    private static readonly string[] FamiliasValidas = { "A", "B", "C" };

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

    private static string MapaPath() => Path.Combine(RepoRoot(), "docs", "product", "11-mapa-del-cableado.md");

    /// <summary>Los <c>Stub*.cs</c> que hay de verdad en el disco.</summary>
    private static IReadOnlyList<string> StubsEnDisco()
        => Directory.EnumerateFiles(
                Path.Combine(RepoRoot(), "Synergos.CMS.Application", "Services", "Impl"), "Stub*.cs")
            .Select(Path.GetFileNameWithoutExtension)
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    private sealed record Entrada(string Stub, string Familia, string? Destino);

    /// <summary>
    /// Lee la tabla entre los marcadores <c>MAPA:INICIO</c>/<c>MAPA:FIN</c>.
    /// </summary>
    /// <remarks>
    /// Los marcadores existen para que la prosa de arriba pueda nombrar stubs —y los nombra a
    /// docenas— sin que el gate la confunda con el inventario. Sin ellos, documentar bien el
    /// mapa lo rompería, que es el peor incentivo posible.
    /// </remarks>
    private static IReadOnlyList<Entrada> Mapa()
    {
        var texto = File.ReadAllText(MapaPath());

        var inicio = texto.IndexOf("<!-- MAPA:INICIO -->", StringComparison.Ordinal);
        var fin = texto.IndexOf("<!-- MAPA:FIN -->", StringComparison.Ordinal);
        Assert.True(inicio >= 0 && fin > inicio, "El mapa no tiene los marcadores MAPA:INICIO/MAPA:FIN.");

        var tabla = texto[inicio..fin];
        var filas = Regex.Matches(tabla, @"^\|\s*`(Stub\w+)`\s*\|\s*([ABC])\s*\|\s*([^|]+?)\s*\|\s*$",
            RegexOptions.Multiline);

        return filas.Select(m =>
        {
            var destino = m.Groups[3].Value.Trim().Trim('`');
            return new Entrada(m.Groups[1].Value, m.Groups[2].Value,
                destino is "—" or "-" or "" ? null : destino);
        }).ToList();
    }

    /// <summary>
    /// Las CIFRAS de la prosa cuadran con el inventario y con el disco.
    /// </summary>
    /// <remarks>
    /// <para><b>Este gate existe porque la prosa se desvió tres olas seguidas</b> (#50). Los
    /// marcadores <c>MAPA:INICIO</c>/<c>MAPA:FIN</c> están puestos a propósito para que la
    /// narrativa pueda nombrar stubs sin romper el inventario — y el efecto secundario fue que
    /// <b>nada medía la narrativa</b>. Quien cableaba movía su fila del inventario, porque el gate
    /// se lo exigía, y no el resumen, porque nadie se lo pedía.</para>
    ///
    /// <para>El resultado: el documento se contradecía a sí mismo —resumen A=9, cabecera A(8)— y
    /// ninguna de las dos era la verdad, que eran 12. <b>El gate estaba verde y tenía razón</b>;
    /// mentía la mitad que se mantiene a mano, que además es la que la gente lee para elegir el
    /// siguiente trabajo.</para>
    ///
    /// <para>Se comprueban las cifras y no el texto: el <i>porqué</i> escrito a mano es lo que da
    /// valor al documento y generarlo lo convertiría en un artefacto. Lo que se desvía son los
    /// números.</para>
    /// </remarks>
    [Fact]
    public void Las_cifras_de_la_prosa_cuadran_con_el_inventario()
    {
        var texto = File.ReadAllText(MapaPath());
        var mapa = Mapa();
        var enDisco = StubsEnDisco().Count;

        Assert.Equal(enDisco, mapa.Count);

        // El total, en las tres veces que el documento lo dice.
        foreach (var frase in new[]
                 {
                     $"Los {enDisco} `Stub*` de",
                     $"## Lo primero: son {enDisco}, no 45",
                     $"## Los {enDisco}, en una lista",
                 })
        {
            Assert.True(texto.Contains(frase, StringComparison.Ordinal),
                $"La prosa del mapa no dice «{frase}». Son {enDisco} stubs en disco: "
                + "si la cifra cambió, hay que moverla en la prosa además del inventario.");
        }

        // El reparto por familia, en la tabla-resumen y en cada cabecera de sección.
        var titulos = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["A"] = "## Familia A — cableado pendiente ({0})",
            ["B"] = "## Familia B — ya resuelto desde el contenido de Umbraco ({0})",
            ["C"] = "## Familia C — se queda en stub a propósito ({0})",
        };

        foreach (var familia in FamiliasValidas)
        {
            var cuantos = mapa.Count(e => e.Familia == familia);

            Assert.True(Regex.IsMatch(texto, $@"^\|\s*\*\*{familia} —[^|]*\|\s*{cuantos}\s*\|", RegexOptions.Multiline),
                $"La tabla-resumen no dice {cuantos} para la familia {familia}, que es lo que tiene el inventario.");

            var titulo = string.Format(System.Globalization.CultureInfo.InvariantCulture, titulos[familia], cuantos);
            Assert.True(texto.Contains(titulo, StringComparison.Ordinal),
                $"La cabecera de la familia {familia} no dice ({cuantos}). Esperado: «{titulo}».");
        }
    }

    /// <summary>
    /// La tabla narrativa de la familia A lista a TODOS los de la familia A. Uno por uno.
    /// </summary>
    /// <remarks>
    /// <para><b>Que las cifras cuadren no obliga a que exista la fila.</b> El gate de arriba
    /// compara números: si el inventario tiene 13 en A, la cabecera tiene que decir «(13)». Pero
    /// la cabecera se corrige de un tecleo y la tabla de abajo —la que dice a qué nivel va cada
    /// uno y por qué— se queda con doce filas, y todo sigue en verde.</para>
    ///
    /// <para><b>Y es justo el fallo que ocurrió</b>: los stubs que se cablearon en #33a, #44 y
    /// #46 entraron a la familia A moviendo su fila del inventario, que es lo que el gate exigía.
    /// La tabla narrativa no los nombraba. La consecuencia no es cosmética: <b>ésta es la tabla
    /// que se lee para elegir el siguiente trabajo</b> —el inventario es una rejilla de tres
    /// columnas—, así que un stub ausente de aquí es un stub que nadie va a tomar.</para>
    ///
    /// <para>Sólo la familia A. B y C describen decisiones tomadas —«ya sale del contenido», «se
    /// queda a propósito»— y agrupan bien en prosa; A es la única que es una <b>lista de trabajo
    /// pendiente</b>, y una lista de trabajo incompleta es la que hace daño. Se comprueba en los
    /// dos sentidos: una fila narrativa de un stub que ya no es A también miente, y esa mentira
    /// dice «esto está por hacer» de algo que ya se hizo.</para>
    /// </remarks>
    [Fact]
    public void La_tabla_narrativa_de_la_familia_A_lista_a_todos()
    {
        var texto = File.ReadAllText(MapaPath());

        var inicio = texto.IndexOf("## Familia A", StringComparison.Ordinal);
        var fin = texto.IndexOf("## Familia B", StringComparison.Ordinal);
        Assert.True(inicio >= 0 && fin > inicio, "El mapa no tiene las secciones «## Familia A» y «## Familia B».");

        // Las filas de la sección, no las de los recuadros: una fila citada dentro de un `>` es
        // una corrección explicando el pasado, no el inventario de lo que falta.
        var narradas = Regex.Matches(texto[inicio..fin], @"^\|\s*`(Stub\w+)`\s*\|", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var familiaA = Mapa().Where(e => e.Familia == "A").Select(e => e.Stub).ToList();

        var sinFila = familiaA.Where(s => !narradas.Contains(s)).ToList();
        Assert.True(sinFila.Count == 0,
            $"Estos stubs son familia A en el inventario y no tienen fila en la tabla narrativa de "
            + $"«## Familia A»: {string.Join(", ", sinFila)}. Esa tabla es la que se lee para elegir "
            + "el siguiente trabajo: sin fila, el stub es invisible aunque la cifra cuadre.");

        var deMas = narradas.Where(s => !familiaA.Contains(s)).ToList();
        Assert.True(deMas.Count == 0,
            $"Estos stubs tienen fila en la tabla narrativa de «## Familia A» y no son familia A en "
            + $"el inventario: {string.Join(", ", deMas)}. La tabla anuncia trabajo pendiente que "
            + "el inventario ya no reconoce como tal.");
    }

    /// <summary>
    /// La columna «Estado» de la tabla narrativa de la familia A, fila por fila.
    /// </summary>
    /// <remarks>
    /// <b>El criterio de «hecho» es la NEGRITA</b>, y es el que ya usa el documento: distingue
    /// <c>**HU #24**</c> de <c>pendiente</c>, de <c>épica #2</c> y de <c>bloqueado por #27</c>.
    /// Contar por «menciona un número de ticket» daría por entregado justo lo que está bloqueado
    /// <i>por</i> uno, que es el error más fácil de cometer acá.
    /// </remarks>
    private static IReadOnlyList<(string Stub, bool Hecho)> EstadosDeLaFamiliaA()
    {
        var texto = File.ReadAllText(MapaPath());

        var inicio = texto.IndexOf("## Familia A", StringComparison.Ordinal);
        var fin = texto.IndexOf("## Familia B", StringComparison.Ordinal);
        Assert.True(inicio >= 0 && fin > inicio, "El mapa no tiene las secciones «## Familia A» y «## Familia B».");

        return Regex.Matches(texto[inicio..fin],
                @"^\|\s*`(Stub\w+)`\s*\|[^|]*\|[^|]*\|\s*([^|]+?)\s*\|\s*$", RegexOptions.Multiline)
            .Select(m => (Stub: m.Groups[1].Value, Hecho: m.Groups[2].Value.StartsWith("**", StringComparison.Ordinal)))
            .ToList();
    }

    /// <summary>
    /// El desglose «de los N, X están hechos … faltan Y» cuadra con la tabla, y los que faltan
    /// se nombran.
    /// </summary>
    /// <remarks>
    /// <para><b>El defecto que evita ya ocurrió dos veces, y la segunda mientras se escribía este
    /// gate</b> (#66). La primera fue en la dirección obvia: la prosa decía «once están hechos …
    /// faltan dos» sobre una familia de catorce, y el que se caía de la cuenta era el stub con más
    /// consumidores vivos. La segunda fue <b>al revés</b>: <c>#57</c> cableó
    /// <c>StubReturnService</c> contra <c>Bff.Tienda</c>, movió §11 a «once hechos» y dejó su fila
    /// narrativa diciendo <c>pendiente</c> — con el razonamiento viejo, el que ese mismo ticket
    /// había corregido.</para>
    ///
    /// <para><b>Ninguno de los dos rompía nada.</b> Los gates de arriba cuadran el TOTAL de la
    /// familia contra la rejilla y contra el disco, y trece filas siguen siendo trece filas
    /// aunque una mienta sobre su estado. Lo que se desvía es el desglose, que es exactamente lo
    /// que alguien lee para decidir qué toma: en el primer caso deja un stub sin dueño, en el
    /// segundo manda a alguien a hacer trabajo que ya está hecho.</para>
    ///
    /// <para><b>Y se exige que los que faltan se NOMBREN</b>, no sólo que la cifra cuadre. «Faltan
    /// dos» sin decir cuáles obliga a recorrer trece filas a ojo, que es la operación que este
    /// documento existe para ahorrar.</para>
    ///
    /// <para><b>La frase se busca con los espacios colapsados</b>: la guía va envuelta a mano a
    /// ~72 columnas y estas frases caen partidas. Exigirlas contiguas obligaría a maquetar la
    /// prosa para pasar el gate, y entonces manda el gate y no lo que dice el texto.</para>
    /// </remarks>
    [Fact]
    public void El_desglose_hecho_y_pendiente_de_la_familia_A_cuadra()
    {
        var estados = EstadosDeLaFamiliaA();
        var familiaA = Mapa().Where(e => e.Familia == "A").Select(e => e.Stub).ToList();

        // Sin esto, un regex roto leería cero filas y el gate pediría «de los 0, 0 están hechos».
        Assert.Equal(familiaA.Count, estados.Count);

        var hechos = estados.Count(e => e.Hecho);
        var faltan = estados.Where(e => !e.Hecho).Select(e => e.Stub).ToList();

        var guia = Regex.Replace(File.ReadAllText(Path.Combine(RepoRoot(), "CLAUDE.md")), @"\s+", " ");

        foreach (var frase in new[]
                 {
                     $"De los {estados.Count}, **{Palabra(hechos)}** están hechos",
                     $"**Faltan {Palabra(faltan.Count)}**",
                 })
        {
            Assert.True(guia.Contains(frase, StringComparison.Ordinal),
                $"CLAUDE.md §11 no dice «{frase}». La tabla narrativa de la familia A tiene "
                + $"{estados.Count} filas: {hechos} en negrita (hechas) y {faltan.Count} sin negrita. "
                + "El desglose se cuenta contra la columna «Estado», no se recuerda (#66).");
        }

        // Y con nombre, EN LA FRASE — no en cualquier parte del fichero.
        //
        // Buscarlo en todo CLAUDE.md es la trampa de «la declaración se valida contra su propia
        // declaración»: el párrafo que documenta este defecto nombra los stubs, así que el gate
        // se satisfaría a sí mismo y quedaría verde sobre una enumeración vacía. Se mira desde
        // «**Faltan N**» hasta donde arranca la nota siguiente.
        var desde = guia.IndexOf($"**Faltan {Palabra(faltan.Count)}**", StringComparison.Ordinal);
        var hasta = guia.IndexOf(" > ", desde, StringComparison.Ordinal);
        var enumeracion = hasta > desde ? guia[desde..hasta] : guia[desde..];

        var sinNombrar = faltan.Where(s => !enumeracion.Contains($"`{s}`", StringComparison.Ordinal)).ToList();
        Assert.True(sinNombrar.Count == 0,
            $"CLAUDE.md §11 dice cuántos faltan y no los nombra ahí mismo: {string.Join(", ", sinNombrar)}. "
            + "Un stub que no se nombra es un stub que nadie va a tomar — y nombrarlo doce párrafos "
            + "más abajo no cuenta, porque eso ya lo hace la nota que explica este gate.");
    }

    /// <summary>Las cifras de esta prosa se escriben con letra, así que se comparan con letra.</summary>
    private static string Palabra(int n) => n switch
    {
        0 => "cero", 1 => "uno", 2 => "dos", 3 => "tres", 4 => "cuatro", 5 => "cinco",
        6 => "seis", 7 => "siete", 8 => "ocho", 9 => "nueve", 10 => "diez", 11 => "once",
        12 => "doce", 13 => "trece", 14 => "catorce", 15 => "quince",
        _ => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// La cifra de stubs que <c>CLAUDE.md</c> declara, contada contra el disco.
    /// </summary>
    /// <remarks>
    /// <para><b>Otro sitio a mano que se desvió, y lo destapó buscar el de arriba</b>: §3 decía
    /// «cada uno de los <b>48</b> <c>Stub*</c>» cuando §11 —doce pantallas más abajo, en el mismo
    /// fichero— ya decía 49. El gate del mapa cuadra la prosa DEL MAPA contra el disco; nadie
    /// miraba la de la guía.</para>
    ///
    /// <para>Y §3 es la tabla «dónde está la verdad», o sea lo primero que abre un agente que no
    /// conoce el repo. Una cifra equivocada ahí no se lee como un typo: se lee como el inventario.</para>
    /// </remarks>
    [Fact]
    public void La_cifra_de_stubs_de_CLAUDE_md_se_cuenta_contra_el_disco()
    {
        var cuantos = StubsEnDisco().Count;
        var guia = Regex.Replace(File.ReadAllText(Path.Combine(RepoRoot(), "CLAUDE.md")), @"\s+", " ");

        var menciones = Regex.Matches(guia, @"(\d{2,3}) `Stub\*`").Select(m => int.Parse(m.Groups[1].Value)).ToList();

        Assert.True(menciones.Count >= 2,
            $"CLAUDE.md menciona la cifra de stubs {menciones.Count} vez/veces. Eran dos —§3 y §11—: "
            + "borrar una mención no es una forma válida de pasar este gate.");

        var desviadas = menciones.Where(n => n != cuantos).ToList();
        Assert.True(desviadas.Count == 0,
            $"En disco hay {cuantos} `Stub*` y CLAUDE.md dice {string.Join(" y ", desviadas)}. "
            + "La cifra se cuenta, no se recuerda: §3 llevaba 48 con 49 en disco, y §11 ya decía 49 "
            + "en el mismo fichero.");
    }

    /// <summary>
    /// Cuántos stubs son DURABLES se cuenta, no se recuerda.
    /// </summary>
    /// <remarks>
    /// La cifra decía 20 sobre 46 y son 18 sobre 47. Es la afirmación más citada del documento
    /// —«"stub" en este repo dejó de querer decir en memoria»— y la que más caro sale equivocada:
    /// hace que un ticket prometa arreglar algo que ya está arreglado. El criterio es el que el
    /// propio documento declara, así que se puede contar.
    /// </remarks>
    [Fact]
    public void La_cifra_de_stubs_durables_se_cuenta_contra_el_disco()
    {
        var impl = Path.Combine(RepoRoot(), "Synergos.CMS.Application", "Services", "Impl");

        var durables = Directory.EnumerateFiles(impl, "Stub*.cs")
            .Count(f => File.ReadAllText(f) is var codigo
                        && (codigo.Contains("IJsonEntityStore", StringComparison.Ordinal)
                            || codigo.Contains("IPrivateFileStore", StringComparison.Ordinal)
                            || codigo.Contains("IPhiStore", StringComparison.Ordinal)));

        var esperado = $"{durables} de los {StubsEnDisco().Count}";
        Assert.True(File.ReadAllText(MapaPath()).Contains(esperado, StringComparison.Ordinal),
            $"El mapa no dice «{esperado}» al hablar de durabilidad. Contados en disco: {durables}.");
    }

    [Fact]
    public void Todo_stub_del_disco_esta_en_el_mapa()
    {
        // El defecto que evita: alguien añade un stub, nadie lo mapea, y queda invisible para
        // siempre en una lista de 47.
        var mapeados = Mapa().Select(e => e.Stub).ToHashSet(StringComparer.Ordinal);
        var faltan = StubsEnDisco().Where(s => !mapeados.Contains(s)).ToList();

        Assert.True(faltan.Count == 0,
            $"Estos Stub* no están en docs/product/11-mapa-del-cableado.md: {string.Join(", ", faltan)}. "
            + "Cada stub necesita familia (A: va a una capacidad o BFF · B: ya sale del contenido · "
            + "C: se queda en stub a propósito) y una frase de por qué.");
    }

    [Fact]
    public void Toda_entrada_del_mapa_corresponde_a_un_stub_que_existe()
    {
        // Al revés que el anterior: el mapa describiendo un repo que ya no está. Pasa al borrar
        // un stub —o al cablearlo de verdad— sin tocar el documento.
        var enDisco = StubsEnDisco().ToHashSet(StringComparer.Ordinal);
        var sobran = Mapa().Where(e => !enDisco.Contains(e.Stub)).Select(e => e.Stub).ToList();

        Assert.True(sobran.Count == 0,
            $"El mapa nombra Stub* que ya no existen: {string.Join(", ", sobran)}. "
            + "Si se cablearon, la entrada se borra; el mapa describe lo que HAY.");
    }

    [Fact]
    public void Ningun_destino_nombra_una_capacidad_inexistente()
    {
        // «Va a Api.NoExiste» se lee como trabajo planificado y no lo es. La excepción son los
        // seis orquestadores que CLAUDE.md §11 declara sin construir, y va explícita.
        var raiz = RepoRoot();
        var malos = Mapa()
            .Where(e => e.Destino is not null)
            .Where(e => !Directory.Exists(Path.Combine(raiz, e.Destino!))
                     && !OrquestadoresPendientes.Contains(e.Destino, StringComparer.Ordinal))
            .Select(e => $"{e.Stub} → {e.Destino}")
            .ToList();

        Assert.True(malos.Count == 0,
            $"Destinos que no existen ni están declarados como pendientes: {string.Join(", ", malos)}.");
    }

    [Fact]
    public void Solo_la_familia_A_lleva_destino()
    {
        // Las otras dos NO van a ninguna capacidad, y ése es justamente su contenido informativo.
        // Un destino en una entrada B o C es la confusión que la épica advierte como cara: creer
        // que un catálogo que ya sale del contenido es cableado pendiente.
        var mapa = Mapa();

        var bcConDestino = mapa.Where(e => e.Familia != "A" && e.Destino is not null)
            .Select(e => $"{e.Stub} ({e.Familia}) → {e.Destino}").ToList();
        Assert.True(bcConDestino.Count == 0,
            $"Familias B/C con destino: {string.Join(", ", bcConDestino)}. "
            + "B ya sale del contenido y C se queda en stub: ninguna se cablea.");

        var aSinDestino = mapa.Where(e => e.Familia == "A" && e.Destino is null)
            .Select(e => e.Stub).ToList();
        Assert.True(aSinDestino.Count == 0,
            $"Familia A sin destino: {string.Join(", ", aSinDestino)}. "
            + "Si es cableado pendiente, hay que decir a qué y a qué nivel.");
    }

    [Fact]
    public void El_mapa_no_repite_ni_inventa_familias()
    {
        var mapa = Mapa();

        var repetidos = mapa.GroupBy(e => e.Stub, StringComparer.Ordinal)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(repetidos.Count == 0, $"Stubs repetidos en el mapa: {string.Join(", ", repetidos)}.");

        Assert.All(mapa, e => Assert.Contains(e.Familia, FamiliasValidas));
        Assert.NotEmpty(mapa);
    }
}
