using System.Text.Json;
using System.Text.RegularExpressions;
using Synergos.CMS.Application.Services.Impl;

namespace Synergos.CMS.Tests.Gobierno;

/// <summary>
/// El proceso del trámite es UN dato (009-gobierno-tramite, FR-001, FR-007 y FR-009): la definición que se publica en
/// <c>Api.Workflow</c> vive en <c>tools/provisionar.procesos.json</c>, y todo lo que decide sobre un expediente se
/// cruza con ella.
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta.</b> Medido el 2026-10-16: la tabla del motor en proceso y la definición publicada
/// decían cosas distintas en 3 de 9 pares estado×decisión, y nada lo veía, porque la definición vivía en un heredoc
/// de bash que ningún gate leía (D-3, D-4).</para>
/// </remarks>
public sealed class ProcesoDelTramiteTests
{
    private const string V1 = "gov.tramite";
    private const string V2 = "gov.tramite.v2";

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

    private sealed record Transicion(string Name, string From, string To, IReadOnlyList<string> RequiredRoles);

    private sealed record Definicion(
        string Key, string InitialState, IReadOnlyList<string> FinalStates, IReadOnlyList<Transicion> Transitions);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Las definiciones del fichero de procesos; las entradas que sólo llevan <c>_</c> son comentarios.</summary>
    private static IReadOnlyList<Definicion> DelFichero()
    {
        var ruta = Path.Combine(RepoRoot(), "tools", "provisionar.procesos.json");
        Assert.True(File.Exists(ruta), $"No existe {ruta}: el proceso del trámite es un dato del repositorio (FR-009).");

        using var doc = JsonDocument.Parse(File.ReadAllText(ruta));
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        return doc.RootElement.EnumerateArray()
            .Where(e => e.EnumerateObject().Any(p => p.Name != "_"))
            .Select(e => e.Deserialize<Definicion>(Json)!)
            .ToList();
    }

    private static Definicion Clave(string clave)
        => DelFichero().SingleOrDefault(d => d.Key == clave)
           ?? throw new Xunit.Sdk.XunitException($"El fichero de procesos no declara «{clave}».");

    /// <summary>La v1 tal como la publica hoy el heredoc de provisionar.sh, con la clave en su valor por defecto.</summary>
    private static Definicion DelHeredoc()
    {
        var guion = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "provisionar.sh")).Replace("\r\n", "\n");
        var m = Regex.Match(guion, @"DEF_GOB=\$\(cat <<JSON\n([\s\S]*?)\nJSON\n\)");
        Assert.True(m.Success, "No se encontró el heredoc DEF_GOB en provisionar.sh: si la v1 ya sale del fichero, este test se retira con él.");
        return JsonSerializer.Deserialize<Definicion>(m.Groups[1].Value.Replace("$GOB_DEFINITION", V1), Json)!;
    }

    private static string Huella(Definicion d) => string.Join("|",
        d.Key, d.InitialState, string.Join(",", d.FinalStates),
        string.Join(";", d.Transitions.Select(t => $"{t.Name}:{t.From}>{t.To}[{string.Join(",", t.RequiredRoles)}]")));

    /// <summary>
    /// Mientras el heredoc siga publicando la v1, el fichero la copia sin cambiarle nada.
    /// </summary>
    /// <remarks>
    /// Son dos copias del mismo dato hasta que el despliegue copie el fichero (NP-1) y el guion publique desde él: si
    /// divergieran, un servidor provisionado tendría una v1 distinta de la que el repositorio dice que existe.
    /// </remarks>
    [Fact]
    public void La_v1_del_fichero_es_la_del_heredoc_sin_cambiar_nada()
    {
        Assert.Equal(Huella(DelHeredoc()), Huella(Clave(V1)));
    }

    [Fact] // FR-005: los cinco estados de siempre, la inicial y las dos finales.
    public void La_v2_usa_los_estados_que_el_CMS_sabe_representar()
    {
        var v2 = Clave(V2);
        var estados = v2.Transitions.SelectMany(t => new[] { t.From, t.To })
            .Append(v2.InitialState).Concat(v2.FinalStates).Distinct().ToList();

        foreach (var estado in estados)
        {
            Assert.True(GovStatusSlugs.TryParse(estado, out var parseado) && GovStatusSlugs.ToSlug(parseado) == estado,
                $"La v2 nombra «{estado}», que el CMS no sabe representar: el expediente quedaría en un estado sin traducción.");
        }

        Assert.Equal("submitted", v2.InitialState);
        Assert.Equal(new[] { "approved", "rejected" }, v2.FinalStates.OrderBy(s => s, StringComparer.Ordinal));
    }

    [Fact] // FR-006: toda transición exige un rol, para que la guarda de la capacidad rechace a la otra cara.
    public void Toda_transicion_de_la_v2_exige_el_rol_de_una_cara()
    {
        foreach (var t in Clave(V2).Transitions)
        {
            Assert.True(t.RequiredRoles.Count == 1 && (t.RequiredRoles[0] is "funcionario" or "ciudadano"),
                $"«{t.Name}» desde «{t.From}» exige [{string.Join(",", t.RequiredRoles)}]: tiene que exigir «funcionario» o «ciudadano».");
        }
    }
}
