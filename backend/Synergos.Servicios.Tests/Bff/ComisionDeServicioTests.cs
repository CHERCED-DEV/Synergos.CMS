using System.Text.Json;
using Synergos.Bff.Eventos.Domain;
using Synergos.Core;
using Synergos.CMS.Tests.Architecture;   // Proyectos: dónde vive cada proyecto (#136)

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La comisión de servicio del orquestador de Eventos contra los vectores de oro que comparte con
/// el carrito y con el motor en proceso del CMS (ADR 0137, #194).
/// </summary>
/// <remarks>
/// Los vectores viven en la superficie de acople (<c>docs/contracts/service-fee-vectors.json</c>)
/// porque son tres implementaciones de la misma regla, en dos lenguajes: una tabla escrita acá sólo
/// vigilaría a ésta.
/// </remarks>
public sealed class ComisionDeServicioTests
{
    private sealed record Vector(string Nombre, decimal Subtotal, decimal FeePercent, decimal Fee);

    private static IReadOnlyList<Vector> Vectores()
    {
        var ruta = Proyectos.Ruta("Synergos.CMS.Web", "docs", "contracts", "service-fee-vectors.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(ruta));
        return doc.RootElement.GetProperty("vectores").EnumerateArray()
            .Select(v => new Vector(
                v.GetProperty("nombre").GetString()!,
                v.GetProperty("subtotal").GetDecimal(),
                v.GetProperty("feePercent").GetDecimal(),
                v.GetProperty("fee").GetDecimal()))
            .ToList();
    }

    [Fact]
    public void La_comision_del_orquestador_da_los_vectores_de_oro()
    {
        var vectores = Vectores();
        Assert.True(vectores.Count >= 10, "Los vectores de la comisión no se encontraron o se vaciaron.");

        var fallan = vectores
            .Where(v => ComisionDeServicio.Sobre(Money.Of(v.Subtotal, "COP"), v.FeePercent) != Money.Of(v.Fee, "COP"))
            .Select(v => v.Nombre)
            .ToList();

        Assert.True(fallan.Count == 0, "La comisión del orquestador no da estos vectores: " + string.Join("; ", fallan));
    }

    [Fact]
    public void La_comision_va_en_la_moneda_del_subtotal()
    {
        Assert.Equal("USD", ComisionDeServicio.Sobre(Money.Of(100m, "USD"), 12m).Currency);
        Assert.Equal("USD", ComisionDeServicio.Sobre(Money.Of(100m, "USD"), 0m).Currency);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(12.5, true)]
    [InlineData(100, true)]
    [InlineData(-0.01, false)]
    [InlineData(100.01, false)]
    [InlineData(12.345, false)]
    public void Un_porcentaje_sirve_de_0_a_100_con_dos_decimales(double porcentaje, bool sirve)
        => Assert.Equal(sirve, ComisionDeServicio.Revisar((decimal)porcentaje) is null);
}
