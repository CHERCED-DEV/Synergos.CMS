using System.Text.Json;
using Synergos.CMS.Application.Dinero;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Las unidades menores de un importe (#196, G-13): los mismos vectores de oro que corre el UI,
/// que es quien divide los <c>*Minor</c> para pintarlos.
/// </summary>
public class UnidadesMenoresTests
{
    private sealed record Vector(string Nombre, string Moneda, decimal Importe, long Menores);

    private static IReadOnlyList<Vector> Vectores()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var ruta = Path.Combine(dir!.FullName, "Synergos.CMS.Web", "docs", "contracts", "minor-units-vectors.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(ruta));
        return doc.RootElement.GetProperty("vectores").EnumerateArray()
            .Select(v => new Vector(
                v.GetProperty("nombre").GetString()!,
                v.GetProperty("moneda").GetString()!,
                v.GetProperty("importe").GetDecimal(),
                v.GetProperty("menores").GetInt64()))
            .ToList();
    }

    [Fact]
    public void Las_unidades_menores_del_CMS_dan_los_vectores_de_oro()
    {
        var vectores = Vectores();

        // Los vectores DECIDEN: alguno tiene que separar «centavos para todas» de la tabla por
        // moneda, y alguno el redondeo al par del de mitad-arriba. Sin eso, un cambio de regla
        // pasaría en verde.
        Assert.True(vectores.Count >= 10);
        Assert.Contains(vectores, v => (long)Math.Round(v.Importe * 100m, MidpointRounding.ToEven) != v.Menores);
        Assert.Contains(vectores, v => (long)Math.Round(v.Importe * 100m, MidpointRounding.AwayFromZero) != v.Menores
            && UnidadesMenores.Decimales(v.Moneda) == 2);

        var fallan = vectores
            .Where(v => UnidadesMenores.Desde(v.Importe, v.Moneda) != v.Menores)
            .Select(v => $"{v.Nombre}: {UnidadesMenores.Desde(v.Importe, v.Moneda)} ≠ {v.Menores}")
            .ToList();

        Assert.True(fallan.Count == 0, "Las unidades menores del CMS no dan estos vectores: " + string.Join("; ", fallan));
    }

    [Fact]
    public void De_menores_a_mayores_vuelve_al_importe()
    {
        foreach (var v in Vectores().Where(v => v.Nombre.StartsWith("medio", StringComparison.Ordinal) is false))
        {
            Assert.Equal(v.Importe, UnidadesMenores.Hacia(v.Menores, v.Moneda));
        }
    }
}
