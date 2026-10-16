using System.Text.Json;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El seudónimo de una persona da lo que dicen sus vectores de oro (núcleo N3, NE-9 de 007): los mismos que
/// corre la copia de un orquestador, porque los dos árboles no comparten código y dos copias se desvían.
/// </summary>
public sealed class SeudonimoVectoresTests
{
    public static TheoryData<string, string?, string> Vectores()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "docs", "contracts", "seudonimo-vectors.json")));
        var datos = new TheoryData<string, string?, string>();
        foreach (var v in doc.RootElement.GetProperty("vectores").EnumerateArray())
        {
            datos.Add(v.GetProperty("nombre").GetString()!, v.GetProperty("correo").GetString(), v.GetProperty("seudonimo").GetString()!);
        }
        return datos;
    }

    [Theory]
    [MemberData(nameof(Vectores))]
    public void El_seudonimo_de_cada_correo_es_el_de_sus_vectores(string nombre, string? correo, string seudonimo)
    {
        Assert.True(seudonimo == SeudonimoDePersona.De(correo), $"{nombre}: «{correo}» da {SeudonimoDePersona.De(correo)} y los vectores dicen {seudonimo}.");
    }

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
}
