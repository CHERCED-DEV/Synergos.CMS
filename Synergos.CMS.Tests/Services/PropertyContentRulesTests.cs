using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Catalog;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre las reglas que convierten un <c>propertyListing</c> autorado en una ficha servible
/// del portal inmobiliario (ADR 0118).
/// </summary>
/// <remarks>
/// <para>El eje aquí es distinto al de Eventos: <b>omitir un inmueble es caro</b>. Perder una
/// localidad de un evento deja las otras a la venta; perder una ficha es perder el activo que
/// el portal existe para mostrar. Así que estas pruebas fijan las dos mitades: qué se descarta
/// (lo que no se puede servir sin mentir) y qué se sirve degradado (todo lo demás).</para>
///
/// <para>La otra mitad es el <b>contrato con el mapa</b>: el módulo Angular solo dibuja el pin
/// si <c>lat !== 0 || lng !== 0</c>, así que (0, 0) significa "sin pin" y no "frente a
/// África". Estas pruebas lo fijan en el backend para que siga siendo cierto.</para>
/// </remarks>
public sealed class PropertyContentRulesTests
{
    private const string Cop = "COP";

    private static readonly PropertySpecLabels Labels = new(
        Area: "Área",
        Beds: "Habitaciones",
        Baths: "Baños",
        Parking: "Parqueaderos",
        Stratum: "Estrato",
        Age: "Antigüedad",
        Floor: "Piso");

    private static PropertyListingContent Content(
        string? slug = "apto-chico-norte",
        string? title = "Apartamento en Chicó Norte",
        string? operation = "venta",
        string? type = "apartamento",
        decimal price = 850_000_000m,
        string? city = "Bogotá",
        double? lat = 4.6736,
        double? lng = -74.0556)
        => new(slug, title, operation, type, price, City: city, Lat: lat, Lng: lng);

    private static PropertyDetail? Build(PropertyListingContent content)
        => PropertyContentRules.BuildListing(content, Cop, Labels).Value;

    // ── Lo que se descarta ───────────────────────────────────────────────────

    [Fact]
    public void Sin_slug_no_hay_ficha_porque_no_hay_enlace()
    {
        var result = PropertyContentRules.BuildListing(Content(slug: "   "), Cop, Labels);

        Assert.Null(result.Value);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void Sin_titulo_se_omite_en_vez_de_caer_al_nombre_del_nodo()
    {
        // "Inmueble (1)" en la tarjeta es peor que una ficha ausente: la ausencia se ve.
        Assert.Null(Build(Content(title: null)));
    }

    [Fact]
    public void Sin_ciudad_se_omite_porque_no_caeria_en_ninguna_faceta()
    {
        Assert.Null(Build(Content(city: "  ")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_precio_que_no_es_precio_se_omite_y_es_un_ERROR(decimal price)
    {
        // Un inmueble en cero se pinta como "Gratis". Es la misma regla que ya rechaza el
        // borrador de un agente: una sola, a los dos lados.
        var result = PropertyContentRules.BuildListing(Content(price: price), Cop, Labels);

        Assert.Null(result.Value);
        Assert.Contains(result.Issues, i => i.Level == EventContentIssueLevel.Error);
    }

    // ── Lo que se sirve degradado ────────────────────────────────────────────

    [Fact]
    public void Una_operacion_desconocida_cae_a_venta_y_avisa()
    {
        // La operación elige pantalla y copy, no precio: servido en la pestaña equivocada se
        // ve; ausente, no.
        var result = PropertyContentRules.BuildListing(Content(operation: "permuta"), Cop, Labels);

        Assert.Equal(PropertyContentRules.DefaultOperation, result.Value!.Summary.Operation);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void Un_tipo_vacio_NO_se_inventa()
    {
        // Inventar "apartamento" haría que quien filtra por apartamento acabe viendo un lote.
        var result = PropertyContentRules.BuildListing(Content(type: null), Cop, Labels);

        Assert.NotNull(result.Value);
        Assert.Equal(string.Empty, result.Value!.Summary.Type);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void Un_entero_negativo_es_una_errata_y_vale_cero_sin_perder_la_ficha()
    {
        var content = Content() with { Beds = -2, Baths = -1, AreaM2 = -80, Stratum = -4 };

        var detail = Build(content);

        Assert.NotNull(detail);
        Assert.Equal(0, detail!.Summary.Beds);
        Assert.Equal(0, detail.Summary.Baths);
        Assert.Equal(0, detail.Summary.AreaM2);
        Assert.Equal(0, detail.Summary.Stratum);
    }

    // ── Geo: el contrato con el mapa ─────────────────────────────────────────

    [Fact]
    public void Con_las_dos_coordenadas_en_rango_el_inmueble_sale_en_el_mapa()
    {
        var detail = Build(Content(lat: 4.6736, lng: -74.0556));

        Assert.Equal(4.6736, detail!.Summary.Lat);
        Assert.Equal(-74.0556, detail.Summary.Lng);
        Assert.Equal(4.6736, detail.Location.Lat);
    }

    [Fact]
    public void Sin_geo_el_inmueble_se_sirve_en_cero_cero_y_NO_se_descarta()
    {
        // (0, 0) es el contrato de "sin pin" que el módulo Angular ya respeta
        // (realty.ts: `listing.geo.lat !== 0 || listing.geo.lng !== 0`).
        var result = PropertyContentRules.BuildListing(Content(lat: null, lng: null), Cop, Labels);

        Assert.NotNull(result.Value);
        Assert.Equal(0d, result.Value!.Summary.Lat);
        Assert.Equal(0d, result.Value.Summary.Lng);
        // Y no avisa: no tener geo es legítimo, no un error del editor.
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Media_coordenada_no_es_medio_pin()
    {
        // Media coordenada pondría el pin en el meridiano de Greenwich.
        var result = PropertyContentRules.BuildListing(Content(lat: 4.6736, lng: null), Cop, Labels);

        Assert.Equal(0d, result.Value!.Summary.Lat);
        Assert.Equal(0d, result.Value.Summary.Lng);
        Assert.NotEmpty(result.Issues);
    }

    [Theory]
    [InlineData(95d, -74.05)]   // latitud imposible
    [InlineData(4.67, -200d)]   // longitud imposible
    public void Una_coordenada_fuera_de_rango_saca_al_inmueble_del_mapa_pero_no_del_portal(double lat, double lng)
    {
        // Una longitud tecleada en el campo de latitud manda el mapa al océano.
        var result = PropertyContentRules.BuildListing(Content(lat: lat, lng: lng), Cop, Labels);

        Assert.NotNull(result.Value);
        Assert.Equal(0d, result.Value!.Summary.Lat);
        Assert.NotEmpty(result.Issues);
    }

    // ── Características derivadas ────────────────────────────────────────────

    [Fact]
    public void Las_caracteristicas_salen_de_los_campos_tipados_y_en_orden()
    {
        var content = Content() with
        {
            AreaM2 = 92, Beds = 3, Baths = 2, Parking = 1, Stratum = 5,
            Age = " 5 años ", Floor = "Penthouse",
        };

        var specs = Build(content)!.Specs;

        Assert.Equal(
            new[] { "Área", "Habitaciones", "Baños", "Parqueaderos", "Estrato", "Antigüedad", "Piso" },
            specs.Select(s => s.Label));
        Assert.Equal("92 m²", specs[0].Value);
        Assert.Equal("5 años", specs[5].Value);
    }

    [Fact]
    public void Una_caracteristica_en_cero_o_vacia_NO_sale()
    {
        // "Parqueaderos: 0" es ruido en un apartaestudio, y "Estrato: 0" es directamente
        // falso — el estrato va de 1 a 6.
        var content = Content() with { AreaM2 = 40, Beds = 1, Baths = 1, Parking = 0, Stratum = 0, Age = "  " };

        var specs = Build(content)!.Specs;

        Assert.Equal(new[] { "Área", "Habitaciones", "Baños" }, specs.Select(s => s.Label));
    }

    [Fact]
    public void Las_etiquetas_vienen_de_afuera_y_no_estan_escritas_en_la_regla()
    {
        // Es lo que permite que vivan en el Dictionary uSync: si estuvieran hardcodeadas,
        // cambiar "Estrato" exigiría un despliegue.
        var english = new PropertySpecLabels("Area", "Bedrooms", "Bathrooms", "Parking", "Stratum", "Age", "Floor");
        var content = Content() with { AreaM2 = 92, Beds = 3 };

        var specs = PropertyContentRules.BuildSpecs(content, english);

        Assert.Equal(new[] { "Area", "Bedrooms" }, specs.Select(s => s.Label));
    }

    // ── Ficha ────────────────────────────────────────────────────────────────

    [Fact]
    public void La_portada_es_la_PRIMERA_foto_de_la_galeria()
    {
        var content = Content() with
        {
            Gallery = new[] { "/media/realty/a.jpg", "  ", "/media/realty/b.jpg" },
        };

        var detail = Build(content);

        Assert.Equal("/media/realty/a.jpg", detail!.Summary.ImageUrl);
        Assert.Equal(new[] { "/media/realty/a.jpg", "/media/realty/b.jpg" }, detail.Gallery);
    }

    [Fact]
    public void Sin_fotos_la_portada_va_VACIA_y_no_a_una_ruta_inventada()
    {
        // Una ruta inventada daría un 404 por cada tarjeta de la grilla.
        Assert.Equal(string.Empty, Build(Content())!.Summary.ImageUrl);
    }

    [Fact]
    public void Sin_direccion_se_arma_con_barrio_y_ciudad_sin_comas_colgantes()
    {
        var conBarrio = Build(Content() with { Neighborhood = "Chicó Norte" })!;
        var sinBarrio = Build(Content())!;

        Assert.Equal("Chicó Norte, Bogotá", conBarrio.Location.Address);
        Assert.Equal("Bogotá", sinBarrio.Location.Address);
    }

    [Fact]
    public void La_direccion_autorada_gana_sobre_la_armada()
    {
        var detail = Build(Content() with { Neighborhood = "Chicó Norte", Address = "Cra 11 #93-45" });

        Assert.Equal("Cra 11 #93-45", detail!.Location.Address);
    }

    [Fact]
    public void El_slug_es_el_id_y_se_normaliza()
    {
        var detail = Build(Content(slug: "  APTO-Chico-Norte  "));

        Assert.Equal("apto-chico-norte", detail!.Summary.Id);
        Assert.Equal("apto-chico-norte", detail.Summary.Slug);
    }

    [Fact]
    public void Las_amenidades_en_blanco_se_descartan()
    {
        var content = Content() with { Amenities = new[] { "Gimnasio", "   ", "Zona BBQ" } };

        Assert.Equal(new[] { "Gimnasio", "Zona BBQ" }, Build(content)!.Amenities);
    }

    [Fact]
    public void Un_contenido_nulo_es_un_error_de_programacion_y_lanza()
    {
        Assert.Throws<ArgumentNullException>(() => PropertyContentRules.BuildListing(null!, Cop, Labels));
        Assert.Throws<ArgumentNullException>(() => PropertyContentRules.BuildListing(Content(), Cop, null!));
    }
}
