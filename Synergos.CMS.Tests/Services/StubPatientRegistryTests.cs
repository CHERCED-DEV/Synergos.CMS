using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubPatientRegistry"/> (seam <see cref="IPatientRegistry"/>,
/// padrón del dashboard EHR-lite — OLA 5): los 4 casos canónicos (ADR 0075) —
/// empty / happy / filter / idempotent.
/// </summary>
public class StubPatientRegistryTests
{
    private static IPatientRegistry Make() => new StubPatientRegistry();

    [Fact] // empty: query vacío devuelve el padrón completo ordenado por nombre
    public async Task Search_EmptyQuery_ReturnsAllOrdered()
    {
        var all = await Make().SearchAsync(null);

        Assert.NotEmpty(all);
        var names = all.Select(p => p.FullName).ToList();
        Assert.Equal(names.OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase), names);
    }

    [Fact] // happy: buscar por nombre devuelve el paciente
    public async Task Search_ByName_FindsPatient()
    {
        var matches = await Make().SearchAsync("Camila");

        Assert.Contains(matches, p => p.FullName.Contains("Camila"));
    }

    [Fact] // filter: buscar por documento filtra (no devuelve todo)
    public async Task Search_ByDocument_Filters()
    {
        var registry = Make();
        var all = await registry.SearchAsync(null);

        var matches = await registry.SearchAsync("79.521.336"); // Jorge Medina

        Assert.Single(matches);
        Assert.True(matches.Count < all.Count);
        Assert.Equal("Jorge Medina", matches[0].FullName);
    }

    [Fact] // filter: query sin coincidencias devuelve vacío (no lanza)
    public async Task Search_NoMatch_ReturnsEmpty()
    {
        var matches = await Make().SearchAsync("zzz-no-existe");
        Assert.Empty(matches);
    }

    [Fact] // happy: Get por id devuelve la ficha demográfica completa
    public async Task Get_ExistingId_ReturnsPatient()
    {
        var patient = await Make().GetAsync("pat-jorge-medina");

        Assert.NotNull(patient);
        Assert.Equal("Jorge Medina", patient!.FullName);
        Assert.Contains("Hipertensión arterial", patient.ChronicConditions);
        Assert.False(string.IsNullOrWhiteSpace(patient.BloodType));
    }

    [Fact] // empty: Get de un id inexistente devuelve null (no lanza)
    public async Task Get_UnknownId_ReturnsNull()
    {
        Assert.Null(await Make().GetAsync("pat-no-existe"));
    }

    [Fact] // idempotent: dos Get del mismo id devuelven la misma ficha
    public async Task Get_Twice_IsIdempotent()
    {
        var registry = Make();
        var first = await registry.GetAsync("pat-camila-restrepo");
        var second = await registry.GetAsync("pat-camila-restrepo");

        Assert.Equal(first, second);
    }
}
