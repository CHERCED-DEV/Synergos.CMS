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
