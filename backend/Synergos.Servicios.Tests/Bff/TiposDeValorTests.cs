using System.Text.Json;
using System.Text.Json.Nodes;
using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Los tipos de valor que un flujo declara: leen lo que tiene su forma, rechazan lo demás con el nombre
/// del campo, y escriben lo que leen (núcleo N1).
/// </summary>
public sealed class TiposDeValorTests
{
    private static readonly TiposDeValor Tipos = TiposDeValor.Con();

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static object? Leer(string tipo, string json) => Tipos.Resolver(tipo)!.Leer(J(json), "campo");

    public static TheoryData<string, string> IdaYVuelta() => new()
    {
        { "texto", "\"hola\"" },
        { "entero", "42" },
        { "decimal", "12.5" },
        { "booleano", "true" },
        { "instante", "\"2026-10-15T10:00:00.0000000-05:00\"" },
        { "ref", """{"kind":"salud.profesional","id":"p-1"}""" },
        { "dinero", """{"amount":50000,"currency":"COP"}""" },
        { "ventana", """{"start":"2026-10-15T10:00:00.0000000+00:00","end":"2026-10-15T10:30:00.0000000+00:00"}""" },
        { "fecha", "\"2026-10-15\"" },
    };

    [Theory]
    [MemberData(nameof(IdaYVuelta))]
    public void Lo_que_lee_lo_escribe_igual(string tipo, string json)
    {
        var resuelto = Tipos.Resolver(tipo)!;
        var valor = resuelto.Leer(J(json), "campo");
        var escrito = resuelto.Escribir(valor);

        // Por igualdad de JSON y no de texto: el escritor escapa «+» como +, que es el mismo JSON.
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), escrito), $"{tipo}: se leyó {json} y se escribió {escrito?.ToJsonString()}.");
        Assert.Equal(resuelto.Tipo.Clr, valor!.GetType());
    }

    public static TheoryData<string, string, string> Malos() => new()
    {
        { "texto", "3", "un texto" },
        { "entero", "1.5", "un entero" },
        // es-CO: el punto es de miles. En JSON un texto «500.000» no es un número, y no se adivina.
        { "decimal", "\"500.000\"", "un número" },
        { "booleano", "\"true\"", "true o false" },
        // sin desfase, el instante dependería del huso del servidor
        { "instante", "\"2026-10-15T10:00:00\"", "desfase" },
        { "ref", """{"kind":"x","id":"1","extra":1}""", "«extra»" },
        { "ref", """{"kind":"x","id":"  "}""", "campo.id" },
        { "dinero", """{"amount":1,"currency":"CO"}""", "ISO 4217" },
        { "dinero", """{"amount":"1","currency":"COP"}""", "campo.amount" },
        { "ventana", """{"start":"2026-10-15T10:00:00Z","end":"2026-10-15T10:00:00Z"}""", "después de su inicio" },
        // un día, no un instante; y sólo la forma ISO: «15/10/2026» se lee distinto en cada cultura
        { "fecha", "\"2026-10-15T10:00:00Z\"", "AAAA-MM-DD" },
        { "fecha", "\"15/10/2026\"", "AAAA-MM-DD" },
        { "fecha", "20261015", "AAAA-MM-DD" },
    };

    [Theory]
    [MemberData(nameof(Malos))]
    public void Lo_que_no_tiene_su_forma_se_rechaza_con_el_nombre_del_campo(string tipo, string json, string mensaje)
    {
        var ex = Assert.Throws<FormatException>(() => Leer(tipo, json));

        Assert.Contains("campo", ex.Message, StringComparison.Ordinal);
        Assert.Contains(mensaje, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void El_anulable_acepta_nulo_y_ausencia_y_el_texto_en_blanco_es_nulo()
    {
        Assert.Null(Leer("texto?", "null"));
        Assert.Null(Tipos.Resolver("dinero?")!.Leer(null, "campo"));
        Assert.Null(Leer("texto?", "\"   \""));
        Assert.Equal("hola", Leer("texto?", "\"hola\""));

        var ex = Assert.Throws<FormatException>(() => Leer("texto", "null"));
        Assert.Contains("hace falta", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void El_esquema_del_anulable_admite_null_como_lo_reconoce_el_suelo_del_contrato()
    {
        var escalar = Tipos.Resolver("texto?")!.Esquema();
        var objeto = Tipos.Resolver("dinero?")!.Esquema();

        Assert.Equal("""["string","null"]""", escalar["type"]!.ToJsonString());
        Assert.Equal("null", (string?)objeto["oneOf"]![1]!["type"]);
        Assert.Equal("object", (string?)objeto["oneOf"]![0]!["type"]);
        Assert.Equal("decimal", (string?)Tipos.Resolver("decimal")!.Esquema()["format"]);
    }

    [Fact]
    public void Una_lista_lee_cada_item_con_sus_campos_y_rechaza_los_que_no_declara()
    {
        var campos = new Dictionary<string, TipoDeCampo> { ["sujeto"] = new("ref"), ["cantidad"] = new("entero"), ["butaca"] = new("texto?") };

        var items = Tipos.LeerLista(J("""[{"sujeto":{"kind":"k","id":"1"},"cantidad":2}]"""), campos, "lineas");

        var item = Assert.Single(items);
        Assert.Equal(Ref.Create("k", "1"), item.Get<Ref>("sujeto"));
        Assert.Equal(2, item.Get<int>("cantidad"));
        Assert.Null(item.Get<string?>("butaca"));

        var ex = Assert.Throws<FormatException>(() =>
            Tipos.LeerLista(J("""[{"sujeto":{"kind":"k","id":"1"},"cantidad":2,"precio":5}]"""), campos, "lineas"));
        Assert.Contains("lineas[0]", ex.Message, StringComparison.Ordinal);
        Assert.Contains("«precio»", ex.Message, StringComparison.Ordinal);

        var esquema = Tipos.EsquemaDeLista(campos);
        Assert.Equal("""["sujeto","cantidad"]""", esquema["items"]!["required"]!.ToJsonString());
    }

    [Fact]
    public void Una_referencia_con_clase_lee_solo_esa_clase_y_la_publica_en_su_esquema()
    {
        // NR-1 de 008: la clase la declara la definición, como dato.
        var conClase = Tipos.Resolver("ref", "salud.profesional")!;

        Assert.Equal(Ref.Create("salud.profesional", "p-1"), conClase.Leer(J("""{"kind":"salud.profesional","id":"p-1"}"""), "profesional"));
        var ex = Assert.Throws<FormatException>(() => conClase.Leer(J("""{"kind":"otra.clase","id":"p-1"}"""), "profesional"));
        Assert.Contains("profesional.kind", ex.Message, StringComparison.Ordinal);
        Assert.Contains("«salud.profesional»", ex.Message, StringComparison.Ordinal);
        Assert.Equal("""["salud.profesional"]""", conClase.Esquema()["properties"]!["kind"]!["enum"]!.ToJsonString());
        Assert.Equal("ref", conClase.Declarado);

        var anulable = Tipos.Resolver("ref?", "salud.profesional")!;
        Assert.Null(anulable.Leer(null, "profesional"));
        Assert.Throws<FormatException>(() => anulable.Leer(J("""{"kind":"otra.clase","id":"p-1"}"""), "profesional"));
        Assert.Equal("""["salud.profesional"]""", anulable.Esquema()["oneOf"]![0]!["properties"]!["kind"]!["enum"]!.ToJsonString());

        // Sin clase (el sujeto, o lo que pone la configuración de negocio) no hay enum que publicar.
        Assert.Null(Tipos.Resolver("ref")!.Esquema()["properties"]!["kind"]!["enum"]);

        var campos = new Dictionary<string, TipoDeCampo> { ["recurso"] = new("ref", "salud.profesional") };
        var enLista = Assert.Throws<FormatException>(() => Tipos.LeerLista(J("""[{"recurso":{"kind":"otra.clase","id":"1"}}]"""), campos, "lineas"));
        Assert.Contains("lineas[0].recurso.kind", enLista.Message, StringComparison.Ordinal);
        Assert.Equal("""["salud.profesional"]""",
            Tipos.EsquemaDeLista(campos)["items"]!["properties"]!["recurso"]!["properties"]!["kind"]!["enum"]!.ToJsonString());
    }

    [Fact]
    public void Un_tipo_que_nadie_registro_no_se_resuelve_y_dos_con_el_mismo_nombre_no_conviven()
    {
        Assert.Null(Tipos.Resolver("moneda"));
        Assert.Throws<InvalidOperationException>(() => new TiposDeValor(TiposDeValor.Comunes.Concat([new Texto()])));
    }
}
