using System.Globalization;
using System.Text;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;
using Xunit.Abstractions;

namespace Synergos.CMS.Tests;

public class ZzTempRefutationTests
{
    private readonly ITestOutputHelper _out;
    public ZzTempRefutationTests(ITestOutputHelper o) => _out = o;

    // Copia de CatalogText.Fold DEGRADADA: no quita NonSpacingMark.
    private static string DegradedFold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var composed = value.Trim().Normalize(NormalizationForm.FormC);
        return composed.ToLowerInvariant();
    }

    private static StubContentStream ContentStream()
    {
        var clock = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        return new StubContentStream(new StubSocialGraphService(), new StubReactionService(), () => clock);
    }
    private static ICourseCatalogProvider Academy() => new StubCourseCatalogProvider(ContentStream());

    [Fact]
    public void A_Diseno_NoTieneNingunNonSpacingMark_TrasProtegerLaEnye()
    {
        // La ruta FormD/NonSpacingMark que el test dice ejercitar.
        var d = "Diseño".Replace('ñ', (char)0xE000).Normalize(NormalizationForm.FormD);
        var marcas = d.Count(c => CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark);
        _out.WriteLine($"NonSpacingMark en 'Diseño' (ñ protegida) = {marcas}");
        Assert.Equal(0, marcas);

        var atomicos = "atómicos".Normalize(NormalizationForm.FormD);
        var marcas2 = atomicos.Count(c => CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark);
        _out.WriteLine($"NonSpacingMark en 'atómicos' = {marcas2}");
        Assert.Equal(1, marcas2);
    }

    [Fact]
    public void B_ConFoldDegradado_DisenoSiguePasando_PeroAtomicosNo()
    {
        // El test actual: "Diseño" vs "diseño" -> IGUALES incluso sin plegar tildes.
        Assert.Equal(DegradedFold("Diseño"), DegradedFold("diseño"));
        _out.WriteLine("DEGRADADO: Diseño == diseño -> el test seguiria VERDE");

        // El arreglo propuesto: "atómicos" vs "atomicos" -> DISTINTOS sin plegado.
        Assert.NotEqual(DegradedFold("atómicos"), DegradedFold("atomicos"));
        _out.WriteLine("DEGRADADO: atómicos != atomicos -> el test propuesto se pondria ROJO");

        // Y con el Fold real, ambos pares casan.
        Assert.Equal(CatalogText.Fold("Diseño"), CatalogText.Fold("diseño"));
        Assert.Equal(CatalogText.Fold("atómicos"), CatalogText.Fold("atomicos"));
    }

    [Fact]
    public async Task C_ElArregloPropuesto_EsVerdeHoy_ConDatosReales()
    {
        var sin = await Academy().SearchAsync(new CourseQuery(Text: "atomicos"));
        var con = await Academy().SearchAsync(new CourseQuery(Text: "atómicos"));
        _out.WriteLine($"atomicos -> {sin.Courses.Count} | atómicos -> {con.Courses.Count}");
        foreach (var c in sin.Courses) _out.WriteLine($"  {c.Id} :: {c.Title}");

        Assert.NotEmpty(sin.Courses);
        Assert.NotEmpty(con.Courses);
        Assert.Equal(con.Courses.Select(c => c.Id), sin.Courses.Select(c => c.Id));

        var res = await Academy().SearchAsync(new CourseQuery(Text: "resolucion"));
        _out.WriteLine($"resolucion -> {res.Courses.Count}");
        Assert.NotEmpty(res.Courses);

        // CONTROL: algo que no existe debe dar 0.
        var ctrl = await Academy().SearchAsync(new CourseQuery(Text: "xxnoexiste"));
        _out.WriteLine($"CONTROL xxnoexiste -> {ctrl.Courses.Count}");
        Assert.Empty(ctrl.Courses);
    }
}
