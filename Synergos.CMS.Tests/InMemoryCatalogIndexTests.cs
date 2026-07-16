using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests;

/// <summary>
/// El motor transversal de catálogo (T5 Ola 0). Cada test aquí corresponde a un bug REAL
/// verificado en vivo antes de escribir el motor, o a una decisión de diseño que el ADR 0107
/// registra y que un refactor futuro no debe deshacer sin darse cuenta.
/// </summary>
public class InMemoryCatalogIndexTests
{
    // ── El catálogo de prueba: mínimo, pero con las trampas del dato real (tildes) ──
    private sealed record Item(string Id, string Name, string Brand, string Category, double Rating, decimal Price);

    private static readonly Item Laptop = new("p1", "Laptop Pro 14", "Aurora", "Tecnología", 4.8, 5_200_000m);
    private static readonly Item Cafetera = new("p2", "Cafetera Espresso", "Barista", "Hogar", 4.2, 890_000m);
    private static readonly Item Audifonos = new("p3", "Audífonos Studio", "Aurora", "Tecnología", 3.5, 450_000m);
    private static readonly Item Zapatos = new("p4", "Zapatos de cuero", "Trail", "Moda", 4.9, 320_000m);

    private static readonly IReadOnlyList<Item> Catalog = new[] { Laptop, Cafetera, Audifonos, Zapatos };

    private static InMemoryCatalogIndex<Item> Make(CatalogSettings? settings = null)
        => new(Descriptor(), settings ?? new CatalogSettings());

    private static CatalogDescriptor<Item> Descriptor() => new(
        idOf: i => i.Id,
        searchFields: new[]
        {
            new CatalogSearchField<Item>(5, i => i.Name),
            new CatalogSearchField<Item>(3, i => i.Brand),
            new CatalogSearchField<Item>(2, i => i.Category),
        },
        defaultOrder: items => items.OrderByDescending(i => i.Rating),
        filters: new CatalogFilter<Item>[]
        {
            new CatalogTermFilter<Item>("brand", "Marca", i => new[] { i.Brand }),
            new CatalogTermFilter<Item>("category", "Categoría", i => new[] { i.Category }, CatalogFacetKind.SingleSelect),
            new CatalogThresholdFilter<Item>("minRating", "Calificación", i => i.Rating, new[]
            {
                new CatalogThresholdBucket(4.5, "4.5 estrellas o más"),
                new CatalogThresholdBucket(4.0, "4 estrellas o más"),
                new CatalogThresholdBucket(3.0, "3 estrellas o más"),
            }),
        },
        sorts: new Dictionary<string, Func<IEnumerable<Item>, IOrderedEnumerable<Item>>>
        {
            ["price-asc"] = items => items.OrderBy(i => i.Price),
            ["price-desc"] = items => items.OrderByDescending(i => i.Price),
        });

    private static CatalogQuery WithFilter(string field, params string[] values)
        => new(Filters: new Dictionary<string, IReadOnlyList<string>> { [field] = values });

    // ── EL bug que motivó todo: multi-select ────────────────────────────────────────

    [Fact] // Verificado en vivo: ?brand=Aurora→1, ?brand=Barista→1, ?brand=Aurora,Barista→0.
    public void MultiSelect_DosValores_DevuelveLaUnion_NoCero()
    {
        var index = Make();

        var aurora = index.Search(Catalog, WithFilter("brand", "Aurora"));
        var barista = index.Search(Catalog, WithFilter("brand", "Barista"));
        var ambas = index.Search(Catalog, WithFilter("brand", "Aurora", "Barista"));

        Assert.Equal(2, aurora.Total);
        Assert.Equal(1, barista.Total);
        // Lo que fallaba: filtrar por dos marcas devolvía MENOS que por una.
        Assert.Equal(3, ambas.Total);
    }

    [Fact] // AND entre facetas distintas, OR dentro de una.
    public void FacetasDistintas_SeCombinanConAnd()
    {
        var index = Make();

        var query = new CatalogQuery(Filters: new Dictionary<string, IReadOnlyList<string>>
        {
            ["brand"] = new[] { "Aurora", "Barista" },   // 3 ítems
            ["category"] = new[] { "Tecnología" },       // ∩ → solo los 2 de Aurora
        });

        var result = index.Search(Catalog, query);

        Assert.Equal(2, result.Total);
        Assert.All(result.Items, i => Assert.Equal("Tecnología", i.Category));
    }

    [Fact] // El chip puede venir de la URL sin tildes y el dato tenerlas.
    public void Faceta_CasaIgnorandoTildes()
        => Assert.Equal(2, Make().Search(Catalog, WithFilter("category", "tecnologia")).Total);

    // ── Threshold: el filtro que moría en silencio ──────────────────────────────────

    [Fact] // Antes: double.TryParse("4.0,4.5") falla → filtro no aplicado EN SILENCIO.
    public void Threshold_DosValores_GanaElMenosRestrictivo_NoSeDescarta()
    {
        var index = Make();

        var solo45 = index.Search(Catalog, WithFilter("minRating", "4.5"));
        var ambos = index.Search(Catalog, WithFilter("minRating", "4.5", "4.0"));

        Assert.Equal(2, solo45.Total);                       // Laptop 4.8 + Zapatos 4.9
        Assert.Equal(3, ambos.Total);                        // + Cafetera 4.2 → "4.0 o más"
        Assert.DoesNotContain(ambos.Items, i => i.Id == "p3"); // Audífonos 3.5 sigue fuera
    }

    [Fact] // Un valor basura no debe VACIAR el listado.
    public void Threshold_ValorNoParseable_NoFiltra()
        => Assert.Equal(4, Make().Search(Catalog, WithFilter("minRating", "cuatro")).Total);

    // ── Facetas: drill-down + conteo sobre el universo, no sobre la página ──────────

    [Fact] // Sin esto, encender "Aurora" colapsa la columna Marca a un solo chip.
    public void Facetas_DrillDown_LaFacetaSeleccionadaNoSeFiltraASiMisma()
    {
        var result = Make().Search(Catalog, WithFilter("brand", "Aurora"));

        var brand = result.Facets.Single(f => f.Field == "brand");

        // Los hermanos siguen visibles CON su conteo real: es lo que permite añadir Barista.
        Assert.Equal(3, brand.Values.Count);
        Assert.Equal(2, brand.Values.Single(v => v.Value == "Aurora").Count);
        Assert.Equal(1, brand.Values.Single(v => v.Value == "Barista").Count);

        // Pero las OTRAS facetas sí se acotan a la selección de marca.
        var category = result.Facets.Single(f => f.Field == "category");
        Assert.DoesNotContain(category.Values, v => v.Value == "Moda");   // Trail quedó fuera
    }

    [Fact] // El conteo describe el total, no el tamaño de la página.
    public void Facetas_CuentanSobreElUniversoFiltrado_NoSobreLaPagina()
    {
        var many = Enumerable.Range(0, 30)
            .Select(n => new Item($"p{n:00}", $"Item {n}", "Aurora", "Tecnología", 4.0, 1000m))
            .ToList();

        var result = Make().Search(many, new CatalogQuery(Take: 5));

        Assert.Equal(5, result.Items.Count);    // la página
        Assert.Equal(30, result.Total);         // el total
        var brand = result.Facets.Single(f => f.Field == "brand");
        Assert.Equal(30, brand.Values.Single(v => v.Value == "Aurora").Count);   // NO 5
    }

    [Fact] // Buscar "laptop" y ver "Barista (1)" sería mentira: no hay laptops Barista.
    public void Facetas_ElTextoAcotaElUniverso()
    {
        var result = Make().Search(Catalog, new CatalogQuery(Text: "laptop"));

        var brand = result.Facets.Single(f => f.Field == "brand");
        Assert.Single(brand.Values);
        Assert.Equal("Aurora", brand.Values[0].Value);
    }

    [Fact] // El label existe porque Value y Label no coinciden: "4.0" se lee "4 estrellas o más".
    public void Facetas_ThresholdTraeLabelLegible()
    {
        var rating = Make().Search(Catalog, new CatalogQuery()).Facets.Single(f => f.Field == "minRating");

        var chip = rating.Values.Single(v => v.Value == "4");
        Assert.Equal("4 estrellas o más", chip.Label);
        Assert.Equal(3, chip.Count);   // 4.8, 4.2, 4.9
    }

    [Fact] // El Kind viaja al cliente: la UI pinta radios o checkboxes según esto.
    public void Facetas_ExponenSuKind()
    {
        var facets = Make().Search(Catalog, new CatalogQuery()).Facets;

        Assert.Equal(CatalogFacetKind.MultiSelect, facets.Single(f => f.Field == "brand").Kind);
        Assert.Equal(CatalogFacetKind.SingleSelect, facets.Single(f => f.Field == "category").Kind);
        Assert.Equal(CatalogFacetKind.Threshold, facets.Single(f => f.Field == "minRating").Kind);
    }

    // ── Texto: el bug de tildes + prefijo + AND ────────────────────────────────────

    [Fact] // EL bug vivo, ahora a través del motor.
    public void Texto_IgnoraTildes()
        => Assert.Equal(2, Make().Search(Catalog, new CatalogQuery(Text: "tecnologia")).Total);

    [Fact] // Ni .Contains ni el GroupedOr de Examine dan prefijo.
    public void Texto_CasaPorPrefijo()
    {
        var result = Make().Search(Catalog, new CatalogQuery(Text: "zapa"));

        Assert.Equal(1, result.Total);
        Assert.Equal("p4", result.Items[0].Id);
    }

    [Fact] // "ola" NO debe encontrar "chocolate": se casa por palabra, no por substring.
    public void Texto_NoCasaEnMitadDeUnaPalabra()
        => Assert.Equal(0, Make().Search(Catalog, new CatalogQuery(Text: "udio")).Total);

    [Fact] // AND entre tokens: el OR de Lucene devolvería medio catálogo.
    public void Texto_ExigeTodosLosTokens()
    {
        Assert.Equal(1, Make().Search(Catalog, new CatalogQuery(Text: "laptop pro")).Total);
        // "laptop" existe y "cafetera" existe, pero ningún ítem tiene ambos.
        Assert.Equal(0, Make().Search(Catalog, new CatalogQuery(Text: "laptop cafetera")).Total);
    }

    [Fact] // Con texto manda la relevancia: el nombre (peso 5) gana a la marca (peso 3).
    public void Texto_OrdenaPorRelevancia_ElCampoDeMasPesoPrimero()
    {
        var items = new[]
        {
            new Item("a", "Cafetera Aurora", "Genérica", "Hogar", 1.0, 100m),   // casa en Name (5)
            new Item("b", "Tostadora", "Aurora", "Hogar", 5.0, 100m),           // casa en Brand (3)
        };

        var result = Make().Search(items, new CatalogQuery(Text: "aurora"));

        Assert.Equal(2, result.Total);
        // Gana el del nombre pese a tener PEOR rating: con texto, manda la relevancia.
        Assert.Equal("a", result.Items[0].Id);
    }

    [Fact] // La puntuación no debe partir "pro-14" de "pro 14".
    public void Texto_LaPuntuacionNoParteLaBusqueda()
        => Assert.Equal(1, Make().Search(Catalog, new CatalogQuery(Text: "pro-14")).Total);

    // ── Orden y paginación ─────────────────────────────────────────────────────────

    [Fact] // Sin texto, la demo NO debe cambiar de aspecto: manda el orden histórico.
    public void SinTexto_UsaElOrdenPorDefectoDelVertical()
    {
        var result = Make().Search(Catalog, new CatalogQuery());

        Assert.Equal(new[] { "p4", "p1", "p2", "p3" }, result.Items.Select(i => i.Id));  // rating desc
    }

    [Fact]
    public void Sort_Declarado_SeAplica()
    {
        var result = Make().Search(Catalog, new CatalogQuery(Sort: "price-asc"));

        Assert.Equal(new[] { "p4", "p3", "p2", "p1" }, result.Items.Select(i => i.Id));
    }

    [Fact] // Un enlace viejo con un sort que ya no existe no merece un 400.
    public void Sort_Desconocido_CaeAlOrdenPorDefecto()
    {
        var conocido = Make().Search(Catalog, new CatalogQuery());
        var basura = Make().Search(Catalog, new CatalogQuery(Sort: "por-color-de-ojos"));

        Assert.Equal(conocido.Items.Select(i => i.Id), basura.Items.Select(i => i.Id));
    }

    [Fact] // Sin desempate total, la paginación baila entre requests.
    public void Orden_EsTotal_AunConValoresEmpatados()
    {
        // Todos con el MISMO rating: sin el desempate por id, el orden sería el del origen.
        var empatados = new[]
        {
            new Item("z", "Z", "M", "C", 4.0, 1m),
            new Item("a", "A", "M", "C", 4.0, 1m),
            new Item("m", "M", "M", "C", 4.0, 1m),
        };

        var first = Make().Search(empatados, new CatalogQuery());
        // AsEnumerable: el Array.Reverse estático (void, in-place) sombrea al de LINQ.
        var second = Make().Search(empatados.AsEnumerable().Reverse().ToList(), new CatalogQuery());

        Assert.Equal(new[] { "a", "m", "z" }, first.Items.Select(i => i.Id));
        // El mismo resultado aunque el origen venga en otro orden: eso es ser TOTAL.
        Assert.Equal(first.Items.Select(i => i.Id), second.Items.Select(i => i.Id));
    }

    [Fact]
    public void Paginacion_DevuelvePaginasDistintas_ConTotalEstable()
    {
        var index = Make();

        var page1 = index.Search(Catalog, new CatalogQuery(Skip: 0, Take: 2));
        var page2 = index.Search(Catalog, new CatalogQuery(Skip: 2, Take: 2));

        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);
        Assert.Empty(page1.Items.Select(i => i.Id).Intersect(page2.Items.Select(i => i.Id)));
        Assert.Equal(4, page1.Total);
        Assert.Equal(4, page2.Total);   // el total NO es el de la página
    }

    [Fact] // ?take=1000000 no debe materializar el catálogo entero.
    public void Paginacion_TakeSeCapaAMaxTake()
    {
        var many = Enumerable.Range(0, 50)
            .Select(n => new Item($"p{n:00}", $"Item {n}", "Aurora", "C", 4.0, 1m)).ToList();

        var result = new InMemoryCatalogIndex<Item>(Descriptor(), new CatalogSettings { MaxTake = 10 })
            .Search(many, new CatalogQuery(Take: 1_000_000));

        Assert.Equal(10, result.Items.Count);
        Assert.Equal(50, result.Total);
    }

    [Fact]
    public void Paginacion_SkipNegativo_SeTrataComoCero()
        => Assert.Equal(4, Make().Search(Catalog, new CatalogQuery(Skip: -5)).Items.Count);

    [Fact] // Pedir más allá del final es una página vacía, no un error.
    public void Paginacion_SkipMasAllaDelFinal_DevuelvePaginaVacia()
    {
        var result = Make().Search(Catalog, new CatalogQuery(Skip: 1_000_000));

        Assert.Empty(result.Items);
        Assert.Equal(4, result.Total);   // pero el total sigue diciendo la verdad
    }

    // ── Vacíos y entradas raras ────────────────────────────────────────────────────

    [Fact]
    public void CatalogoVacio_NoLanza()
    {
        var result = Make().Search(Array.Empty<Item>(), new CatalogQuery(Text: "lo que sea"));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public void QueryVacia_DevuelveTodo()
        => Assert.Equal(4, Make().Search(Catalog, new CatalogQuery()).Total);

    [Fact] // Un ?color=rojo pegado a la URL no debe romper la página.
    public void FiltroDesconocido_SeIgnora_NoVaciaElResultado()
        => Assert.Equal(4, Make().Search(Catalog, WithFilter("color", "rojo")).Total);

    [Fact] // Un chip vacío llega de una UI que deseleccionó: no debe filtrar nada.
    public void FiltroConValoresVacios_NoFiltra()
        => Assert.Equal(4, Make().Search(Catalog, WithFilter("brand", "", "   ")).Total);

    [Fact] // Que el motor sea puro es lo que lo hace Singleton-safe por construcción.
    public void Search_EsPuro_DosLlamadasIdenticasDanLoMismo()
    {
        var index = Make();
        var query = new CatalogQuery(Text: "aurora", Sort: "price-asc");

        var first = index.Search(Catalog, query);
        var second = index.Search(Catalog, query);

        Assert.Equal(first.Items.Select(i => i.Id), second.Items.Select(i => i.Id));
        Assert.Equal(first.Total, second.Total);
    }
}
