using System;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubTramiteCatalogProvider"/> (seam
/// <c>ITramiteCatalogProvider</c>, catálogo de trámites del portal Gobierno): los 4
/// casos canónicos (ADR 0075) — empty (texto sin match / id desconocido) / happy
/// (catálogo sembrado + ficha con formulario seccionado data-driven) / filter (texto +
/// categoría, el caso central del catálogo) / idempotent (buscar dos veces = mismo
/// resultado).
/// </summary>
public class StubTramiteCatalogProviderTests
{
    private static StubTramiteCatalogProvider Make() => new();

    [Fact] // empty: texto sin match devuelve lista vacía (no lanza)
    public async Task Search_NoMatch_ReturnsEmpty()
    {
        var result = await Make().SearchAsync("zzz-no-existe", null);
        Assert.Empty(result);
    }

    [Fact] // empty: id desconocido o vacío devuelve null
    public async Task Get_Unknown_ReturnsNull()
    {
        var svc = Make();
        Assert.Null(await svc.GetAsync("trm-que-no-existe"));
        Assert.Null(await svc.GetAsync(string.Empty));
    }

    [Fact] // happy: sin filtros devuelve el catálogo sembrado ordenado por nombre
    public async Task Search_NoFilters_ReturnsSeededCatalog()
    {
        var result = await Make().SearchAsync(null, null);

        Assert.Equal(7, result.Count);
        Assert.Equal(result.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Select(t => t.Id), result.Select(t => t.Id));
        // Mezcla de gratuitos y con tasa (la tasa opcional es del dominio).
        Assert.Contains(result, t => t.FeeMinor == 0m);
        Assert.Contains(result, t => t.FeeMinor > 0m);
    }

    [Fact] // happy: la ficha trae pasos + elegibilidad + requisitos + formulario seccionado
    public async Task Get_ById_ReturnsDetailWithSectionedForm()
    {
        var detail = await Make().GetAsync("trm-pasaporte");

        Assert.NotNull(detail);
        Assert.Equal("Expedición de pasaporte", detail!.Summary.Name);
        Assert.Equal(189_000m, detail.Summary.FeeMinor);
        Assert.True(detail.RequiresAppointment);
        Assert.NotEmpty(detail.Steps);
        Assert.NotEmpty(detail.Eligibility);
        Assert.NotEmpty(detail.Required);
        Assert.NotEmpty(detail.FormDefinition.Sections);
        var fields = detail.FormDefinition.Sections.SelectMany(s => s.Fields).ToList();
        Assert.Contains(fields, f => f.Type == "select" && f.Options.Count > 0);
        Assert.Contains(fields, f => f.Required);
    }

    [Fact] // happy: la ficha también se resuelve por slug
    public async Task Get_BySlug_ReturnsSameTramite()
    {
        var svc = Make();
        var byId = await svc.GetAsync("trm-pasaporte");
        var bySlug = await svc.GetAsync("expedicion-pasaporte");

        Assert.NotNull(bySlug);
        Assert.Equal(byId!.Summary.Id, bySlug!.Summary.Id);
    }

    [Fact] // filter: categoría filtra en AND con el texto
    public async Task Search_ByCategory_FiltersResults()
    {
        var svc = Make();

        var identidad = await svc.SearchAsync(null, "Identidad");
        Assert.NotEmpty(identidad);
        Assert.All(identidad, t => Assert.Equal("Identidad", t.Category));

        var texto = await svc.SearchAsync("licencia", null);
        Assert.Single(texto);
        Assert.Equal("trm-licencia-conduccion", texto[0].Id);

        var cruzado = await svc.SearchAsync("licencia", "Identidad");
        Assert.Empty(cruzado);
    }

    [Fact] // idempotent: buscar dos veces devuelve el mismo resultado
    public async Task Search_Twice_IsIdempotent()
    {
        var svc = Make();
        var first = await svc.SearchAsync(null, "Identidad");
        var second = await svc.SearchAsync(null, "Identidad");

        Assert.Equal(first.Select(t => t.Id), second.Select(t => t.Id));
    }
}
