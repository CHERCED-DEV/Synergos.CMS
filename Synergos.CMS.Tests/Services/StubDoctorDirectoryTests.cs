using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubDoctorDirectory"/> (seam <see cref="IDoctorDirectory"/>,
/// directorio de médicos del EHR-lite — OLA 5): los 4 casos canónicos (ADR 0075) —
/// empty / happy / filter / idempotent.
/// </summary>
public class StubDoctorDirectoryTests
{
    private static IDoctorDirectory Make() => new StubDoctorDirectory();

    [Fact] // empty: sin filtro devuelve todo el staff ordenado por nombre
    public async Task List_NoFilter_ReturnsAllOrdered()
    {
        var all = await Make().ListAsync();

        Assert.NotEmpty(all);
        var names = all.Select(d => d.FullName).ToList();
        Assert.Equal(names.OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase), names);
    }

    [Fact] // filter: por especialidad devuelve solo los de esa especialidad
    public async Task List_BySpecialty_Filters()
    {
        var directory = Make();
        var all = await directory.ListAsync();

        var cardio = await directory.ListAsync("Cardiología");

        Assert.NotEmpty(cardio);
        Assert.True(cardio.Count < all.Count);
        Assert.All(cardio, d => Assert.Contains("Cardiología", d.Specialty));
    }

    [Fact] // filter: especialidad sin coincidencias devuelve vacío
    public async Task List_UnknownSpecialty_ReturnsEmpty()
    {
        Assert.Empty(await Make().ListAsync("Neurocirugía-inexistente"));
    }

    [Fact] // happy: Get por id devuelve el médico con su disponibilidad
    public async Task Get_ExistingId_ReturnsDoctor()
    {
        var doctor = await Make().GetAsync("doc-ana-rios");

        Assert.NotNull(doctor);
        Assert.Equal("Medicina Interna", doctor!.Specialty);
        Assert.NotEmpty(doctor.WorkingDays);
        Assert.True(doctor.SlotMinutes > 0);
        Assert.True(doctor.SlotEndHour > doctor.SlotStartHour);
    }

    [Fact] // empty: Get de un id inexistente devuelve null
    public async Task Get_UnknownId_ReturnsNull()
    {
        Assert.Null(await Make().GetAsync("doc-no-existe"));
    }

    [Fact] // idempotent: dos List devuelven el mismo conjunto
    public async Task List_Twice_IsIdempotent()
    {
        var directory = Make();
        var first = await directory.ListAsync();
        var second = await directory.ListAsync();

        Assert.Equal(first.Select(d => d.Id), second.Select(d => d.Id));
    }
}
