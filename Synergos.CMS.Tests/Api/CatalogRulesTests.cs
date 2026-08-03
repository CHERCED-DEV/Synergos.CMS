using Synergos.Api.Catalog.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Api;

/// <summary>Cubre <see cref="CatalogRules"/> — normalización, puntuación y filtros.</summary>
public sealed class CatalogRulesTests
{
    private static CatalogItem Item(string titulo, string? resumen = null, params string[] tags)
        => new("i1", Ref.Create("realty.inmueble", "x"), titulo, resumen, tags,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), DateTimeOffset.UnixEpoch);

    [Fact]
    public void La_normalizacion_quita_TILDES()
    {
        // En es-CO la gente busca "medellin" sin acento la mitad de las veces, y un índice que
        // distingue devuelve cero resultados para una consulta perfectamente razonable.
        Assert.Equal("medellin", CatalogRules.Normalize("Medellín"));
        Assert.Equal("bogota", CatalogRules.Normalize("  BOGOTÁ  "));
        Assert.Equal(CatalogRules.Normalize("Cundinamarca"), CatalogRules.Normalize("cundinamarca"));
    }

    [Fact]
    public void La_ene_tambien_se_pliega_al_COMPARAR()
    {
        // Esto se decidió dos veces. El primer intento preservaba la ñ —"no es un acento, es una
        // letra"— y levantando el servicio se vio el costo: buscar "diseno" no encontraba "Curso
        // de diseño". Cero resultados para una consulta normal es el peor desenlace de un
        // catálogo, y el usuario no puede corregirlo porque no sabe qué escribió mal.
        // Conflatar "año/ano" solo agrega un resultado de más.
        Assert.Equal("cabana", CatalogRules.Normalize("Cabaña"));
        Assert.Equal(CatalogRules.Normalize("diseño"), CatalogRules.Normalize("diseno"));
    }

    [Fact]
    public void Plegar_la_ene_NO_la_borra_de_lo_que_se_muestra()
    {
        // El costo es solo de comparación: lo que se pinta es Title sin tocar.
        var item = Item("Cabaña en Guatapé");

        Assert.True(CatalogRules.Score(item, CatalogRules.Normalize("cabana")) > 0);
        Assert.Equal("Cabaña en Guatapé", item.Title);
    }

    [Fact]
    public void El_TITULO_pesa_mas_que_el_resumen()
    {
        // Sin esa diferencia, un ítem que menciona la palabra de pasada en su descripción sale
        // por encima del que se llama exactamente así — la forma más rápida de que la búsqueda
        // parezca rota.
        var exacto = CatalogRules.Score(Item("Apartamento"), "apartamento");
        var enTitulo = CatalogRules.Score(Item("Apartamento en Laureles"), "apartamento");
        var soloResumen = CatalogRules.Score(Item("Casa", "un apartamento cerca"), "apartamento");

        Assert.True(exacto > enTitulo);
        Assert.True(enTitulo > soloResumen);
        Assert.True(soloResumen > 0);
    }

    [Fact]
    public void Lo_que_no_coincide_puntua_CERO_y_queda_fuera()
    {
        Assert.Equal(0, CatalogRules.Score(Item("Casa en Envigado"), "apartamento"));
    }

    [Fact]
    public void Sin_texto_todos_los_que_pasaron_los_filtros_valen_igual()
    {
        // Es lo que hace que "todos los inmuebles de Medellín" —sin texto— sea una búsqueda
        // legítima y no devuelva vacío.
        Assert.Equal(1, CatalogRules.Score(Item("Cualquier cosa"), ""));
    }

    [Fact]
    public void Una_etiqueta_exacta_puntua()
    {
        Assert.True(CatalogRules.Score(Item("Casa", null, "amoblado"), "amoblado") > 0);
    }

    [Fact]
    public void Una_consulta_TOTALMENTE_vacia_se_rechaza()
    {
        Assert.Equal("catalog.query_required", CatalogRules.CheckQuery(null, null, null)?.Code);
    }

    [Fact]
    public void Basta_UNA_de_las_tres_para_que_la_consulta_valga()
    {
        var tags = new[] { "amoblado" };
        var facets = new Dictionary<string, string> { ["ciudad"] = "medellin" };

        Assert.Null(CatalogRules.CheckQuery("casa", null, null));
        Assert.Null(CatalogRules.CheckQuery(null, tags, null));
        Assert.Null(CatalogRules.CheckQuery(null, null, facets));
    }

    [Fact]
    public void Un_titulo_vacio_o_larguisimo_no_se_indexa()
    {
        Assert.Equal("catalog.bad_title", CatalogRules.CheckIndexable("  ", null, null)?.Code);
        Assert.Equal("catalog.bad_title",
            CatalogRules.CheckIndexable(new string('x', CatalogRules.MaxTitleLength + 1), null, null)?.Code);
    }

    [Fact]
    public void Las_etiquetas_y_facetas_tienen_tope()
    {
        var tags = Enumerable.Range(0, CatalogRules.MaxTags + 1).Select(i => $"t{i}").ToList();
        var facets = Enumerable.Range(0, CatalogRules.MaxFacets + 1).ToDictionary(i => $"f{i}", _ => "v");

        Assert.Equal("catalog.too_many_tags", CatalogRules.CheckIndexable("ok", tags, null)?.Code);
        Assert.Equal("catalog.too_many_facets", CatalogRules.CheckIndexable("ok", null, facets)?.Code);
    }
}
