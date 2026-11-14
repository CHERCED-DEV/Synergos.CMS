using System.Text.Json.Nodes;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// <see cref="CuerpoEditable"/> (016-contenido-piezas): cada operación sobre el cuerpo de una página es idempotente y
/// «desconectar» quita el bloque de la página con todo lo suyo, sin tocar lo demás.
/// </summary>
public sealed class CuerpoEditableTests
{
    private static readonly Guid Seccion = Guid.Parse("1c68f4a9-24e9-49ac-9efa-05b3d4b1404a");
    private static readonly Guid Portal = Guid.Parse("f0d2487c-b18e-4e18-aa9b-3116264e89c5");
    private static readonly Guid Rejilla = Guid.Parse("9cea6ea5-b582-4a1e-954d-692a850221d7");
    private static readonly Guid Area = Guid.Parse("3525d41c-ae84-47ac-9297-2148f6a4aae8");

    /// <summary>Dos secciones: la primera con el portal, la segunda con una rejilla.</summary>
    private static string Portada()
    {
        var b = new BlockGridJsonBuilder();
        b.AddTopLevelBlock(Seccion).Set("cssClass", "uno").AddChild(Area, Portal, p => p.Set("heading", "Viajes"));
        b.AddTopLevelBlock(Seccion).Set("cssClass", "dos").AddChild(Area, Rejilla, r => r.Set("headingText", "Cómo funciona"));
        return b.Build();
    }

    private static string UnaSeccion(string clase)
    {
        var b = new BlockGridJsonBuilder();
        b.AddTopLevelBlock(Seccion).Set("cssClass", clase);
        return b.Build();
    }

    [Fact]
    public void Desconectar_quita_la_seccion_y_sus_datos_y_deja_lo_demas()
    {
        var c = new CuerpoEditable(Portada());

        Assert.Equal(1, c.DesconectarSeccionesCon(Portal));

        var cuerpo = JsonNode.Parse(c.Json)!;
        Assert.Single((JsonArray)cuerpo["layout"]!["Umbraco.BlockGrid"]!);
        Assert.DoesNotContain("Viajes", c.Json, StringComparison.Ordinal);
        Assert.Contains("Cómo funciona", c.Json, StringComparison.Ordinal);
        Assert.Equal(2, ((JsonArray)cuerpo["contentData"]!).Count);
        Assert.Equal(0, c.DesconectarSeccionesCon(Portal));
    }

    [Fact]
    public void Reemplazar_dos_veces_cambia_una_sola_vez()
    {
        var c = new CuerpoEditable(Portada());

        Assert.Equal(1, c.Reemplazar(Rejilla, "headingText", "Así funciona"));
        Assert.Equal(0, c.Reemplazar(Rejilla, "headingText", "Así funciona"));
        Assert.Equal(1, c.Cambios);
    }

    [Fact]
    public void Insertar_antes_de_un_bloque_lo_pone_en_su_area_y_no_lo_repite()
    {
        var c = new CuerpoEditable(Portada());
        var encabezado = new BlockGridJsonBuilder();
        encabezado.AddTopLevelBlock(Seccion).Set("cssClass", "titulo");

        Assert.True(c.InsertarAntesDe(Rejilla, encabezado.Build(), "titulo"));
        Assert.False(c.InsertarAntesDe(Rejilla, encabezado.Build(), "titulo"));

        var area = (JsonArray)JsonNode.Parse(c.Json)!["layout"]!["Umbraco.BlockGrid"]![1]!["areas"]![0]!["items"]!;
        Assert.Equal(2, area.Count);
    }

    [Fact]
    public void Arriba_y_antes_de_una_seccion_respetan_el_orden()
    {
        var c = new CuerpoEditable(Portada());

        c.InsertarAlPrincipio(UnaSeccion("primera"));
        c.InsertarSeccionesAntesDe(Rejilla, UnaSeccion("antes-de-la-rejilla"));
        c.Sumar(UnaSeccion("ultima"));

        Assert.Equal(new[] { "primera", "uno", "antes-de-la-rejilla", "dos", "ultima" }, OrdenDeSecciones(c));
    }

    [Fact]
    public void Transformar_una_lista_guardada_como_texto_la_deja_como_texto_y_no_repite()
    {
        var b = new BlockGridJsonBuilder();
        b.AddTopLevelBlock(Seccion).AddChild(Area, Rejilla, r => r.Set("itemsJson", "[{\"author\":\"Nombre Inventado\"}]"));
        var c = new CuerpoEditable(b.Build());

        static JsonArray PorRol(JsonArray l) => new(l.Select(t => (JsonNode)new JsonObject { ["author"] = "Compradora" }).ToArray());

        Assert.Equal(1, c.TransformarLista(Rejilla, "itemsJson", PorRol));
        Assert.Equal(0, c.TransformarLista(Rejilla, "itemsJson", PorRol));
        Assert.Contains("Compradora", c.Json, StringComparison.Ordinal);
        Assert.DoesNotContain("Inventado", c.Json, StringComparison.Ordinal);
    }

    [Fact]
    public void Desconectar_con_filtro_quita_solo_la_seccion_que_casa()
    {
        var b = new BlockGridJsonBuilder();
        b.AddTopLevelBlock(Seccion).AddChild(Area, Rejilla, r => r.Set("headingText", "Cursos destacados"));
        b.AddTopLevelBlock(Seccion).AddChild(Area, Rejilla, r => r.Set("headingText", "Por qué aprender con nosotros"));
        var c = new CuerpoEditable(b.Build());

        Assert.Equal(1, c.DesconectarSeccionesCon(Rejilla, CuerpoEditable.Donde("headingText", "Cursos destacados")));
        Assert.DoesNotContain("Cursos destacados", c.Json, StringComparison.Ordinal);
        Assert.Contains("Por qué aprender con nosotros", c.Json, StringComparison.Ordinal);
    }

    [Fact]
    public void Reemplazar_con_filtro_cambia_solo_el_bloque_que_casa()
    {
        var b = new BlockGridJsonBuilder();
        b.AddTopLevelBlock(Seccion).AddChild(Area, Rejilla, r => r.Set("headingText", "Bienestar"));
        b.AddTopLevelBlock(Seccion).AddChild(Area, Rejilla, r => r.Set("headingText", "Espacios"));
        var c = new CuerpoEditable(b.Build());

        Assert.Equal(1, c.Reemplazar(Rejilla, "columns", "4", CuerpoEditable.Donde("headingText", "Bienestar")));

        var datos = ((JsonArray)JsonNode.Parse(c.Json)!["contentData"]!).OfType<JsonObject>()
            .Where(d => d["headingText"] is not null).ToDictionary(d => d["headingText"]!.GetValue<string>());
        Assert.Equal("4", datos["Bienestar"]["columns"]!.GetValue<string>());
        Assert.Null(datos["Espacios"]["columns"]);
    }

    [Fact]
    public void Transformar_con_filtro_toca_solo_la_lista_del_bloque_que_casa()
    {
        var b = new BlockGridJsonBuilder();
        b.AddTopLevelBlock(Seccion).AddChild(Area, Rejilla, r => r.Set("headingText", "Ficha técnica").Set("itemsJson", "[{\"heading\":\"Dormitorios\"}]"));
        b.AddTopLevelBlock(Seccion).AddChild(Area, Rejilla, r => r.Set("headingText", "Por qué buscar con nosotros").Set("itemsJson", "[{\"heading\":\"Listado curado\"}]"));
        var c = new CuerpoEditable(b.Build());

        var n = c.TransformarLista(Rejilla, "itemsJson",
            _ => new JsonArray(new JsonObject { ["heading"] = "Área", ["body"] = "140 m²" }),
            CuerpoEditable.Donde("headingText", "Ficha técnica"));

        Assert.Equal(1, n);
        Assert.DoesNotContain("Dormitorios", c.Json, StringComparison.Ordinal);
        Assert.Contains("140 m²", c.Json, StringComparison.Ordinal);
        Assert.Contains("Listado curado", c.Json, StringComparison.Ordinal);
    }

    [Fact]
    public void Cambiar_botones_cambia_rotulo_y_enlace_en_su_sitio_y_repetirlo_no_cambia_nada()
    {
        var botones = new JsonObject
        {
            ["layout"] = new JsonObject { ["Umbraco.BlockList"] = new JsonArray(new JsonObject { ["contentUdi"] = "umb://element/b1" }) },
            ["contentData"] = new JsonArray(new JsonObject
            {
                ["udi"] = "umb://element/b1",
                ["ctaLabel"] = "Registrarme",
                ["ctaLink"] = new JsonArray(new JsonObject { ["name"] = "Registrarme", ["url"] = "/eventos/evento#registro" }),
            }),
        };
        var b = new BlockGridJsonBuilder();
        b.AddTopLevelBlock(Seccion).AddChild(Area, Portal, p => p.Set("ctaItems", botones.ToJsonString()));
        var c = new CuerpoEditable(b.Build());
        var enlace = new JsonArray(new JsonObject { ["name"] = "Comprar entradas", ["url"] = "/eventos/#/eventos/e/evt-cumbre-tech" }).ToJsonString();

        Assert.Equal(1, c.CambiarBotones(Portal, "ctaItems", [("Comprar entradas", enlace), ("De más", enlace)]));
        Assert.Equal(0, c.CambiarBotones(Portal, "ctaItems", [("Comprar entradas", enlace)]));

        var dato = ((JsonArray)JsonNode.Parse(c.Json)!["contentData"]!).Single(d => d!["ctaItems"] is not null)!;
        var lista = JsonNode.Parse(dato["ctaItems"]!.GetValue<string>())!;
        var boton = lista["contentData"]![0]!;
        Assert.Equal("umb://element/b1", boton["udi"]!.GetValue<string>());
        Assert.Equal("Comprar entradas", boton["ctaLabel"]!.GetValue<string>());
        Assert.Equal("/eventos/#/eventos/e/evt-cumbre-tech", boton["ctaLink"]![0]!["url"]!.GetValue<string>());
        Assert.Single((JsonArray)lista["contentData"]!);
    }

    private static string[] OrdenDeSecciones(CuerpoEditable c)
    {
        var cuerpo = JsonNode.Parse(c.Json)!;
        var datos = ((JsonArray)cuerpo["contentData"]!).ToDictionary(d => d!["udi"]!.GetValue<string>(), d => d!["cssClass"]?.GetValue<string>() ?? "");
        return ((JsonArray)cuerpo["layout"]!["Umbraco.BlockGrid"]!).Select(i => datos[i!["contentUdi"]!.GetValue<string>()]).ToArray();
    }
}
