using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Un orquestador que conserva su flujo imperativo junto a uno declarado lee el interruptor que dice cuál
/// corre (<c>ModosDelOrquestador.Leer</c>, núcleo N1, P09).
/// </summary>
/// <remarks>
/// <para><b>Por qué un gate</b>: el superset (principio I) deja convivir los dos caminos, y sin interruptor
/// la regla de cuál gana sería el orden en que alguien registró los endpoints. Se mira por la fuente, como
/// <see cref="AperturaDeSagaTests"/>: un <c>*Flow.cs</c> de <c>Domain/</c> que no interpreta una definición
/// (ni <c>FlowRunner&lt;</c> ni <c>FachadaDeFlujo&lt;</c>) es imperativo, y un <c>flujos/*.json</c> es una
/// definición declarada.</para>
///
/// <para><b>Hoy no hay ninguno con los dos</b> (Eventos interpreta su definición, los demás no declaran):
/// el gate nace en verde y lo que se prueba es que el que venga primero —Salud, línea 003— no pueda nacer
/// sin el interruptor. Por eso lleva su red contra el vacío y la prueba de la clasificación.</para>
/// </remarks>
public sealed class ModoDelOrquestadorTests
{
    private const string Prefijo = "Synergos.Bff.";
    private static readonly string[] NoSonVerticales = ["Synergos.Bff.Core", "Synergos.Bff.Pasos"];

    private static string Desnuda(string fuente)
    {
        var sinBloques = Regex.Replace(fuente, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(sinBloques, @"^\s*//.*$", string.Empty, RegexOptions.Multiline);
    }

    /// <summary>Si un <c>*Flow.cs</c> (sin comentarios) es imperativo: no interpreta ninguna definición.</summary>
    internal static bool EsImperativo(string codigo)
        => !codigo.Contains("FlowRunner<", StringComparison.Ordinal) && !codigo.Contains("FachadaDeFlujo<", StringComparison.Ordinal);

    private sealed record Orquestador(string Nombre, bool Declara, bool TieneImperativo, bool LeeElModo);

    private static IReadOnlyList<Orquestador> Orquestadores()
        => Proyectos.Nombres(Prefijo)
            .Where(n => !NoSonVerticales.Contains(n, StringComparer.OrdinalIgnoreCase))
            .Select(n =>
            {
                var dir = Proyectos.Dir(n);
                var dominio = Path.Combine(dir, "Domain");
                var flujos = Path.Combine(dir, "flujos");
                var fuentes = Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                    .Select(f => (Fichero: f, Codigo: Desnuda(File.ReadAllText(f))))
                    .ToList();
                return new Orquestador(
                    n,
                    Directory.Exists(flujos) && Directory.EnumerateFiles(flujos, "*.json").Any(),
                    Directory.Exists(dominio) && fuentes.Any(f => f.Fichero.StartsWith(dominio, StringComparison.Ordinal)
                        && f.Fichero.EndsWith("Flow.cs", StringComparison.Ordinal) && EsImperativo(f.Codigo)),
                    fuentes.Any(f => f.Codigo.Contains("ModosDelOrquestador.Leer(", StringComparison.Ordinal)));
            })
            .ToList();

    [Fact]
    public void Hay_orquestadores_que_mirar()
    {
        // Red contra el vacío: si el descubrimiento deja de ver, el gate de abajo pasa sin mirar nada.
        var vistos = Orquestadores();
        Assert.True(vistos.Count >= 5, $"se descubrieron {vistos.Count} orquestadores y hay cinco construidos.");
        Assert.Contains(vistos, o => o.Declara);
        Assert.Contains(vistos, o => o.TieneImperativo);
    }

    [Fact]
    public void Interpretar_una_definicion_no_es_un_flujo_imperativo()
    {
        Assert.False(EsImperativo("new FlowRunner<TicketingSaga>(motor, pasos, flujo, binding, fases, reloj, log)"));
        Assert.False(EsImperativo("_fachada = new FachadaDeFlujo<TicketingSaga>(sagas, flujo, def, reglas);"));
        Assert.True(EsImperativo("var hold = await _caps.HoldAsync(resourceId, window, patient, saga.KeyFor(\"hold\"), ct);"));
    }

    [Fact]
    public void Quien_tiene_un_flujo_imperativo_y_uno_declarado_lee_el_interruptor()
    {
        var sinInterruptor = Orquestadores()
            .Where(o => o.Declara && o.TieneImperativo && !o.LeeElModo)
            .Select(o => o.Nombre)
            .ToList();

        Assert.True(sinInterruptor.Count == 0,
            "tienen un *Flow.cs imperativo y un flujos/*.json y no leen `ModosDelOrquestador.Leer`: la regla de cuál "
            + "corre sería el orden de registro (principio I, P09): " + string.Join(", ", sinInterruptor));
    }
}
